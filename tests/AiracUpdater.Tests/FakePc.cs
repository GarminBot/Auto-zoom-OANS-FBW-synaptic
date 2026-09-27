using System.IO;
using AiracUpdater.Core;

namespace AiracUpdater.Tests
{
    /// <summary>Builds a pretend Windows PC with simulators and add-ons below a temporary folder.</summary>
    public sealed class FakePc
    {
        public FakePc(TempDir dir)
        {
            Dir = dir;
            Folders = SystemFolders.UnderRoot(dir.Combine("pc"));
        }

        public TempDir Dir { get; }

        public SystemFolders Folders { get; }

        public string Msfs2024StoreBase => Path.Combine(Folders.LocalAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe");

        public string Msfs2024SteamBase => Path.Combine(Folders.RoamingAppData, "Microsoft Flight Simulator 2024");

        public string Msfs2020StoreBase => Path.Combine(Folders.LocalAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe");

        /// <summary>MSFS 2024 from the Microsoft Store with its packages in the given folder.</summary>
        public string AddMsfs2024Store(string packagesPath)
        {
            WriteFile(Path.Combine(Msfs2024StoreBase, "LocalCache", "UserCfg.opt"),
                "{Graphics\n}\nInstalledPackagesPath \"" + packagesPath + "\"\n");
            Directory.CreateDirectory(Path.Combine(packagesPath, "Community"));
            Directory.CreateDirectory(Path.Combine(packagesPath, "Community2024"));
            return Path.Combine(Msfs2024StoreBase, "LocalState");
        }

        public string AddMsfs2024Steam(string packagesPath)
        {
            WriteFile(Path.Combine(Msfs2024SteamBase, "UserCfg.opt"), "InstalledPackagesPath \"" + packagesPath + "\"\n");
            Directory.CreateDirectory(Path.Combine(packagesPath, "Community"));
            return Msfs2024SteamBase;
        }

        public string AddMsfs2020Store(string packagesPath)
        {
            WriteFile(Path.Combine(Msfs2020StoreBase, "LocalCache", "UserCfg.opt"), "InstalledPackagesPath \"" + packagesPath + "\"\n");
            Directory.CreateDirectory(Path.Combine(packagesPath, "Community"));
            return Path.Combine(Msfs2020StoreBase, "LocalState");
        }

        /// <summary>An installed package with a manifest.</summary>
        public string AddPackage(string folder, string name, string title)
        {
            string package = Path.Combine(folder, name);
            WriteFile(Path.Combine(package, "manifest.json"), "{ \"title\": \"" + title + "\", \"content_type\": \"AIRCRAFT\" }");
            return package;
        }

        public static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        /// <summary>Navigraph's two MSFS 2024 packages as Hub extracts them into Community.</summary>
        public static void WriteMsfs2024Navdata(string folder, string cycle, int revision = 1, bool withBase = true)
        {
            if (withBase)
            {
                string basePackage = Path.Combine(folder, "navigraph-nav-base");
                WriteFile(Path.Combine(basePackage, "manifest.json"),
                    "{\"dependencies\":[],\"content_type\":\"SCENERY\",\"title\":\"AIRAC Cycle Base\",\"package_order_hint\":\"CUSTOM_NAVDATA\",\"package_version\":\"0.1.0\"}");
                WriteFile(Path.Combine(basePackage, "layout.json"), "{\"content\":[]}");
                WriteFile(Path.Combine(basePackage, "ContentInfo", "navigraph-navdata", "cycle.json"),
                    "{\"Provider\":\"JEPPESEN\",\"Cycle\":\"" + cycle + "\",\"Revision\":" + revision + "}");
                WriteFile(Path.Combine(basePackage, "scenery", "fs-base-jep", "scenery", "world", "base.bgl"), "BGL base " + cycle);
            }

            string jepp = Path.Combine(folder, "navigraph-nav-jepp");
            WriteFile(Path.Combine(jepp, "manifest.json"),
                "{\"dependencies\":[{\"name\":\"navigraph-nav-base\"}],\"title\":\"AIRAC Cycle " + cycle + " rev." + revision + "\",\"package_order_hint\":\"CUSTOM_NAVDATA_PATCH\",\"package_version\":\"2.25.1\"}");
            WriteFile(Path.Combine(jepp, "layout.json"), "{\"content\":[]}");
            WriteFile(Path.Combine(jepp, "scenery", "fs-base-jep", "scenery", "world", "AIRACCycle.bgl"), "BGL " + cycle);
        }

        /// <summary>Fenix navdata as Navigraph Hub leaves it, plus the files the Fenix app adds.</summary>
        public static void WriteFenixData(string folder, string cycle, bool withImport = false)
        {
            WriteFile(Path.Combine(folder, "nd.db3"), "SQLite format 3\0 Fenix " + cycle);
            WriteFile(Path.Combine(folder, "cycle_info.txt"), "AIRAC cycle    : " + cycle + "\r\nVersion        : 1\r\nValid (from/to): 19/MAR/2026 - 16/APR/2026\r\n");
            WriteFile(Path.Combine(folder, "cycle.json"), "{\"cycle\":\"" + cycle + "\",\"revision\":\"1\",\"name\":\"Fenix A320\"}");
            if (withImport)
            {
                WriteFile(Path.Combine(folder, "imported.db3"), "imported " + cycle);
                WriteFile(Path.Combine(folder, "imported_cycle_hash.bin"), "hash");
            }
        }

        /// <summary>Navigraph Navigation Data Interface data, as iniBuilds aircraft download it.</summary>
        public static void WriteNdiData(string folder, string cycle)
        {
            WriteFile(Path.Combine(folder, "db.s3db"), "SQLite format 3\0 NG_FWDFD " + cycle);
            WriteFile(Path.Combine(folder, "cycle.json"),
                "{\"cycle\":\"" + cycle + "\",\"revision\":\"1\",\"name\":\"Navigraph Avionics\",\"format\":\"dfdv2\",\"validityPeriod\":\"2025-10-02/2025-10-29\"}");
        }

        /// <summary>PMDG navdata as Navigraph Hub leaves it: database, cycle.json and cycle_info.txt.</summary>
        public static void WritePmdgData(string folder, string cycle, string revision = "1")
        {
            WriteFile(Path.Combine(folder, "e_dfd_PMDG.s3db"), "SQLite format 3\0 PMDG " + cycle);
            WriteFile(Path.Combine(folder, "cycle.json"), "{\"cycle\":\"" + cycle + "\",\"revision\":\"" + revision + "\",\"name\":\"PMDG (all compatible products)\"}");
            WriteFile(Path.Combine(folder, "cycle_info.txt"), "AIRAC cycle    : " + cycle + "\r\nVersion        : " + revision + "\r\n");
        }
    }
}
