# mimi

GPT や Codex に渡す文章を音声入力するための小さなツールです。

- macOS: Swift製CLI。Enterで録音を終え、文字起こしを標準出力とクリップボードへ送ります
- Windows: PTT付きの編集ウィンドウと通知領域アイコンを備えた音声入力メモです

## macOS版（CLI）

macOS 13以降とXcode Command Line Toolsが必要です。Pythonや常駐プロセスは使用しません。

### インストール

次を実行します。

```sh
./macos/install.sh
mimi --check
```

`~/.local/bin` が `PATH` に含まれていない場合は、シェルの設定へ追加してください。

### 使い方

```sh
mimi
```

起動と同時に録音が始まります。話し終えたら Enter を押してください。文字起こし結果だけを標準出力へ出し、同じ内容をクリップボードにもコピーします。録音は最長60秒、API通信は最長90秒です。

manoからは次のように使えます。

1. `Ctrl-T` を押す
2. `mimi` と入力して Enterを押す
3. 開始音のあとに話す
4. Enterを押して録音を終える
5. 文字起こし結果がカーソル位置へ入る

初回はmacOSからマイク使用の確認が表示されます。「ターミナル」を許可してください。状態は `mimi --check` で確認できます。

APIキーは `jh` と同じものを使います。環境変数 `OPENAI_API_KEY`、なければ `~/.env` の `OPENAI_API_KEY` の順で読みます。

モデルと文字起こし用ヒントは、Windows版と同様に `MIMI_TRANSCRIBE_MODEL` と `MIMI_TRANSCRIBE_PROMPT` で変更できます。

## Windows版

GPT や Codex に渡す文章を、送信前に落ち着いて整えるための小さなWindows音声入力メモです。

- 400文字まで直接編集
- PTT（押している間だけ録音）
- OpenAI の文字起こし API で日本語化し、元のカーソル位置へ挿入
- 消去ボタン
- 「コピーして閉じる」でクリップボードへコピーし、ウィンドウを隠す
- 通知領域の猫アイコンを左クリックすると再表示
- APIキーをファイルへ保存しない

## 動作環境

- Windows 10 / 11
- .NET Framework 4.x（Windows 10 / 11 に標準搭載）
- Visual Studio 2022 Build Tools（ソースからビルドする場合）
- マイクとインターネット接続
- OpenAI API の利用可能なアカウント（API利用料は別途発生します）

## 1. APIキーを環境変数へ設定

PowerShell を開き、`YOUR_API_KEY` を自分のキーに置き換えて実行します。

```powershell
[Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "YOUR_API_KEY", "User")
```

mimi はプロセス環境変数に加え、Windows のユーザー環境変数も直接読むため、設定後に mimi を再起動すれば利用できます。APIキーはソースや設定ファイルへ書き込まれません。

既定の文字起こしモデルは `gpt-4o-mini-transcribe` です。Whisper を使いたい場合は次のように変更できます。

```powershell
[Environment]::SetEnvironmentVariable("MIMI_TRANSCRIBE_MODEL", "whisper-1", "User")
```

文字起こし用のヒントを変えたい場合は `MIMI_TRANSCRIBE_PROMPT` も設定できます。

## 2. ビルド

このディレクトリで PowerShell を開き、次を実行します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

成果物は `bin\Release\Mimi.exe` です。ビルドスクリプトは Windows にある .NET Framework 4.x のランタイム参照を使うため、Developer Pack や NuGet パッケージの追加は不要です。ビルド末尾では、400文字制限・カーソル挿入・消去・API応答解析のスモークテストも自動実行します。

## 3. 起動と使い方

`run.bat` をダブルクリックするか、`bin\Release\Mimi.exe` を起動します。

1. テキスト欄の挿入したい位置へカーソルを置きます。
2. 「押して話す」をマウスで押したまま話します。
3. ボタンを離すと録音を終了し、文字起こし結果をカーソル位置へ入れます。
4. 必要なら文章を直し、「コピーして閉じる」を押します。
5. GPT / Codex の入力欄へ貼り付けます。

ウィンドウを閉じてもアプリは通知領域に残ります。完全に終了するには、通知領域の猫アイコンを右クリックして「終了」を選びます。`Mimi.exe` をタスクバーへピン留めしても使えます。

## マイクが使えない場合

Windows の「設定 → プライバシーとセキュリティ → マイク」で、「デスクトップ アプリにマイクへのアクセスを許可する」をオンにしてください。録音には Windows の既定の入力デバイスを使います。

## データの扱い

PTT を離した時点で録音 WAV を OpenAI の `/v1/audio/transcriptions` へ送信します。送信後、ローカルの一時 WAV は削除します。入力欄に手で書いた既存テキストは送信しません。
