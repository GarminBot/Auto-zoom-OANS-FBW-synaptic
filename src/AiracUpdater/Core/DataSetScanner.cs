using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>Finds every navdata set in the unpacked input.</summary>
    public static class DataSetScanner
    {
        public static List<NavDataSet> Scan(string root, IReadOnlyList<NavDataFormat> formats)
        {
            var result = new List<NavDataSet>();
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string directory = queue.Dequeue();
                NavDataFormat format = formats.FirstOrDefault(f => f.IsDataRoot(directory));
                if (format == null)
                {
                    foreach (string sub in FileTools.SafeDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(sub);
                    }

                    continue;
                }

                string display = FileTools.RelativePath(root, directory);
                if (display.Length == 0)
                {
                    display = "(oberste Ebene)";
                }

                AiracCycle? cycle = null;
                if (format.TryReadCycle(directory, out AiracCycle found, out int revision))
                {
                    cycle = found;
                }
                else if (TryCycleFromPath(display, out AiracCycle fromName))
                {
                    cycle = fromName;
                }

                result.Add(new NavDataSet(format, directory, display.Replace(Path.DirectorySeparatorChar, '/'), cycle, revision));
            }

            return result;
        }

        /// <summary>Uses the deepest folder name that contains a cycle, e.g. "navigraph_pmdg_2510".</summary>
        private static bool TryCycleFromPath(string relative, out AiracCycle cycle)
        {
            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (CycleText.TryParseName(parts[i], out cycle))
                {
                    return true;
                }
            }

            cycle = default;
            return false;
        }
    }
}
