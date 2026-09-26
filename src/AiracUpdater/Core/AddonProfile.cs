using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>What the profiles need to find add-ons on this PC.</summary>
    public sealed class ToolContext
    {
        public ToolContext(SystemFolders folders, IReadOnlyList<SimInstallation> sims)
        {
            Folders = folders;
            Sims = sims;
        }

        public SystemFolders Folders { get; }

        public IReadOnlyList<SimInstallation> Sims { get; }

        public static ToolContext Discover(SystemFolders folders) =>
            new ToolContext(folders, SimInstallation.Discover(folders));
    }

    public sealed class InstallOptions
    {
        public InstallOptions(ToolContext context, bool keepBackup, Action<string> log)
        {
            Context = context;
            KeepBackup = keepBackup;
            Log = log ?? (_ => { });
        }

        public ToolContext Context { get; }

        /// <summary>Keep the replaced data (one backup per target) so it can be restored.</summary>
        public bool KeepBackup { get; }

        public Action<string> Log { get; }
    }

    /// <summary>
    /// An add-on (or the simulator itself) that has navdata: how to find it on this PC and how to
    /// install a data set into it.
    /// </summary>
    public abstract class AddonProfile
    {
        protected AddonProfile(string id, string name, NavDataFormat format, params string[] keywords)
        {
            Id = id;
            Name = name;
            Format = format;
            Keywords = keywords ?? Array.Empty<string>();
        }

        public string Id { get; }

        public string Name { get; }

        /// <summary>The data format it reads; null if it has no navdata of its own.</summary>
        public NavDataFormat Format { get; }

        /// <summary>Words in a ZIP folder name that point to exactly this add-on (lower case).</summary>
        public IReadOnlyList<string> Keywords { get; }

        /// <summary>For add-ons without own navdata: who provides it, shown to the user.</summary>
        public virtual string CoveredBy => null;

        public abstract IEnumerable<AddonTarget> Locate(ToolContext context);

        /// <summary>Default: the target folder's content is replaced by the data set.</summary>
        public virtual void Install(NavDataSet data, AddonTarget target, InstallOptions options)
        {
            string backup = options.KeepBackup ? BackupFolder(target, options.Context) : null;
            FolderSwap.Replace(data.ContentPath, target.TargetPath, backup, options.Log);
        }

        /// <summary>True if an earlier update kept the data it replaced.</summary>
        public virtual bool HasBackup(AddonTarget target, ToolContext context) =>
            FileTools.HasAnyEntry(BackupFolder(target, context));

        /// <summary>Puts the kept data back (the backup itself stays).</summary>
        public virtual void RestoreBackup(AddonTarget target, InstallOptions options)
        {
            string backup = BackupFolder(target, options.Context);
            if (!FileTools.HasAnyEntry(backup))
            {
                throw new IOException("Keine Sicherung vorhanden für " + target.Name + ".");
            }

            FolderSwap.Replace(backup, target.TargetPath, null, options.Log);
        }

        /// <summary>Reads the cycle that is installed at a target right now.</summary>
        public virtual AiracCycle? ReadInstalledCycle(AddonTarget target, out int revision) =>
            ReadCycle(target.TargetPath, out revision);

        /// <summary>Reads the installed cycle of a target folder.</summary>
        protected AiracCycle? ReadCycle(string folder, out int revision)
        {
            revision = 0;
            if (Format != null && Directory.Exists(folder) && Format.TryReadCycle(folder, out AiracCycle cycle, out revision))
            {
                return cycle;
            }

            return null;
        }

        protected AddonTarget Target(SimInstallation sim, string name, string targetPath, string details)
        {
            AiracCycle? cycle = ReadCycle(targetPath, out int revision);
            return new AddonTarget(this, sim, name, targetPath, cycle, revision, details);
        }

        public static string BackupFolder(AddonTarget target, ToolContext context)
        {
            string key = new string(target.Key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_').ToArray());
            if (key.Length > 80)
            {
                key = key.Substring(0, 50) + "_" + ((uint)StableHash(key)).ToString("x8");
            }

            return Path.Combine(context.Folders.ToolData, "Backups", key);
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text)
                {
                    hash = hash * 31 + c;
                }

                return hash;
            }
        }

        public override string ToString() => Name;
    }
}
