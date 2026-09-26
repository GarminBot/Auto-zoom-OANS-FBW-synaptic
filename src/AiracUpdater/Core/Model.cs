using System.Collections.Generic;

namespace AiracUpdater.Core
{
    /// <summary>One navdata set found in the user's ZIP, e.g. the PMDG data in folder "PMDG".</summary>
    public sealed class NavDataSet
    {
        public NavDataSet(NavDataFormat format, string contentPath, string displayPath, AiracCycle? cycle, int revision)
        {
            Format = format;
            ContentPath = contentPath;
            DisplayPath = displayPath;
            Cycle = cycle;
            Revision = revision;
        }

        public NavDataFormat Format { get; }

        /// <summary>The folder whose content gets installed (in the temporary copy of the input).</summary>
        public string ContentPath { get; }

        /// <summary>Where the data sits inside the user's ZIP, for display and for matching folder names.</summary>
        public string DisplayPath { get; }

        public AiracCycle? Cycle { get; }

        public int Revision { get; }

        public string CycleText => Cycle.HasValue ? Cycle.Value + (Revision > 0 ? " rev. " + Revision : string.Empty) : "?";

        public override string ToString() => Format.Name + " " + CycleText + " (" + DisplayPath + ")";
    }

    /// <summary>One place on this PC where an add-on (or the simulator) expects its navdata.</summary>
    public sealed class AddonTarget
    {
        public AddonTarget(AddonProfile profile, SimInstallation sim, string name, string targetPath, AiracCycle? installedCycle, int installedRevision, string details, string problem = null, bool hasData = true)
        {
            HasData = hasData;
            Profile = profile;
            Sim = sim;
            Name = name;
            TargetPath = targetPath;
            InstalledCycle = installedCycle;
            InstalledRevision = installedRevision;
            Details = details;
            Problem = problem;
        }

        public AddonProfile Profile { get; }

        /// <summary>The simulator this target belongs to (null for sim-independent data such as Fenix).</summary>
        public SimInstallation Sim { get; }

        /// <summary>Display name, e.g. "PMDG 737-800 (MSFS 2024)".</summary>
        public string Name { get; }

        /// <summary>Folder that receives the data.</summary>
        public string TargetPath { get; }

        public AiracCycle? InstalledCycle { get; }

        public int InstalledRevision { get; }

        /// <summary>Extra information for the details view (package folder etc.).</summary>
        public string Details { get; }

        /// <summary>Simulator column text; defaults to the simulator's name.</summary>
        public string SimLabel
        {
            get => simLabel ?? Sim?.Name ?? "–";
            set => simLabel = value;
        }

        private string simLabel;

        /// <summary>False if no navdata is installed there yet.</summary>
        public bool HasData { get; }

        /// <summary>Why the target cannot receive data right now (null if it can).</summary>
        public string Problem { get; }

        /// <summary>Stable key for backups and settings.</summary>
        public string Key => Profile.Id + "|" + TargetPath;

        public string InstalledCycleText =>
            InstalledCycle.HasValue
                ? InstalledCycle.Value + (InstalledRevision > 0 ? " rev. " + InstalledRevision : string.Empty)
                : "–";

        public override string ToString() => Name + " -> " + TargetPath;
    }

    public enum PlanState
    {
        /// <summary>Newer data in the ZIP: will be installed.</summary>
        Update,

        /// <summary>Same cycle already installed.</summary>
        UpToDate,

        /// <summary>The ZIP holds an older cycle than the installed one.</summary>
        Older,

        /// <summary>Installed cycle unknown: installing is allowed.</summary>
        Unknown,

        /// <summary>The ZIP holds no data for this add-on.</summary>
        NoData,

        /// <summary>Add-on without own navdata; it uses another target (e.g. the simulator's data).</summary>
        Covered,

        /// <summary>Installed, but the target folder is not usable yet (see AddonTarget.Problem).</summary>
        NotReady,
    }

    public sealed class PlanItem
    {
        public PlanItem(AddonTarget target, NavDataSet data, PlanState state, string message)
        {
            Target = target;
            Data = data;
            State = state;
            Message = message;
            Selected = state == PlanState.Update || state == PlanState.Unknown;
        }

        public AddonTarget Target { get; }

        public NavDataSet Data { get; }

        public PlanState State { get; }

        public string Message { get; }

        /// <summary>Whether "Alle aktualisieren" installs this item.</summary>
        public bool Selected { get; set; }

        public bool CanInstall => Data != null && State != PlanState.NoData && State != PlanState.Covered && State != PlanState.NotReady;
    }

    public sealed class InstallResult
    {
        public InstallResult(PlanItem item, bool success, string message, bool accessDenied = false)
        {
            Item = item;
            Success = success;
            Message = message;
            AccessDenied = accessDenied;
        }

        public PlanItem Item { get; }

        public bool Success { get; }

        public string Message { get; }

        /// <summary>Failed for lack of write permission: running as administrator may help.</summary>
        public bool AccessDenied { get; }
    }

    /// <summary>Everything the tool knows after scanning the ZIP and the PC.</summary>
    public sealed class ScanResult
    {
        public ScanResult(IReadOnlyList<NavDataSet> dataSets, IReadOnlyList<PlanItem> items, IReadOnlyList<SimInstallation> sims, IReadOnlyList<string> notes)
        {
            DataSets = dataSets;
            Items = items;
            Sims = sims;
            Notes = notes;
        }

        public IReadOnlyList<NavDataSet> DataSets { get; }

        public IReadOnlyList<PlanItem> Items { get; }

        public IReadOnlyList<SimInstallation> Sims { get; }

        /// <summary>Hints for the user, e.g. data sets in the ZIP that no installed add-on uses.</summary>
        public IReadOnlyList<string> Notes { get; }
    }
}
