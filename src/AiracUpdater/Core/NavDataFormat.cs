using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// A navdata file format, recognised by its content. One format can serve several add-ons
    /// (e.g. one PMDG data set for every PMDG aircraft).
    /// </summary>
    public abstract class NavDataFormat
    {
        public abstract string Id { get; }

        public abstract string Name { get; }

        /// <summary>True if <paramref name="directory"/> is the top folder of a data set in this format.</summary>
        public abstract bool IsDataRoot(string directory);

        /// <summary>Reads the cycle of a data set folder or of an installed target folder.</summary>
        public abstract bool TryReadCycle(string directory, out AiracCycle cycle, out int revision);

        public override string ToString() => Name;
    }

    /// <summary>
    /// A format recognised by a set of files that must all be present in one folder, with the
    /// cycle in a small text file (usually Navigraph's cycle_info.txt).
    /// </summary>
    public sealed class FileSignatureFormat : NavDataFormat
    {
        private readonly string id;
        private readonly string name;
        private readonly string[] requiredFiles;
        private readonly string[] requiredDirectories;
        private readonly string[] cycleFiles;

        public FileSignatureFormat(string id, string name, string[] requiredFiles, string[] requiredDirectories, string[] cycleFiles)
        {
            this.id = id;
            this.name = name;
            this.requiredFiles = requiredFiles ?? Array.Empty<string>();
            this.requiredDirectories = requiredDirectories ?? Array.Empty<string>();
            this.cycleFiles = cycleFiles ?? Array.Empty<string>();
        }

        public override string Id => id;

        public override string Name => name;

        public IReadOnlyList<string> RequiredFiles => requiredFiles;

        public override bool IsDataRoot(string directory)
        {
            if (requiredFiles.Length == 0 && requiredDirectories.Length == 0)
            {
                return false;
            }

            var files = new HashSet<string>(FileTools.SafeFiles(directory).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            if (!requiredFiles.All(files.Contains))
            {
                return false;
            }

            var directories = new HashSet<string>(FileTools.SafeDirectories(directory).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            return requiredDirectories.All(directories.Contains);
        }

        public override bool TryReadCycle(string directory, out AiracCycle cycle, out int revision)
        {
            foreach (string cycleFile in cycleFiles)
            {
                string path = FileTools.FindFile(directory, cycleFile);
                if (path != null && CycleText.TryParseFile(path, out cycle, out revision))
                {
                    return true;
                }
            }

            cycle = default;
            revision = 0;
            return false;
        }
    }
}
