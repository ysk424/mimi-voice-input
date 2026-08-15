using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mimi.Services;

namespace Mimi
{
    internal sealed class MainForm : Form
    {
        public const string WindowTitle = "mimi — GPT/Codex 音声入力メモ";
        private const int MaximumCharacters = 400;

        private readonly RichTextBox _textBox;
        private readonly Label _countLabel;
        private readonly Label _statusLabel;
        private readonly Button _clearButton;
        private readonly Button _pttButton;
        private readonly Button _copyButton;
        private readonly MciAudioRecorder _recorder = new MciAudioRecorder();
        private readonly OpenAiTranscriptionClient _transcriptionClient = new OpenAiTranscriptionClient();
        private readonly Stopwatch _recordingTimer = new Stopwatch();

        private bool _allowExit;
        private bool _isRecording;
        private bool _isTranscribing;
        private int _insertionStart;
        private int _insertionLength;

        public MainForm(Icon appIcon)
        {
            Text = WindowTitle;
            Icon = (Icon)appIcon.Clone();
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 468);
            MinimumSize = new Size(636, 507);
            MaximumSize = new Size(636, 507);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(255, 248, 244);
            Font = new Font("Yu Gothic UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = true;
            TopMost = true;

            var iconBox = new PictureBox
            {
                Location = new Point(20, 14),
                Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = appIcon.ToBitmap(),
                TabStop = false
            };

            var titleLabel = new Label
            {
                AutoSize = true,
                Location = new Point(70, 12),
                Text = "mimi",
                ForeColor = Color.FromArgb(87, 55, 47),
                Font = new Font("Yu Gothic UI", 17F, FontStyle.Bold, GraphicsUnit.Point)
            };

            var helpLabel = new Label
            {
                AutoSize = true,
                Location = new Point(72, 42),
                Text = "PTTを押している間に話すと、カーソル位置へ日本語を入力します",
                ForeColor = Color.FromArgb(126, 94, 84),
                Font = new Font("Yu Gothic UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
            };

            _textBox = new RichTextBox
            {
                Location = new Point(20, 70),
                Size = new Size(580, 268),
                MaxLength = MaximumCharacters,
                AcceptsTab = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(55, 47, 44),
                Font = new Font("Yu Gothic UI", 12F, FontStyle.Regular, GraphicsUnit.Point),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            _textBox.TextChanged += delegate { UpdateCharacterCount(); };

            _countLabel = new Label
            {
                AutoSize = false,
                Location = new Point(500, 343),
                Size = new Size(100, 22),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(146, 111, 100)
            };

            _statusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(20, 343),
                Size = new Size(470, 22),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(126, 94, 84)
            };

            _clearButton = CreateButton("消去", new Point(20, 382), new Size(116, 56));
            _clearButton.BackColor = Color.FromArgb(244, 232, 226);
            _clearButton.ForeColor = Color.FromArgb(100, 75, 67);
            _clearButton.Click += OnClearClicked;

            _pttButton = CreateButton("🎙  押して話す", new Point(153, 374), new Size(294, 68));
            _pttButton.BackColor = Color.FromArgb(255, 164, 150);
            _pttButton.ForeColor = Color.FromArgb(83, 45, 40);
            _pttButton.Font = new Font("Yu Gothic UI Emoji", 12F, FontStyle.Bold, GraphicsUnit.Point);
            _pttButton.MouseDown += OnPttMouseDown;
            _pttButton.MouseUp += OnPttMouseUp;
            _pttButton.KeyDown += OnPttKeyDown;
            _pttButton.KeyUp += OnPttKeyUp;

            _copyButton = CreateButton("コピーして閉じる", new Point(464, 382), new Size(136, 56));
            _copyButton.BackColor = Color.FromArgb(111, 193, 177);
            _copyButton.ForeColor = Color.White;
            _copyButton.Click += OnCopyClicked;

            Controls.Add(iconBox);
            Controls.Add(titleLabel);
            Controls.Add(helpLabel);
            Controls.Add(_textBox);
            Controls.Add(_countLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_clearButton);
            Controls.Add(_pttButton);
            Controls.Add(_copyButton);

            FormClosing += OnFormClosing;
            Deactivate += OnFormDeactivated;
            UpdateCharacterCount();
            UpdateReadyStatus();
        }

        public void ShowFromTray()
        {
            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            BringToFront();
            Activate();
            _textBox.Focus();

            if (!_isRecording && !_isTranscribing)
            {
                UpdateReadyStatus();
            }
        }

        public void AllowExit()
        {
            _allowExit = true;
            CancelRecording();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _recorder.Dispose();
                if (Icon != null)
                {
                    Icon.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private static Button CreateButton(string text, Point location, Size size)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Font = new Font("Yu Gothic UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                TabStop = true
            };
        }

        private void OnPttMouseDown(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                StartRecording();
            }
        }

        private async void OnPttMouseUp(object sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                await StopAndTranscribeAsync();
            }
        }

        private void OnPttKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode == Keys.Space && !_isRecording)
            {
                eventArgs.SuppressKeyPress = true;
                StartRecording();
            }
        }

        private async void OnPttKeyUp(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode == Keys.Space)
            {
                eventArgs.SuppressKeyPress = true;
                await StopAndTranscribeAsync();
            }
        }

        private void StartRecording()
        {
            if (_isRecording || _isTranscribing)
            {
                return;
            }

            if (!OpenAiTranscriptionClient.HasApiKey())
            {
                _statusLabel.Text = "OPENAI_API_KEY が未設定です";
                MessageBox.Show(
                    this,
                    "環境変数 OPENAI_API_KEY にAPIキーを設定してから、もう一度お試しください。\n\n設定方法は README.md にあります。",
                    "mimi - APIキーが必要です",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _insertionStart = _textBox.SelectionStart;
            _insertionLength = _textBox.SelectionLength;

            try
            {
                _recorder.Start();
                _recordingTimer.Restart();
                _isRecording = true;
                _pttButton.Text = "●  録音中… 離すと送信";
                _pttButton.BackColor = Color.FromArgb(239, 91, 91);
                _pttButton.ForeColor = Color.White;
                _statusLabel.Text = "話してください（PTTを離すと文字起こしします）";
            }
            catch (Exception exception)
            {
                CancelRecording();
                _statusLabel.Text = "マイクを開始できませんでした";
                MessageBox.Show(
                    this,
                    exception.Message + "\n\nWindowsの「マイクのプライバシー設定」も確認してください。",
                    "mimi - 録音エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async Task StopAndTranscribeAsync()
        {
            if (!_isRecording || _isTranscribing)
            {
                return;
            }

            _isRecording = false;
            _recordingTimer.Stop();
            ResetPttAppearance();

            string wavePath = null;
            try
            {
                wavePath = _recorder.StopAndSave();
                if (_recordingTimer.ElapsedMilliseconds < 180)
                {
                    _statusLabel.Text = "短すぎたため送信しませんでした";
                    return;
                }

                SetTranscribing(true);
                _statusLabel.Text = "日本語に文字起こし中…";

                var transcript = await _transcriptionClient.TranscribeJapaneseAsync(wavePath);
                InsertTranscript(transcript);
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "文字起こしに失敗しました";
                MessageBox.Show(
                    this,
                    exception.Message,
                    "mimi - 文字起こしエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                SetTranscribing(false);
                if (!string.IsNullOrEmpty(wavePath))
                {
                    TryDelete(wavePath);
                }
            }
        }

        private void InsertTranscript(string transcript)
        {
            var normalized = (transcript ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                _statusLabel.Text = "音声を認識できませんでした";
                return;
            }

            var start = Math.Max(0, Math.Min(_insertionStart, _textBox.TextLength));
            var selectionLength = Math.Max(0, Math.Min(_insertionLength, _textBox.TextLength - start));
            var available = MaximumCharacters - (_textBox.TextLength - selectionLength);

            if (available <= 0)
            {
                _statusLabel.Text = "400文字に達しているため挿入できませんでした";
                return;
            }

            var wasTruncated = normalized.Length > available;
            var insertedText = wasTruncated ? normalized.Substring(0, available) : normalized;

            _textBox.Select(start, selectionLength);
            _textBox.SelectedText = insertedText;
            _textBox.Select(start + insertedText.Length, 0);
            _textBox.Focus();
            _statusLabel.Text = wasTruncated
                ? "400文字に収まるところまで挿入しました"
                : "カーソル位置へ挿入しました";
        }

        private void OnClearClicked(object sender, EventArgs eventArgs)
        {
            if (_isRecording || _isTranscribing)
            {
                return;
            }

            _textBox.Clear();
            _textBox.Focus();
            _statusLabel.Text = "消去しました";
        }

        private void OnCopyClicked(object sender, EventArgs eventArgs)
        {
            if (_isRecording || _isTranscribing)
            {
                return;
            }

            try
            {
                if (_textBox.TextLength == 0)
                {
                    Clipboard.Clear();
                }
                else
                {
                    Clipboard.SetText(_textBox.Text);
                }

                Hide();
                _statusLabel.Text = "コピーしました。通知領域の猫アイコンから再度開けます";
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    "クリップボードへコピーできませんでした。\n" + exception.Message,
                    "mimi - コピーエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (_allowExit)
            {
                return;
            }

            eventArgs.Cancel = true;
            CancelRecording();
            Hide();
        }

        private async void OnFormDeactivated(object sender, EventArgs eventArgs)
        {
            if (_isRecording)
            {
                await StopAndTranscribeAsync();
            }
        }

        private void CancelRecording()
        {
            if (!_isRecording && !_recorder.IsRecording)
            {
                return;
            }

            _isRecording = false;
            _recordingTimer.Reset();
            _recorder.Cancel();
            ResetPttAppearance();
        }

        private void SetTranscribing(bool value)
        {
            _isTranscribing = value;
            _textBox.Enabled = !value;
            _clearButton.Enabled = !value;
            _pttButton.Enabled = !value;
            _copyButton.Enabled = !value;
        }

        private void ResetPttAppearance()
        {
            _pttButton.Text = "🎙  押して話す";
            _pttButton.BackColor = Color.FromArgb(255, 164, 150);
            _pttButton.ForeColor = Color.FromArgb(83, 45, 40);
        }

        private void UpdateCharacterCount()
        {
            _countLabel.Text = _textBox.TextLength + " / " + MaximumCharacters;
        }

        private void UpdateReadyStatus()
        {
            _statusLabel.Text = OpenAiTranscriptionClient.HasApiKey()
                ? "準備できました"
                : "OPENAI_API_KEY を設定してください";
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // The file lives in the user's temp directory and can be cleaned up by Windows.
            }
        }
    }
}
