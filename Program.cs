using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Mimi
{
    internal static class Program
    {
        private const string MutexName = @"Local\MimiPromptTool-9C52BA73-92E3-49A2-83BF-14778895CC69";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateExistingWindow();
                    return;
                }

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var context = new TrayApplicationContext())
                {
                    Application.Run(context);
                }
            }
        }

        private static void ActivateExistingWindow()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var handle = FindWindow(null, MainForm.WindowTitle);
                if (handle != IntPtr.Zero)
                {
                    ShowWindowAsync(handle, 5); // SW_SHOW
                    SetForegroundWindow(handle);
                    return;
                }

                Thread.Sleep(100);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);
    }
}
