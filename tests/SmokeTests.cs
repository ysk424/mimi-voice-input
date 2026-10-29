using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class SmokeTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: SmokeTests <Mimi.exe> <mimi.ico>");
            return 2;
        }

        try
        {
            Application.EnableVisualStyles();
            var assembly = Assembly.LoadFrom(args[0]);
            var formType = assembly.GetType("Mimi.MainForm", true);

            using (var icon = new Icon(args[1]))
            using (var form = (Form)Activator.CreateInstance(
                formType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { icon },
                null))
            {
                var textBox = (RichTextBox)GetField(formType, form, "_textBox");
                var pttButton = (Button)GetField(formType, form, "_pttButton");
                var copyButton = (Button)GetField(formType, form, "_copyButton");

                Assert(textBox.MaxLength == 400, "Text limit must be 400 characters.");
                Assert(pttButton.Text.Contains("話す"), "PTT button was not initialized.");
                Assert(copyButton.Text.Contains("コピー"), "Copy button was not initialized.");

                textBox.Text = new string('あ', 390);
                SetField(formType, form, "_insertionStart", 390);
                SetField(formType, form, "_insertionLength", 0);
                Invoke(formType, form, "InsertTranscript", new string('い', 30));
                Assert(textBox.TextLength == 400, "Transcription insertion must stop at 400 characters.");
                Assert(textBox.SelectionStart == 400, "Caret must move after inserted transcription.");

                Invoke(formType, form, "OnClearClicked", null, EventArgs.Empty);
                Assert(textBox.TextLength == 0, "Clear button logic did not clear the editor.");

                // Exercise real visibility changes without taking the user's focus.
                form.TopMost = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                ShowWindow(form.Handle, 4); // SW_SHOWNOACTIVATE; also used for native reactivation.
                textBox.Text = "前回の文章";
                form.Hide();
                ShowWindow(form.Handle, 4);
                Assert(textBox.TextLength == 0, "Native reopening must clear the previous memo.");
                Assert(!textBox.CanUndo, "Reopening must clear edit history.");
                Assert(((Label)GetField(formType, form, "_countLabel")).Text == "0 / 400",
                    "Reopening must reset the character count.");

                // Opening an already visible window from tray must clear the previous memo.
                textBox.Text = "編集中";
                formType.GetMethod("ShowFromTray").Invoke(form, null);
                Assert(textBox.TextLength == 0, "Opening an already visible window from tray must clear the previous memo.");

                // Activating via relaunch message must clear the previous memo even if already visible.
                textBox.Text = "前の文章";
                var wmActivateAndReset = (int)formType.GetField("WmActivateAndReset", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
                var message = Message.Create(form.Handle, wmActivateAndReset, IntPtr.Zero, IntPtr.Zero);
                formType.GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { message });
                Assert(textBox.TextLength == 0, "Activating via relaunch message must clear the previous memo.");

                form.Hide();
                form.Show(); // Same managed visibility path as ShowFromTray.
                Assert(textBox.TextLength == 0, "Tray reopening must clear the previous memo.");

                foreach (var busyFlag in new[] { "_isRecording", "_isTranscribing" })
                {
                    textBox.Text = "処理中の文章";
                    SetField(formType, form, "_insertionStart", 3);
                    SetField(formType, form, "_insertionLength", 2);
                    form.Hide();
                    SetField(formType, form, busyFlag, true);
                    formType.GetMethod("ShowFromTray").Invoke(form, null);
                    Assert(textBox.Text == "処理中の文章", "Reopening while busy must preserve text.");
                    Assert((int)GetField(formType, form, "_insertionStart") == 3,
                        "Reopening while busy must preserve the insertion position.");
                    SetField(formType, form, busyFlag, false);
                }

                form.Hide();
                ShowWindow(form.Handle, 4);
                Assert(textBox.TextLength == 0, "Reopening after processing must clear text.");
                Assert((int)GetField(formType, form, "_insertionStart") == 0 &&
                    (int)GetField(formType, form, "_insertionLength") == 0,
                    "Reopening must reset the insertion range.");
            }

            var clientType = assembly.GetType("Mimi.Services.OpenAiTranscriptionClient", true);
            var parser = clientType.GetMethod("ReadTextProperty", BindingFlags.Static | BindingFlags.NonPublic);
            var parsed = (string)parser.Invoke(null, new object[] { "{\"text\":\"こんにちは\"}" });
            Assert(parsed == "こんにちは", "Transcription JSON parser returned the wrong text.");

            var waveType = assembly.GetType("Mimi.Services.WaveAudio", true);
            var validate = waveType.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static);
            validate.Invoke(null, new object[] { CreateWave(6400, false) });
            validate.Invoke(null, new object[] { CreateWave(32000, true) });
            AssertInvalidWave(validate, CreateWave(0, false));
            AssertInvalidWave(validate, CreateWave(248, false)); // Observed after a 200 ms press.
            AssertInvalidWave(validate, CreateWave(6398, false));
            AssertInvalidWave(validate, new byte[0]);
            var truncated = CreateWave(6400, false);
            Array.Resize(ref truncated, truncated.Length - 2);
            AssertInvalidWave(validate, truncated);
            var invalidChunk = CreateWave(6400, false);
            Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, invalidChunk, 40, 4);
            AssertInvalidWave(validate, invalidChunk);
            var unsupported = CreateWave(6400, false);
            unsupported[20] = 3; // IEEE float instead of PCM.
            AssertInvalidWave(validate, unsupported);
            var invalidRate = CreateWave(6400, false);
            invalidRate[28] = 1;
            AssertInvalidWave(validate, invalidRate);

            Console.WriteLine("Smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static object GetField(Type type, object instance, string name)
    {
        return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    private static byte[] CreateWave(int dataLength, bool withMetadata)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength + (withMetadata ? 10 : 0));
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(16000);
            writer.Write(32000);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            if (withMetadata)
            {
                writer.Write(Encoding.ASCII.GetBytes("JUNK"));
                writer.Write(1);
                writer.Write((byte)42);
                writer.Write((byte)0); // Odd-sized RIFF chunks require padding.
            }
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
            return stream.ToArray();
        }
    }

    private static void AssertInvalidWave(MethodInfo validate, byte[] bytes)
    {
        try
        {
            validate.Invoke(null, new object[] { bytes });
        }
        catch (TargetInvocationException exception)
        {
            if (exception.InnerException is InvalidDataException) return;
            throw;
        }
        throw new InvalidOperationException("Invalid or short WAV must be rejected before upload.");
    }

    private static void SetField(Type type, object instance, string name, object value)
    {
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }

    private static void Invoke(Type type, object instance, string name, params object[] arguments)
    {
        type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
