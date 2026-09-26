using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// One run of the tool: find the simulators and add-ons, read the user's ZIP, plan and install.
    /// Used by the window and by the command line.
    /// </summary>
    public sealed class Session : IDisposable
    {
        private readonly IReadOnlyList<AddonProfile> profiles;
        private readonly IReadOnlyList<NavDataFormat> formats;
        private InputFolder input;

        public Session(SystemFolders folders, IReadOnlyList<AddonProfile> profiles, IReadOnlyList<NavDataFormat> formats)
        {
            Folders = folders;
            this.profiles = profiles;
            this.formats = formats;
            Targets = new List<AddonTarget>();
            DataSets = new List<NavDataSet>();
            Items = new List<PlanItem>();
            Notes = new List<string>();
        }

        public SystemFolders Folders { get; }

        public ToolContext Context { get; private set; }

        public List<AddonTarget> Targets { get; private set; }

        public List<NavDataSet> DataSets { get; private set; }

        public List<PlanItem> Items { get; private set; }

        public List<string> Notes { get; private set; }

        public string InputPath => input?.SourcePath;

        /// <summary>Finds simulators and installed add-ons; keeps an already loaded ZIP.</summary>
        public void Refresh()
        {
            Context = ToolContext.Discover(Folders);
            var targets = new List<AddonTarget>();
            foreach (AddonProfile profile in profiles)
            {
                targets.AddRange(profile.Locate(Context));
            }

            Targets = targets;
            Plan();
        }

        /// <summary>Unpacks and reads a ZIP (or folder) with navdata.</summary>
        public void LoadInput(string path, Action<string> status)
        {
            InputFolder opened = InputFolder.Open(path, Folders.Temp, status);
            input?.Dispose();
            input = opened;
            status?.Invoke("Suche Navdaten in " + Path.GetFileName(path));
            DataSets = DataSetScanner.Scan(input.Root, formats);
            if (Context == null)
            {
                Refresh();
            }
            else
            {
                Plan();
            }
        }

        private void Plan()
        {
            Items = Planner.Plan(Targets, DataSets, profiles);
            Notes = BuildNotes();
        }

        public List<InstallResult> Install(bool keepBackup, Action<string> log, Action<PlanItem, int, int> progress)
        {
            var options = new InstallOptions(Context, keepBackup, log);
            List<InstallResult> results = Installer.Run(Items, options, progress);
            return results;
        }

        private List<string> BuildNotes()
        {
            var notes = new List<string>();
            if (input == null)
            {
                return notes;
            }

            if (DataSets.Count == 0)
            {
                notes.Add("In der ZIP wurden keine bekannten Navdaten gefunden.");
            }

            foreach (NavDataSet data in DataSets)
            {
                bool used = Items.Any(i => i.Data == data);
                if (!used)
                {
                    bool formatUsed = Targets.Any(t => t.Profile.Format != null && t.Profile.Format.Id == data.Format.Id);
                    notes.Add(formatUsed
                        ? data.Format.Name + " " + data.CycleText + " in \"" + data.DisplayPath + "\" wird nicht benutzt (ein anderer Ordner mit denselben Daten passt besser)."
                        : data.Format.Name + " " + data.CycleText + " in \"" + data.DisplayPath + "\": dafür ist kein Addon installiert.");
                }
            }

            // Top-level folders without anything the tool understands.
            foreach (string folder in FileTools.SafeDirectories(input.Root))
            {
                if (!DataSets.Any(d => FileTools.IsInside(folder, d.ContentPath) || FileTools.IsInside(d.ContentPath, folder)))
                {
                    notes.Add("Ordner \"" + Path.GetFileName(folder) + "\": keine bekannten Navdaten erkannt.");
                }
            }

            return notes;
        }

        public void Dispose()
        {
            input?.Dispose();
            input = null;
        }
    }
}
