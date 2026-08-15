using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Mimi
{
    internal sealed class TrayApplicationContext : ApplicationContext, IDisposable
    {
        private readonly Icon _appIcon;
        private readonly MainForm _mainForm;
        private readonly NotifyIcon _notifyIcon;
        private bool _isExiting;
        private bool _disposed;

        public TrayApplicationContext()
        {
            _appIcon = LoadAppIcon();
            _mainForm = new MainForm(_appIcon);

            var openItem = new ToolStripMenuItem("開く");
            openItem.Click += delegate { ShowMainWindow(); };

            var exitItem = new ToolStripMenuItem("終了");
            exitItem.Click += delegate { ExitApplication(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "mimi - GPT/Codex用 音声入力メモ",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.MouseClick += OnTrayIconMouseClick;
            _notifyIcon.DoubleClick += delegate { ShowMainWindow(); };

            // Hidden forms do not create a native window until Handle is accessed.
            // Creating it now lets a second launch bring this instance to the front.
            var unusedHandle = _mainForm.Handle;

            Application.Idle += ShowOnFirstIdle;
        }

        private static Icon LoadAppIcon()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("Mimi.Assets.mimi.ico"))
            {
                if (stream == null)
                {
                    return (Icon)SystemIcons.Application.Clone();
                }

                using (var icon = new Icon(stream))
                {
                    return (Icon)icon.Clone();
                }
            }
        }

        private void ShowOnFirstIdle(object sender, EventArgs eventArgs)
        {
            Application.Idle -= ShowOnFirstIdle;
            ShowMainWindow();
        }

        private void OnTrayIconMouseClick(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        }

        private void ShowMainWindow()
        {
            if (_isExiting)
            {
                return;
            }

            if (_mainForm.InvokeRequired)
            {
                _mainForm.BeginInvoke(new Action(ShowMainWindow));
                return;
            }

            _mainForm.ShowFromTray();
        }

        private void ExitApplication()
        {
            if (_isExiting)
            {
                return;
            }

            _isExiting = true;
            _notifyIcon.Visible = false;
            _mainForm.AllowExit();
            _mainForm.Close();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            _notifyIcon.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
                Application.Idle -= ShowOnFirstIdle;
                _notifyIcon.Dispose();
                _mainForm.Dispose();
                _appIcon.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
