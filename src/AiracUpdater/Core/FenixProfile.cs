using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// Fenix A319/A320/A321: one navdata folder for all variants and both simulators,
    /// %ProgramData%\Fenix\Navdata (nd.db3, cycle_info.txt, cycle.json). Navigraph Hub replaces the
    /// whole folder; the Fenix app imports nd.db3 again at its next start.
    /// </summary>
    public sealed class FenixProfile : AddonProfile
    {
        private static readonly string[] Packages = { "fnx-aircraft-320", "fnx-aircraft-319-321" };

        public FenixProfile(NavDataFormat format)
            : base("fenix", "Fenix A319/A320/A321", format, "fenix", "a319", "a320", "a321", "fnx")
        {
        }

        public override IEnumerable<(string Process, string Name)> BlockingProcesses => new[]
        {
            ("Fenix", "Fenix-App"),
            ("FenixBootstrapper", "Fenix-App"),
        };

        public override IEnumerable<AddonTarget> Locate(ToolContext context)
        {
            string fenix = Path.Combine(context.Folders.ProgramData, "Fenix");
            string navdata = Path.Combine(fenix, "Navdata");
            List<SimInstallation> sims = context.Sims.Where(s => Packages.Any(p => s.FindPackage(p) != null)).ToList();
            bool appInstalled = Directory.Exists(Path.Combine(fenix, "App")) || Directory.Exists(Path.Combine(fenix, "FenixSim A320"));
            if (sims.Count == 0 && !appInstalled && !Directory.Exists(navdata))
            {
                yield break;
            }

            AddonTarget target = Target(null, Name, navdata, "gilt für A319, A320 und A321 in MSFS 2020 und 2024");
            target.SimLabel = sims.Count == 0 ? "–" : string.Join(" + ", sims.Select(s => s.ShortName).Distinct());
            yield return target;
        }
    }
}
