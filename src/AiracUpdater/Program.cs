using System;
using System.Windows.Forms;
using AiracUpdater.Gui;

namespace AiracUpdater
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
            {
                return Cli.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ShowCrash(e.ExceptionObject as Exception);

            // A ZIP dropped onto the exe arrives as the first argument.
            string input = args.Length > 0 ? args[0] : null;
            Application.Run(new MainForm(input));
            return 0;
        }

        private static void ShowCrash(Exception e)
        {
            MessageBox.Show(
                "Unerwarteter Fehler:\n\n" + (e?.ToString() ?? "unbekannt"),
                "AIRAC Updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
