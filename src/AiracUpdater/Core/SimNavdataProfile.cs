using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// Navigraph's navdata for the simulator itself: a base package plus a cycle package in the
    /// Community folder, e.g. navigraph-nav-base and navigraph-nav-jepp in MSFS 2024.
    /// </summary>
    public sealed class NavigraphPackagesFormat : NavDataFormat
    {
        private readonly string id;
        private readonly string name;

        public NavigraphPackagesFormat(string id, string name, SimVersion sim, string basePackage, string cyclePackage)
        {
            this.id = id;
            this.name = name;
            Sim = sim;
            BasePackage = basePackage;
            CyclePackage = cyclePackage;
        }

        public override string Id => id;

        public override string Name => name;

        public SimVersion Sim { get; }

        public string BasePackage { get; }

        /// <summary>The package whose manifest title carries the cycle ("AIRAC Cycle 2609 rev.1").</summary>
        public string CyclePackage { get; }

        public IEnumerable<string> Packages => new[] { BasePackage, CyclePackage };

        /// <summary>A folder that holds the cycle package (and normally the base package next to it).</summary>
        public override bool IsDataRoot(string directory)
        {
            string package = FileTools.FindDirectory(directory, CyclePackage);
            return package != null && FileTools.FindFile(package, "manifest.json") != null;
        }

        public override bool TryReadCycle(string directory, out AiracCycle cycle, out int revision)
        {
            string package = FileTools.FindDirectory(directory, CyclePackage);
            string manifest = package == null ? null : FileTools.FindFile(package, "manifest.json");
            if (manifest != null && CycleText.TryParseFile(manifest, out cycle, out revision))
            {
                return true;
            }

            // Fallback: the base package's ContentInfo\...\cycle.json ({"Cycle":"2501","Revision":1,...}).
            string basePackage = FileTools.FindDirectory(directory, BasePackage);
            string contentInfo = basePackage == null ? null : FileTools.FindDirectory(basePackage, "ContentInfo");
            if (contentInfo != null)
            {
                foreach (string file in Directory.GetFiles(contentInfo, "*.json", SearchOption.AllDirectories))
                {
                    if (FileTools.NameIs(file, "cycle.json") && CycleText.TryParseFile(file, out cycle, out revision))
                    {
                        return true;
                    }
                }
            }

            cycle = default;
            revision = 0;
            return false;
        }
    }

    /// <summary>
    /// The simulator's own navdata (used by the default aircraft and by add-ons without their own
    /// database). Installs the packages like Navigraph Hub: old packages out, new ones into Community.
    /// </summary>
    public sealed class SimNavdataProfile : AddonProfile
    {
        private readonly NavigraphPackagesFormat packages;
        private readonly string[] obsoletePackages;

        public SimNavdataProfile(string id, string name, NavigraphPackagesFormat format, string[] obsoletePackages, params string[] keywords)
            : base(id, name, format, keywords)
        {
            packages = format;
            this.obsoletePackages = obsoletePackages ?? Array.Empty<string>();
        }

        public override IEnumerable<AddonTarget> Locate(ToolContext context)
        {
            foreach (SimInstallation sim in context.Sims.Where(s => s.Version == packages.Sim))
            {
                string community = InstalledIn(sim) ?? Path.Combine(sim.PackagesPath, "Community");
                AiracCycle? cycle = packages.TryReadCycle(community, out AiracCycle found, out int revision) ? found : (AiracCycle?)null;
                bool hasData = FileTools.FindDirectory(community, packages.CyclePackage) != null;
                yield return new AddonTarget(this, sim, Name, community, cycle, revision,
                    "Pakete " + string.Join(" + ", packages.Packages) + " in " + community, null, hasData);
            }
        }

        public override AiracCycle? ReadInstalledCycle(AddonTarget target, out int revision)
        {
            if (packages.TryReadCycle(target.TargetPath, out AiracCycle cycle, out revision))
            {
                return cycle;
            }

            return null;
        }

        /// <summary>The Community folder that holds the cycle package now (Community2024 wins, like in the sim).</summary>
        private string InstalledIn(SimInstallation sim) =>
            sim.CommunityFolders.FirstOrDefault(folder => FileTools.FindDirectory(folder, packages.CyclePackage) != null);

        /// <summary>Staging, swap and backup folders next to Community, where the sim does not look.</summary>
        private static string WorkArea(AddonTarget target) => Path.Combine(target.Sim.PackagesPath, "AIRAC-Updater");

        public override void Install(NavDataSet data, AddonTarget target, InstallOptions options)
        {
            string work = WorkArea(target);
            string backupRoot = Path.Combine(work, "backup", Id);
            bool replacedBackup = false;
            foreach (string package in packages.Packages)
            {
                string source = FileTools.FindDirectory(data.ContentPath, package);
                if (source == null)
                {
                    continue;
                }

                if (options.KeepBackup && !replacedBackup)
                {
                    // One backup per target: the previous one goes when a new update starts.
                    FileTools.DeleteDirectory(backupRoot);
                    replacedBackup = true;
                }

                string existing = FileTools.FindDirectory(target.TargetPath, package) ?? Path.Combine(target.TargetPath, package);
                FolderSwap.Replace(
                    source,
                    existing,
                    Path.Combine(work, "new", package),
                    Path.Combine(work, "old", package),
                    options.KeepBackup ? Path.Combine(backupRoot, package) : null,
                    options.Log);
            }

            // Hub removes before it installs: no second copy of the same data may stay behind,
            // neither in the other Community folder nor under the names of the 2025 beta packages.
            foreach (string folder in target.Sim.CommunityFolders)
            {
                IEnumerable<string> names = obsoletePackages.Concat(
                    FileTools.IsInside(folder, target.TargetPath) ? Enumerable.Empty<string>() : packages.Packages);
                foreach (string name in names)
                {
                    string stale = FileTools.FindDirectory(folder, name);
                    if (stale == null)
                    {
                        continue;
                    }

                    options.Log("  entferne doppeltes Paket " + stale);
                    if (options.KeepBackup)
                    {
                        string destination = Path.Combine(backupRoot, "entfernt", Path.GetFileName(folder), Path.GetFileName(stale));
                        FileTools.DeleteDirectory(destination);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        FolderSwap.MoveDirectory(stale, destination);
                    }
                    else
                    {
                        FileTools.DeleteDirectory(stale);
                    }
                }
            }

            if (FileTools.FindDirectory(target.TargetPath, packages.BasePackage) == null)
            {
                throw new IOException(packages.BasePackage + " fehlt: bitte das Basis-Paket mit in die ZIP legen (neben " + packages.CyclePackage + ").");
            }
        }

        public override bool HasBackup(AddonTarget target, ToolContext context) =>
            packages.Packages.Any(p => FileTools.HasAnyEntry(Path.Combine(WorkArea(target), "backup", Id, p)));

        public override void RestoreBackup(AddonTarget target, InstallOptions options)
        {
            string work = WorkArea(target);
            foreach (string package in packages.Packages)
            {
                string backup = Path.Combine(work, "backup", Id, package);
                if (!FileTools.HasAnyEntry(backup))
                {
                    continue;
                }

                string existing = FileTools.FindDirectory(target.TargetPath, package) ?? Path.Combine(target.TargetPath, package);
                FolderSwap.Replace(backup, existing, Path.Combine(work, "new", package), Path.Combine(work, "old", package), null, options.Log);
            }
        }
    }
}
