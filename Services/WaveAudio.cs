using System;
using System.IO;
using System.Text;

namespace Mimi.Services
{
    internal static class WaveAudio
    {
        public static byte[] ReadForTranscription(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Validate(bytes);
            return bytes;
        }

        // Measure recorded samples, not how long the button was held: opening
        // the input device can consume most of a short press.
        public static void Validate(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12 ||
                Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE" ||
                BitConverter.ToUInt32(bytes, 4) != bytes.Length - 8)
            {
                throw InvalidWave();
            }

            bool hasFormat = false;
            bool hasData = false;
            uint dataLength = 0;
            long offset = 12;
            while (offset < bytes.Length)
            {
                if (offset + 8 > bytes.Length) throw InvalidWave();
                var tag = Tag(bytes, (int)offset);
                var length = BitConverter.ToUInt32(bytes, (int)offset + 4);
                var start = offset + 8;
                var end = start + length;
                var next = end + (length & 1);
                if (next > bytes.Length) throw InvalidWave();

                if (tag == "fmt ")
                {
                    if (hasFormat || length < 16) throw InvalidWave();
                    var at = (int)start;
                    // The recorder explicitly requests PCM, mono, 16 kHz, 16 bit.
                    if (BitConverter.ToUInt16(bytes, at) != 1 ||
                        BitConverter.ToUInt16(bytes, at + 2) != 1 ||
                        BitConverter.ToUInt32(bytes, at + 4) != 16000 ||
                        BitConverter.ToUInt32(bytes, at + 8) != 32000 ||
                        BitConverter.ToUInt16(bytes, at + 12) != 2 ||
                        BitConverter.ToUInt16(bytes, at + 14) != 16)
                    {
                        throw InvalidWave();
                    }
                    hasFormat = true;
                }
                else if (tag == "data")
                {
                    if (hasData || length % 2 != 0) throw InvalidWave();
                    hasData = true;
                    dataLength = length;
                }
                offset = next;
            }

            if (!hasFormat || !hasData) throw InvalidWave();
            if (dataLength < 6400) // 0.2 seconds of actual PCM samples.
            {
                throw new InvalidDataException("録音が短すぎます。少し長く押して再録音してください。");
            }
        }

        private static string Tag(byte[] bytes, int offset)
        {
            return Encoding.ASCII.GetString(bytes, offset, 4);
        }

        private static InvalidDataException InvalidWave()
        {
            return new InvalidDataException("録音ファイルを正常に保存できませんでした。もう一度録音してください。");
        }
    }
}
