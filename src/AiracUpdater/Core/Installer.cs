using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>Installs the selected plan items one after the other; one failure does not stop the rest.</summary>
    public static class Installer
    {
        public static List<InstallResult> Run(IEnumerable<PlanItem> items, InstallOptions options, Action<PlanItem, int, int> progress = null)
        {
            List<PlanItem> selected = items.Where(i => i.Selected && i.CanInstall).ToList();
            var results = new List<InstallResult>();
            for (int index = 0; index < selected.Count; index++)
            {
                PlanItem item = selected[index];
                progress?.Invoke(item, index, selected.Count);
                options.Log(item.Target.Name + ": " + item.Data.Format.Name + " " + item.Data.CycleText + " aus \"" + item.Data.DisplayPath + "\"");
                options.Log("  Ziel: " + item.Target.TargetPath);
                try
                {
                    item.Target.Profile.Install(item.Data, item.Target, options);
                    string message = Verify(item);
                    options.Log("  " + message);
                    results.Add(new InstallResult(item, true, message));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is ArgumentException || e is NotSupportedException)
                {
                    bool denied = IsAccessDenied(e);
                    string message = denied
                        ? "Keine Schreibrechte (" + e.Message + "). Programm als Administrator starten."
                        : e.Message;
                    options.Log("  FEHLER: " + message);
                    results.Add(new InstallResult(item, false, message, denied));
                }
            }

            return results;
        }

        public static bool IsAccessDenied(Exception e)
        {
            for (Exception current = e; current != null; current = current.InnerException)
            {
                // 0x80070005 = E_ACCESSDENIED
                if (current is UnauthorizedAccessException || current.HResult == unchecked((int)0x80070005))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Verify(PlanItem item)
        {
            // Read the cycle back from where the add-on will read it.
            AiracCycle? now = item.Target.Profile.ReadInstalledCycle(item.Target, out int revision);
            if (!now.HasValue)
            {
                return "installiert";
            }

            string text = now.Value + (revision > 0 ? " rev. " + revision : string.Empty);
            if (item.Data.Cycle.HasValue && now.Value != item.Data.Cycle.Value)
            {
                throw new IOException("Nach dem Kopieren meldet das Ziel Zyklus " + text + " statt " + item.Data.CycleText + ".");
            }

            return "installiert, Zyklus jetzt " + text;
        }
    }
}
