using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AiracUpdater.Core;

namespace AiracUpdater
{
    /// <summary>
    /// Command line, mainly for tests and scripts:
    ///   AIRAC-Updater.exe --list
    ///   AIRAC-Updater.exe --scan    &lt;zip-or-folder&gt;
    ///   AIRAC-Updater.exe --install &lt;zip-or-folder&gt; [--no-backup] [--all]
    /// Every command also takes --report &lt;file&gt; to write its output to a file.
    /// </summary>
    internal static class Cli
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        public static int Run(string[] args)
        {
            var output = new StringBuilder();
            int exitCode;
            string report = Option(args, "--report");
            try
            {
                exitCode = Execute(args, line => output.AppendLine(line));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                output.AppendLine("FEHLER: " + e.Message);
                exitCode = 1;
            }

            if (report != null)
            {
                File.WriteAllText(report, output.ToString(), new UTF8Encoding(false));
            }

            try
            {
                // A WinExe has no console of its own; write to the one it was started from, if any.
                AttachConsole(AttachParentProcess);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }

            Console.Out.Write(output.ToString());
            Console.Out.Flush();
            return exitCode;
        }

        private static int Execute(string[] args, Action<string> print)
        {
            string command = args[0];
            SystemFolders folders = SystemFolders.FromEnvironment();
            var log = new RunLog(folders.ToolData);
            using (var session = new Session(folders, Catalog.Profiles, Catalog.Formats))
            {
                session.Refresh();
                foreach (SimInstallation sim in session.Context.Sims)
                {
                    print("Simulator: " + sim.Name + ", Pakete: " + sim.PackagesPath);
                }

                if (command == "--list")
                {
                    PrintItems(session, print);
                    return 0;
                }

                if (command != "--scan" && command != "--install")
                {
                    print("Unbekannter Befehl " + command + ". Möglich: --list, --scan <zip>, --install <zip> [--no-backup] [--all] [--report <datei>]");
                    return 2;
                }

                string input = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
                if (input == null)
                {
                    print(command + " braucht eine ZIP-Datei oder einen Ordner.");
                    return 2;
                }

                log.Write("Kommandozeile " + command + " " + input);
                session.LoadInput(input, null);
                foreach (NavDataSet data in session.DataSets)
                {
                    print("Gefunden: " + data.Format.Name + " " + data.CycleText + " in \"" + data.DisplayPath + "\"");
                }

                foreach (string note in session.Notes)
                {
                    print("Hinweis: " + note);
                }

                if (Flag(args, "--all"))
                {
                    // Also re-install the same cycle (e.g. to repair damaged data).
                    foreach (PlanItem item in session.Items.Where(i => i.CanInstall))
                    {
                        item.Selected = true;
                    }
                }

                PrintItems(session, print);
                if (command == "--scan")
                {
                    return 0;
                }

                List<string> running = Blockers.Running();
                if (running.Count > 0)
                {
                    print("Abbruch: bitte zuerst beenden: " + string.Join(", ", running));
                    return 1;
                }

                List<InstallResult> results = session.Install(!Flag(args, "--no-backup"), line => log.Write(line), null);
                foreach (InstallResult result in results)
                {
                    print((result.Success ? "OK     " : "FEHLER ") + result.Item.Target.Name + ": " + result.Message);
                }

                print(results.Count(r => r.Success) + " von " + results.Count + " aktualisiert.");
                return results.All(r => r.Success) ? 0 : 1;
            }
        }

        private static void PrintItems(Session session, Action<string> print)
        {
            foreach (PlanItem item in session.Items)
            {
                string mark = item.Selected && item.CanInstall ? "[x]" : "[ ]";
                print(mark + " " + item.Target.Name
                    + " | " + (item.Target.Sim?.Name ?? "-")
                    + " | installiert " + item.Target.InstalledCycleText
                    + " | ZIP " + (item.Data?.CycleText ?? "-")
                    + " | " + item.State + ": " + item.Message
                    + " | " + item.Target.TargetPath);
            }
        }

        private static string Option(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static bool Flag(string[] args, string name) => args.Contains(name);
    }
}
