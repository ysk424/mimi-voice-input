import AppKit
import AVFoundation
import Darwin
import Foundation

private let version = "1.0.0"
private let defaultModel = "gpt-4o-mini-transcribe"
private let defaultPrompt = "自然な日本語の文章です。句読点を適切に使います。GPT、Codex、OpenAI、API、Swift、macOS。"
private let maximumRecordingSeconds = 60.0
private let minimumRecordingSeconds = 0.18

private var receivedSignal: sig_atomic_t = 0

private func handleTerminationSignal(_ signalNumber: Int32) {
    receivedSignal = signalNumber
}

private enum MimiError: LocalizedError {
    case apiKeyMissing
    case apiKeyReadFailed(String)
    case microphoneDenied
    case microphoneRestricted
    case microphoneUnavailable
    case terminalRequired
    case recordingFailed
    case recordingTooShort
    case interrupted
    case inputClosed
    case inputFailed(String)
    case fileReadFailed(String)
    case invalidResponse
    case apiError(Int, String)
    case networkError(String)

    var errorDescription: String? {
        switch self {
        case .apiKeyMissing:
            return "OPENAI_API_KEY が環境変数にも ~/.env にもありません。"
        case .apiKeyReadFailed(let detail):
            return "~/.env からOPENAI_API_KEYを読み取れません: \(detail)"
        case .microphoneDenied:
            return "マイクへのアクセスが許可されていません。システム設定の「プライバシーとセキュリティ」→「マイク」で、使用中のターミナルを許可してください。"
        case .microphoneRestricted:
            return "このMacではマイクの使用が制限されています。"
        case .microphoneUnavailable:
            return "マイクの使用許可を確認できませんでした。"
        case .terminalRequired:
            return "mimi は端末から実行してください。録音終了には Enter を使います。"
        case .recordingFailed:
            return "録音を開始できませんでした。"
        case .recordingTooShort:
            return "録音時間が短すぎたため送信しませんでした。"
        case .interrupted:
            return "録音を中止しました。"
        case .inputClosed:
            return "標準入力が閉じられたため録音を中止しました。"
        case .inputFailed(let detail):
            return "キーボード入力を読み取れませんでした: \(detail)"
        case .fileReadFailed(let detail):
            return "録音データを読み込めませんでした: \(detail)"
        case .invalidResponse:
            return "OpenAI APIの応答に文字起こし結果がありませんでした。"
        case .apiError(let status, let detail):
            return detail.isEmpty
                ? "OpenAI APIがエラーを返しました（HTTP \(status)）。"
                : "OpenAI APIエラー（HTTP \(status)）: \(detail)"
        case .networkError(let detail):
            return "OpenAI APIへ接続できませんでした: \(detail)"
        }
    }
}

private enum StopReason {
    case enter
    case timeout
}

private struct TranscriptionResponse: Decodable {
    let text: String
}

private struct APIErrorEnvelope: Decodable {
    struct APIErrorDetail: Decodable {
        let message: String?
    }

    let error: APIErrorDetail?
}

@main
private struct MimiCommand {
    static func main() async {
        // mano connects both stdout and stderr to the text insertion pipe.
        // Silence framework diagnostics there, then restore stderr for our own
        // error message if the command fails.
        let stderrSilencer = StderrSilencer(enabled: isatty(STDOUT_FILENO) == 0)

        do {
            let arguments = Array(CommandLine.arguments.dropFirst())

            if arguments == ["--help"] || arguments == ["-h"] {
                printHelp()
                return
            }

            if arguments == ["--version"] || arguments == ["-V"] {
                print("mimi \(version) (macOS)")
                return
            }

            if arguments == ["--check"] {
                printCheck()
                return
            }

            guard arguments.isEmpty else {
                throw MimiError.inputFailed("不明な引数です: \(arguments.joined(separator: " "))")
            }

            try await recordAndTranscribe()
        } catch {
            stderrSilencer.restore()
            writeError(error.localizedDescription)
            Darwin.exit(EXIT_FAILURE)
        }
    }

    private static func recordAndTranscribe() async throws {
        guard isatty(STDIN_FILENO) == 1 else {
            throw MimiError.terminalRequired
        }

        let environment = ProcessInfo.processInfo.environment
        let resolvedKey = try APIKeyProvider.resolve(environment: environment)
        guard !resolvedKey.value.isEmpty else {
            throw MimiError.apiKeyMissing
        }

        try await requireMicrophonePermission()
        removeOldRecordings()

        let recordingURL = try makeRecordingURL()
        defer {
            try? FileManager.default.removeItem(at: recordingURL)
        }

        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: 16_000,
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
            AVLinearPCMIsNonInterleaved: false
        ]

        let recorder = try AVAudioRecorder(url: recordingURL, settings: settings)
        guard recorder.prepareToRecord(), recorder.record() else {
            throw MimiError.recordingFailed
        }

        signal(SIGINT, handleTerminationSignal)
        signal(SIGTERM, handleTerminationSignal)

        let startedAt = Date()
        showStatus("録音中です。Enterで終了します（最長60秒）。")
        beep()

        let stopReason: StopReason
        do {
            stopReason = try waitForEnter(timeout: maximumRecordingSeconds)
        } catch {
            recorder.stop()
            signal(SIGINT, SIG_DFL)
            signal(SIGTERM, SIG_DFL)
            throw error
        }

        recorder.stop()
        signal(SIGINT, SIG_DFL)
        signal(SIGTERM, SIG_DFL)
        beep()

        let elapsed = Date().timeIntervalSince(startedAt)
        guard elapsed >= minimumRecordingSeconds else {
            throw MimiError.recordingTooShort
        }

        if stopReason == .timeout {
            showStatus("60秒に達したため録音を終了しました。文字起こし中です…")
        } else {
            showStatus("文字起こし中です…")
        }

        let model = nonempty(environment["MIMI_TRANSCRIBE_MODEL"]) ?? defaultModel
        let prompt = nonempty(environment["MIMI_TRANSCRIBE_PROMPT"]) ?? defaultPrompt
        let transcript = try await transcribe(
            recordingURL: recordingURL,
            apiKey: resolvedKey.value,
            model: model,
            prompt: prompt
        ).trimmingCharacters(in: .whitespacesAndNewlines)

        guard !transcript.isEmpty else {
            throw MimiError.invalidResponse
        }

        copyToClipboard(transcript)
        writeTranscript(transcript)
    }

    private static func requireMicrophonePermission() async throws {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            return
        case .notDetermined:
            let granted = await withCheckedContinuation { continuation in
                AVCaptureDevice.requestAccess(for: .audio) { allowed in
                    continuation.resume(returning: allowed)
                }
            }
            if !granted {
                throw MimiError.microphoneDenied
            }
        case .denied:
            throw MimiError.microphoneDenied
        case .restricted:
            throw MimiError.microphoneRestricted
        @unknown default:
            throw MimiError.microphoneUnavailable
        }
    }

    private static func makeRecordingURL() throws -> URL {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("mimi", isDirectory: true)
        try FileManager.default.createDirectory(
            at: directory,
            withIntermediateDirectories: true
        )
        return directory.appendingPathComponent("mimi-\(UUID().uuidString).wav")
    }

    private static func removeOldRecordings() {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("mimi", isDirectory: true)
        guard let files = try? FileManager.default.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: [.contentModificationDateKey]
        ) else {
            return
        }

        let cutoff = Date().addingTimeInterval(-24 * 60 * 60)
        for file in files where file.pathExtension.lowercased() == "wav" {
            let values = try? file.resourceValues(forKeys: [.contentModificationDateKey])
            if let modified = values?.contentModificationDate, modified < cutoff {
                try? FileManager.default.removeItem(at: file)
            }
        }
    }

    private static func waitForEnter(timeout: TimeInterval) throws -> StopReason {
        let deadline = Date().addingTimeInterval(timeout)
        var descriptor = pollfd(fd: STDIN_FILENO, events: Int16(POLLIN), revents: 0)

        while Date() < deadline {
            if receivedSignal != 0 {
                throw MimiError.interrupted
            }

            descriptor.revents = 0
            let result = poll(&descriptor, 1, 250)

            if result < 0 {
                if errno == EINTR {
                    continue
                }
                throw MimiError.inputFailed(String(cString: strerror(errno)))
            }

            if result == 0 {
                continue
            }

            if descriptor.revents & Int16(POLLIN) != 0 {
                var byte: UInt8 = 0
                let count = Darwin.read(STDIN_FILENO, &byte, 1)
                if count == 0 {
                    throw MimiError.inputClosed
                }
                if count < 0 {
                    if errno == EINTR {
                        continue
                    }
                    throw MimiError.inputFailed(String(cString: strerror(errno)))
                }
                if byte == 10 || byte == 13 {
                    return .enter
                }
            }

            if descriptor.revents & Int16(POLLERR | POLLHUP | POLLNVAL) != 0 {
                throw MimiError.inputClosed
            }
        }

        return .timeout
    }

    private static func transcribe(
        recordingURL: URL,
        apiKey: String,
        model: String,
        prompt: String
    ) async throws -> String {
        let audioData: Data
        do {
            audioData = try Data(contentsOf: recordingURL)
        } catch {
            throw MimiError.fileReadFailed(error.localizedDescription)
        }

        let boundary = "mimi-\(UUID().uuidString)"
        var body = Data()
        body.appendFormField(name: "model", value: model, boundary: boundary)
        body.appendFormField(name: "language", value: "ja", boundary: boundary)
        body.appendFormField(name: "prompt", value: prompt, boundary: boundary)
        body.appendFormField(name: "response_format", value: "json", boundary: boundary)
        body.appendFile(
            name: "file",
            filename: "mimi.wav",
            mimeType: "audio/wav",
            contents: audioData,
            boundary: boundary
        )
        body.appendUTF8("--\(boundary)--\r\n")

        guard let endpoint = URL(string: "https://api.openai.com/v1/audio/transcriptions") else {
            throw MimiError.invalidResponse
        }

        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.httpBody = body
        request.timeoutInterval = 90
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        request.setValue("mimi/\(version) macOS", forHTTPHeaderField: "User-Agent")

        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 90
        configuration.timeoutIntervalForResource = 90
        let session = URLSession(configuration: configuration)
        defer {
            session.invalidateAndCancel()
        }

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw MimiError.networkError(error.localizedDescription)
        }

        guard let httpResponse = response as? HTTPURLResponse else {
            throw MimiError.invalidResponse
        }

        guard (200..<300).contains(httpResponse.statusCode) else {
            let envelope = try? JSONDecoder().decode(APIErrorEnvelope.self, from: data)
            throw MimiError.apiError(
                httpResponse.statusCode,
                envelope?.error?.message ?? ""
            )
        }

        guard let decoded = try? JSONDecoder().decode(TranscriptionResponse.self, from: data) else {
            throw MimiError.invalidResponse
        }
        return decoded.text
    }

    private static func copyToClipboard(_ text: String) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        if !pasteboard.setString(text, forType: .string) {
            showStatus("警告: クリップボードへコピーできませんでした。")
        }
    }

    private static func printHelp() {
        print("""
        mimi \(version) — macOS 音声入力CLI

        使い方:
          mimi             録音を開始し、Enterで終了して文字起こし
          mimi --check     APIキーとマイク権限を確認
          mimi --version   バージョンを表示
          mimi --help      このヘルプを表示

        成功時は文字起こし本文だけを標準出力へ出し、同時に
        macOSのクリップボードへコピーします。manoでは Ctrl-T を押し、
        mimi と入力するとカーソル位置へ結果を挿入できます。
        """)
    }

    private static func printCheck() {
        let microphone: String

        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            microphone = "許可済み"
        case .notDetermined:
            microphone = "未確認（初回録音時に確認します）"
        case .denied:
            microphone = "拒否されています"
        case .restricted:
            microphone = "制限されています"
        @unknown default:
            microphone = "不明"
        }

        print("mimi \(version) (macOS)")
        do {
            let key = try APIKeyProvider.resolve(environment: ProcessInfo.processInfo.environment)
            print("OPENAI_API_KEY: \(key.value.isEmpty ? "未設定" : "設定済み（\(key.source)）")")
        } catch {
            print("OPENAI_API_KEY: 読み取りエラー")
        }
        print("マイク: \(microphone)")
    }

    private static func showStatus(_ message: String) {
        // mano captures stdout and stderr and inserts them into the document.
        // Stay silent there; the system beep indicates recording state instead.
        guard isatty(STDOUT_FILENO) == 1 else {
            return
        }
        FileHandle.standardError.write(Data((message + "\n").utf8))
    }

    private static func writeTranscript(_ transcript: String) {
        var output = transcript
        if isatty(STDOUT_FILENO) == 1 {
            output += "\n"
        }
        FileHandle.standardOutput.write(Data(output.utf8))
    }

    private static func writeError(_ message: String) {
        FileHandle.standardError.write(Data(("mimi: " + message + "\n").utf8))
    }

    private static func beep() {
        NSSound.beep()
    }

    private static func nonempty(_ value: String?) -> String? {
        guard let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines),
              !trimmed.isEmpty else {
            return nil
        }
        return trimmed
    }
}

private struct ResolvedAPIKey {
    let value: String
    let source: String
}

private enum APIKeyProvider {
    static func resolve(environment: [String: String]) throws -> ResolvedAPIKey {
        if let value = nonempty(environment["OPENAI_API_KEY"]) {
            return ResolvedAPIKey(value: value, source: "環境変数")
        }

        let envURL = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".env")
        guard FileManager.default.fileExists(atPath: envURL.path) else {
            return ResolvedAPIKey(value: "", source: "未設定")
        }

        let contents: String
        do {
            contents = try String(contentsOf: envURL, encoding: .utf8)
        } catch {
            throw MimiError.apiKeyReadFailed(error.localizedDescription)
        }

        for rawLine in contents.split(whereSeparator: \.isNewline) {
            var line = String(rawLine).trimmingCharacters(in: .whitespaces)
            line = line.trimmingCharacters(in: CharacterSet(charactersIn: "\u{FEFF}"))

            if line.isEmpty || line.hasPrefix("#") {
                continue
            }
            if line.hasPrefix("export ") {
                line = String(line.dropFirst(7)).trimmingCharacters(in: .whitespaces)
            }

            guard let equals = line.firstIndex(of: "=") else {
                continue
            }
            let name = line[..<equals].trimmingCharacters(in: .whitespaces)
            guard name == "OPENAI_API_KEY" else {
                continue
            }

            let rawValue = String(line[line.index(after: equals)...])
                .trimmingCharacters(in: .whitespaces)
            return ResolvedAPIKey(value: unquote(rawValue), source: "~/.env")
        }

        return ResolvedAPIKey(value: "", source: "未設定")
    }

    private static func unquote(_ value: String) -> String {
        if value.count >= 2,
           let first = value.first,
           let last = value.last,
           (first == "\"" && last == "\"") || (first == "'" && last == "'") {
            return String(value.dropFirst().dropLast())
        }

        if let comment = value.range(of: " #") {
            return String(value[..<comment.lowerBound]).trimmingCharacters(in: .whitespaces)
        }
        return value
    }

    private static func nonempty(_ value: String?) -> String? {
        guard let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines),
              !trimmed.isEmpty else {
            return nil
        }
        return trimmed
    }
}

private final class StderrSilencer {
    private var savedDescriptor: Int32 = -1

    init(enabled: Bool) {
        guard enabled else {
            return
        }

        let nullDescriptor = Darwin.open("/dev/null", O_WRONLY)
        guard nullDescriptor >= 0 else {
            return
        }

        savedDescriptor = Darwin.dup(STDERR_FILENO)
        if savedDescriptor >= 0 {
            _ = Darwin.dup2(nullDescriptor, STDERR_FILENO)
        }
        Darwin.close(nullDescriptor)
    }

    func restore() {
        guard savedDescriptor >= 0 else {
            return
        }
        _ = Darwin.dup2(savedDescriptor, STDERR_FILENO)
        Darwin.close(savedDescriptor)
        savedDescriptor = -1
    }

    deinit {
        if savedDescriptor >= 0 {
            Darwin.close(savedDescriptor)
        }
    }
}

private extension Data {
    mutating func appendUTF8(_ string: String) {
        append(Data(string.utf8))
    }

    mutating func appendFormField(name: String, value: String, boundary: String) {
        appendUTF8("--\(boundary)\r\n")
        appendUTF8("Content-Disposition: form-data; name=\"\(name)\"\r\n\r\n")
        appendUTF8(value)
        appendUTF8("\r\n")
    }

    mutating func appendFile(
        name: String,
        filename: String,
        mimeType: String,
        contents: Data,
        boundary: String
    ) {
        appendUTF8("--\(boundary)\r\n")
        appendUTF8("Content-Disposition: form-data; name=\"\(name)\"; filename=\"\(filename)\"\r\n")
        appendUTF8("Content-Type: \(mimeType)\r\n\r\n")
        append(contents)
        appendUTF8("\r\n")
    }
}
