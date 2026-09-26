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

        /// <summary>PMDG navdata as Navigraph Hub leaves it: database, cycle.json and cycle_info.txt.</summary>
        public static void WritePmdgData(string folder, string cycle, string revision = "1")
        {
            WriteFile(Path.Combine(folder, "e_dfd_PMDG.s3db"), "SQLite format 3\0 PMDG " + cycle);
            WriteFile(Path.Combine(folder, "cycle.json"), "{\"cycle\":\"" + cycle + "\",\"revision\":\"" + revision + "\",\"name\":\"PMDG (all compatible products)\"}");
            WriteFile(Path.Combine(folder, "cycle_info.txt"), "AIRAC cycle    : " + cycle + "\r\nVersion        : " + revision + "\r\n");
        }
    }
}
