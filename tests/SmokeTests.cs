using System;
using System.Drawing;
using System.Reflection;
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
            }

            var clientType = assembly.GetType("Mimi.Services.OpenAiTranscriptionClient", true);
            var parser = clientType.GetMethod("ReadTextProperty", BindingFlags.Static | BindingFlags.NonPublic);
            var parsed = (string)parser.Invoke(null, new object[] { "{\"text\":\"こんにちは\"}" });
            Assert(parsed == "こんにちは", "Transcription JSON parser returned the wrong text.");

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
