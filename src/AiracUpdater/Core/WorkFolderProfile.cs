using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// An aircraft package that reads its navdata from its MSFS "work" folder, e.g. PMDG:
    /// ...\LocalState\WASM\MSFS2024\pmdg-aircraft-738\work\NavigationData. One target per simulator
    /// in which the package is installed.
    /// </summary>
    public sealed class WorkFolderProfile : AddonProfile
    {
        public WorkFolderProfile(string id, string name, NavDataFormat format, string packageName, string dataFolder, params string[] keywords)
            : base(id, name, format, keywords)
        {
            PackageName = packageName;
            DataFolder = dataFolder;
        }

        public string PackageName { get; }

        /// <summary>Sub-folder of "work" that holds the data, e.g. "NavigationData".</summary>
        public string DataFolder { get; }

        public override IEnumerable<AddonTarget> Locate(ToolContext context)
        {
            foreach (SimInstallation sim in context.Sims)
            {
                string package = sim.FindPackage(PackageName);
                if (package == null)
                {
                    continue;
                }

                string details = "Paket: " + package;
                string work = sim.FindWorkFolder(PackageName);
                if (work == null)
                {
                    string expected = Path.Combine(sim.WorkRoots.First(), PackageName, "work", DataFolder);
                    yield return new AddonTarget(this, sim, Name, expected, null, 0, details,
                        "erst einmal im Simulator laden, dann „Neu prüfen“");
                    continue;
                }

                yield return Target(sim, Name, Path.Combine(work, DataFolder), details);
            }
        }
    }
}
