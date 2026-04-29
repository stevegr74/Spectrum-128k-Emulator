using System;
using System.IO;
using System.Text;

namespace Spectrum128kEmulator
{
    internal static class Program
    {
        private static MainForm? currentForm;

        [STAThread]
        static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) =>
                WriteUnhandledCrashReport(args.Exception, "Application.ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                WriteUnhandledCrashReport(args.ExceptionObject as Exception, "AppDomain.UnhandledException");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            currentForm = new MainForm();
            Application.Run(currentForm);
        }

        private static void WriteUnhandledCrashReport(Exception? exception, string context)
        {
            try
            {
                string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
                Directory.CreateDirectory(debugFolder);
                string fileName = $"fatal-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
                string path = Path.Combine(debugFolder, fileName);

                var sb = new StringBuilder();
                sb.AppendLine($"Context: {context}");
                sb.AppendLine();
                sb.AppendLine(exception?.ToString() ?? "No exception object was provided.");
                sb.AppendLine();

                if (currentForm != null)
                {
                    sb.AppendLine(currentForm.BuildCrashDiagnosticDump(context));
                }

                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
            }
        }
    }
}
