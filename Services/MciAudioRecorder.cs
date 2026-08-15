using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Mimi.Services
{
    internal sealed class MciAudioRecorder : IDisposable
    {
        private readonly string _alias = "mimi_voice_" + System.Diagnostics.Process.GetCurrentProcess().Id;
        private bool _disposed;

        public bool IsRecording { get; private set; }

        public void Start()
        {
            ThrowIfDisposed();
            if (IsRecording)
            {
                throw new InvalidOperationException("すでに録音中です。");
            }

            CloseWithoutThrowing();
            Send("open new Type waveaudio Alias " + _alias);

            try
            {
                Send("set " + _alias + " time format ms bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2");
                Send("record " + _alias);
                IsRecording = true;
            }
            catch
            {
                CloseWithoutThrowing();
                throw;
            }
        }

        public string StopAndSave()
        {
            ThrowIfDisposed();
            if (!IsRecording)
            {
                throw new InvalidOperationException("録音されていません。");
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "Mimi");
            Directory.CreateDirectory(tempDirectory);
            var outputPath = Path.Combine(tempDirectory, "mimi-" + Guid.NewGuid().ToString("N") + ".wav");

            try
            {
                Send("stop " + _alias);
                Send("save " + _alias + " \"" + outputPath + "\"");
                return outputPath;
            }
            finally
            {
                IsRecording = false;
                CloseWithoutThrowing();
            }
        }

        public void Cancel()
        {
            if (_disposed)
            {
                return;
            }

            IsRecording = false;
            CloseWithoutThrowing();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Cancel();
            _disposed = true;
        }

        private static void Send(string command)
        {
            var errorCode = mciSendString(command, null, 0, IntPtr.Zero);
            if (errorCode == 0)
            {
                return;
            }

            var message = new StringBuilder(256);
            if (!mciGetErrorString(errorCode, message, message.Capacity))
            {
                message.Append("MCIエラー ").Append(errorCode);
            }

            throw new Win32Exception((int)errorCode, "録音デバイスの操作に失敗しました: " + message);
        }

        private void CloseWithoutThrowing()
        {
            mciSendString("close " + _alias, null, 0, IntPtr.Zero);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern uint mciSendString(
            string command,
            StringBuilder returnValue,
            int returnLength,
            IntPtr callbackWindow);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern bool mciGetErrorString(uint errorCode, StringBuilder errorText, int errorTextSize);
    }
}
