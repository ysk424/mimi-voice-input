using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Mimi.Services
{
    internal sealed class OpenAiTranscriptionClient
    {
        private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
        private const string DefaultModel = "gpt-4o-mini-transcribe";
        private const string DefaultPrompt = "自然な日本語の文章です。句読点を適切に使います。GPT、Codex、OpenAI、API、C#、C++、Windows。";

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        public static bool HasApiKey()
        {
            return !string.IsNullOrWhiteSpace(GetEnvironmentValue("OPENAI_API_KEY"));
        }

        public async Task<string> TranscribeJapaneseAsync(string wavePath)
        {
            var apiKey = GetEnvironmentValue("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("環境変数 OPENAI_API_KEY が設定されていません。");
            }

            var model = GetEnvironmentValue("MIMI_TRANSCRIBE_MODEL");
            if (string.IsNullOrWhiteSpace(model))
            {
                model = DefaultModel;
            }

            var prompt = GetEnvironmentValue("MIMI_TRANSCRIBE_PROMPT");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = DefaultPrompt;
            }

            byte[] audioBytes;
            try
            {
                audioBytes = File.ReadAllBytes(wavePath);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("録音データを読み込めませんでした。", exception);
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            using (var form = new MultipartFormDataContent())
            using (var audio = new ByteArrayContent(audioBytes))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                request.Headers.UserAgent.ParseAdd("mimi/1.0");

                audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(audio, "file", Path.GetFileName(wavePath));
                form.Add(new StringContent(model.Trim(), Encoding.UTF8), "model");
                form.Add(new StringContent("ja", Encoding.UTF8), "language");
                form.Add(new StringContent(prompt.Trim(), Encoding.UTF8), "prompt");
                form.Add(new StringContent("json", Encoding.UTF8), "response_format");
                request.Content = form;

                HttpResponseMessage response;
                try
                {
                    response = await HttpClient.SendAsync(request).ConfigureAwait(true);
                }
                catch (TaskCanceledException exception)
                {
                    throw new TimeoutException("OpenAI APIから90秒以内に応答がありませんでした。", exception);
                }
                catch (HttpRequestException exception)
                {
                    throw new InvalidOperationException("OpenAI APIへ接続できませんでした。インターネット接続を確認してください。", exception);
                }

                using (response)
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(BuildApiError(response, body));
                    }

                    var text = ReadTextProperty(body);
                    if (text == null)
                    {
                        throw new InvalidOperationException("OpenAI APIの応答に文字起こし結果がありませんでした。");
                    }

                    return text;
                }
            }
        }

        private static string ReadTextProperty(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                object text;
                if (root != null && root.TryGetValue("text", out text))
                {
                    return text as string;
                }
            }
            catch (ArgumentException)
            {
                // A clear application-level error is returned below.
            }

            return null;
        }

        private static string BuildApiError(HttpResponseMessage response, string json)
        {
            var detail = string.Empty;
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                object errorValue;
                if (root != null && root.TryGetValue("error", out errorValue))
                {
                    var error = errorValue as Dictionary<string, object>;
                    object message;
                    if (error != null && error.TryGetValue("message", out message))
                    {
                        detail = message as string;
                    }
                }
            }
            catch (ArgumentException)
            {
                // Keep the status-only fallback.
            }

            var status = (int)response.StatusCode + " " + response.ReasonPhrase;
            return string.IsNullOrWhiteSpace(detail)
                ? "OpenAI APIがエラーを返しました（" + status + "）。"
                : "OpenAI APIエラー（" + status + "）: " + detail;
        }

        private static string GetEnvironmentValue(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            try
            {
                value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }

                return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            }
            catch
            {
                return null;
            }
        }
    }
}
