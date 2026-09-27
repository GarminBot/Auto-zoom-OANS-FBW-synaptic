using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// Navigraph's Navigation Data Interface (NDI), which iniBuilds aircraft use in their "NAVIGRAPH"
    /// mode: work\NavigationData with cycle.json ("format": "dfdv2", "name": "Navigraph Avionics")
    /// and one SQLite database (*.s3db, normally db.s3db).
    /// </summary>
    public sealed class NdiFormat : NavDataFormat
    {
        public override string Id => "ndi";

        public override string Name => "Navigraph-NDI-Navdaten";

        public override bool IsDataRoot(string directory)
        {
            string cycleJson = FileTools.FindFile(directory, "cycle.json");
            if (cycleJson == null || new FileInfo(cycleJson).Length > 64 * 1024)
            {
                return false;
            }

            string[] files = FileTools.SafeFiles(directory).Select(Path.GetFileName).ToArray();
            if (!files.Any(f => f.EndsWith(".s3db", StringComparison.OrdinalIgnoreCase))
                || files.Any(f => string.Equals(f, "e_dfd_PMDG.s3db", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string text = File.ReadAllText(cycleJson);
            return text.IndexOf("dfdv2", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Navigraph Avionics", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public override bool TryReadCycle(string directory, out AiracCycle cycle, out int revision)
        {
            string cycleJson = FileTools.FindFile(directory, "cycle.json");
            if (cycleJson != null && CycleText.TryParseFile(cycleJson, out cycle, out revision))
            {
                return true;
            }

            cycle = default;
            revision = 0;
            return false;
        }
    }

    /// <summary>
    /// An aircraft without navdata files of its own: it reads the simulator's navdata (FBW A380X,
    /// or iniBuilds/Synaptic in their "sim"/"native" mode). Listed so the user sees it is covered.
    /// </summary>
    public sealed class CoveredAircraftProfile : AddonProfile
    {
        private readonly string[] packageNames;
        private readonly string[] simObjectFolders;
        private readonly string coveredBy;

        /// <param name="packageNames">Known package folder names.</param>
        /// <param name="simObjectFolders">Folders below SimObjects\Airplanes that identify the aircraft in any package.</param>
        public CoveredAircraftProfile(string id, string name, string coveredBy, string[] packageNames, string[] simObjectFolders)
            : base(id, name, null)
        {
            this.coveredBy = coveredBy;
            this.packageNames = packageNames ?? Array.Empty<string>();
            this.simObjectFolders = simObjectFolders ?? Array.Empty<string>();
        }

        public override string CoveredBy => coveredBy;

        public override IEnumerable<AddonTarget> Locate(ToolContext context)
        {
            foreach (SimInstallation sim in context.Sims)
            {
                string package = FindAircraftPackage(sim, packageNames, simObjectFolders);
                if (package != null)
                {
                    yield return new AddonTarget(this, sim, Name, package, null, 0, "Paket: " + package, null, false);
                }
            }
        }

        /// <summary>By package name first, else by a SimObjects\Airplanes folder in any installed package.</summary>
        public static string FindAircraftPackage(SimInstallation sim, IEnumerable<string> packageNames, IReadOnlyCollection<string> simObjectFolders)
        {
            foreach (string name in packageNames)
            {
                string found = sim.FindPackage(name);
                if (found != null)
                {
                    return found;
                }
            }

            if (simObjectFolders.Count == 0)
            {
                return null;
            }

            return sim.FindPackages(_ => true).FirstOrDefault(package =>
            {
                string airplanes = FileTools.FindDirectory(FileTools.FindDirectory(package, "SimObjects") ?? package, "Airplanes");
                return airplanes != null && simObjectFolders.Any(folder => FileTools.FindDirectory(airplanes, folder) != null);
            });
        }
    }
}
