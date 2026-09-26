using System.Collections.Generic;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>All supported data formats and add-ons.</summary>
    public static class Catalog
    {
        /// <summary>
        /// Navigraph navdata for MSFS 2024 itself: navigraph-nav-base ("AIRAC Cycle Base") and
        /// navigraph-nav-jepp ("AIRAC Cycle 2609 rev.1"), installed by Navigraph Hub into Community.
        /// </summary>
        public static readonly NavigraphPackagesFormat Msfs2024 = new NavigraphPackagesFormat(
            "msfs2024", "MSFS-2024-Navdaten", SimVersion.Msfs2024, "navigraph-nav-base", "navigraph-nav-jepp");

        /// <summary>The same for MSFS 2020: navigraph-navdata-base and navigraph-navdata.</summary>
        public static readonly NavigraphPackagesFormat Msfs2020 = new NavigraphPackagesFormat(
            "msfs2020", "MSFS-2020-Navdaten", SimVersion.Msfs2020, "navigraph-navdata-base", "navigraph-navdata");

        /// <summary>Fenix A319/A320/A321: nd.db3 with cycle_info.txt and cycle.json.</summary>
        public static readonly FileSignatureFormat Fenix = new FileSignatureFormat(
            "fenix",
            "Fenix-Navdaten",
            new[] { "nd.db3" },
            null,
            new[] { "cycle.json", "cycle_info.txt" });

        /// <summary>
        /// PMDG 737 and 777 (MSFS 2020 and 2024): Navigraph's DFD database for "all compatible PMDG
        /// products". Navigraph Hub replaces work\NavigationData with exactly these files.
        /// </summary>
        public static readonly FileSignatureFormat Pmdg = new FileSignatureFormat(
            "pmdg",
            "PMDG-Navdaten",
            new[] { "e_dfd_PMDG.s3db" },
            null,
            new[] { "cycle.json", "cycle_info.txt" });

        public static IReadOnlyList<NavDataFormat> Formats { get; } = new List<NavDataFormat>
        {
            Msfs2024,
            Msfs2020,
            Fenix,
            Pmdg,
        };

        public static IReadOnlyList<AddonProfile> Profiles { get; } = BuildProfiles();

        private static List<AddonProfile> BuildProfiles()
        {
            var profiles = new List<AddonProfile>
            {
                // The 2025 beta installed the packages with sort prefixes; Navigraph asks to delete them.
                new SimNavdataProfile("msfs2024-navdata", "Simulator-Navdaten (Standardflugzeuge)", Msfs2024,
                    new[] { "!!!navigraph-nav-base", "}}}navigraph-nav-jepp" }),
                new SimNavdataProfile("msfs2020-navdata", "Simulator-Navdaten (Standardflugzeuge)", Msfs2020, null),
                new FenixProfile(Fenix),
            };

            // One package per model, the variants (BBJ, BCF, BDSF, ER ...) live inside it.
            (string Package, string Name, string[] Keywords)[] pmdg =
            {
                ("pmdg-aircraft-736", "PMDG 737-600", new[] { "737-600", "736", "737" }),
                ("pmdg-aircraft-737", "PMDG 737-700", new[] { "737-700", "pmdg-aircraft-737", "737" }),
                ("pmdg-aircraft-738", "PMDG 737-800", new[] { "737-800", "738", "737" }),
                ("pmdg-aircraft-739", "PMDG 737-900", new[] { "737-900", "739", "737" }),
                ("pmdg-aircraft-77er", "PMDG 777-200ER", new[] { "777-200er", "77er", "777" }),
                ("pmdg-aircraft-77l", "PMDG 777-200LR", new[] { "777-200lr", "77l", "777" }),
                ("pmdg-aircraft-77w", "PMDG 777-300ER", new[] { "777-300er", "77w", "777" }),
                ("pmdg-aircraft-77f", "PMDG 777F", new[] { "777f", "777-f", "77f", "777" }),
            };
            profiles.AddRange(pmdg.Select(p => new WorkFolderProfile(p.Package, p.Name, Pmdg, p.Package, "NavigationData", p.Keywords)));

            return profiles;
        }
    }
}
