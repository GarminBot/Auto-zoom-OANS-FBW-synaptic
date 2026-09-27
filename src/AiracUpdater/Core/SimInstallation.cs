using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AiracUpdater.Core
{
    public enum SimVersion
    {
        Msfs2020,
        Msfs2024,
    }

    public enum SimStore
    {
        MicrosoftStore,
        Steam,
    }

    /// <summary>
    /// One installed Microsoft Flight Simulator: where its packages are (from UserCfg.opt) and where
    /// its per-package "work" folders are.
    /// </summary>
    public sealed class SimInstallation
    {
        private static readonly Regex PackagesPathLine =
            new Regex(@"^\s*InstalledPackagesPath\s+""([^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public SimInstallation(SimVersion version, SimStore store, string userCfgPath, string packagesPath, string localState)
        {
            Version = version;
            Store = store;
            UserCfgPath = userCfgPath;
            PackagesPath = packagesPath;
            LocalState = localState;
        }

        public SimVersion Version { get; }

        public SimStore Store { get; }

        public string UserCfgPath { get; }

        /// <summary>The folder that holds Community, Official etc. (InstalledPackagesPath).</summary>
        public string PackagesPath { get; }

        /// <summary>
        /// Store: ...\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState. Steam: %APPDATA%\Microsoft Flight Simulator 2024.
        /// </summary>
        public string LocalState { get; }

        public string Name =>
            (Version == SimVersion.Msfs2024 ? "MSFS 2024" : "MSFS 2020")
            + (Store == SimStore.Steam ? " (Steam)" : " (Microsoft Store)");

        public string ShortName => Version == SimVersion.Msfs2024 ? "MSFS 2024" : "MSFS 2020";

        /// <summary>For narrow columns: "MSFS 2024 Store" or "MSFS 2024 Steam".</summary>
        public string CompactName => ShortName + (Store == SimStore.Steam ? " Steam" : " Store");

        /// <summary>Community folders in the order the tool prefers them for new packages.</summary>
        public IEnumerable<string> CommunityFolders
        {
            get
            {
                if (Version == SimVersion.Msfs2024)
                {
                    yield return Path.Combine(PackagesPath, "Community2024");
                }

                yield return Path.Combine(PackagesPath, "Community");
            }
        }

        /// <summary>Folders with marketplace/official packages that can contain add-on aircraft.</summary>
        public IEnumerable<string> OfficialFolders
        {
            get
            {
                // MSFS 2024 keeps its own marketplace content in Official2024 and carried-over 2020
                // content in Official2020; MSFS 2020 uses Official.
                string[] roots = Version == SimVersion.Msfs2024 ? new[] { "Official2024", "Official2020" } : new[] { "Official" };
                foreach (string official in roots)
                {
                    foreach (string sub in new[] { "OneStore", "Steam" })
                    {
                        yield return Path.Combine(PackagesPath, official, sub);
                    }
                }
            }
        }

        /// <summary>MSFS 2024 marketplace packages that are streamed: StreamedPackages\fs24-&lt;package&gt;.</summary>
        public string StreamedPackagesFolder => Version == SimVersion.Msfs2024 ? Path.Combine(PackagesPath, "StreamedPackages") : null;

        /// <summary>
        /// Folders whose sub-folders are the per-package "work" roots: &lt;root&gt;\&lt;package&gt;\work.
        /// MSFS 2024 keeps them by WASM generation, MSFS 2020 in LocalState\packages.
        /// </summary>
        public IEnumerable<string> WorkRoots
        {
            get
            {
                if (Version == SimVersion.Msfs2024)
                {
                    yield return Path.Combine(LocalState, "WASM", "MSFS2024");
                    yield return Path.Combine(LocalState, "WASM", "MSFS2020");
                }

                yield return Path.Combine(LocalState, "packages");
            }
        }

        /// <summary>Finds an installed package folder by name in Community, Official or streamed folders.</summary>
        public string FindPackage(string packageName)
        {
            foreach (string folder in CommunityFolders.Concat(OfficialFolders))
            {
                string found = FileTools.FindDirectory(folder, packageName);
                if (found != null)
                {
                    return found;
                }
            }

            return StreamedPackagesFolder == null ? null : FileTools.FindDirectory(StreamedPackagesFolder, "fs24-" + packageName);
        }

        /// <summary>
        /// The existing work folder of a package. MSFS creates it when the aircraft is loaded for the
        /// first time; like Navigraph Hub, the tool only uses an existing one.
        /// </summary>
        public string FindWorkFolder(string packageName)
        {
            foreach (string root in WorkRoots)
            {
                string package = FileTools.FindDirectory(root, packageName);
                string work = package == null ? null : FileTools.FindDirectory(package, "work");
                if (work != null)
                {
                    return work;
                }
            }

            if (Version == SimVersion.Msfs2024)
            {
                // Any other WASM generation folder (WASM\*\<package>\work).
                foreach (string generation in FileTools.SafeDirectories(Path.Combine(LocalState, "WASM")))
                {
                    string package = FileTools.FindDirectory(generation, packageName);
                    string work = package == null ? null : FileTools.FindDirectory(package, "work");
                    if (work != null)
                    {
                        return work;
                    }
                }
            }

            return null;
        }

        /// <summary>All installed package folders whose name matches the predicate.</summary>
        public IEnumerable<string> FindPackages(Func<string, bool> nameMatches)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in CommunityFolders.Concat(OfficialFolders))
            {
                foreach (string package in FileTools.SafeDirectories(folder))
                {
                    string name = Path.GetFileName(package);
                    if (nameMatches(name) && seen.Add(name))
                    {
                        yield return package;
                    }
                }
            }
        }

        public override string ToString() => Name + ": " + PackagesPath;

        /// <summary>Finds every MSFS 2020/2024 installation (Store and Steam) of the current user.</summary>
        public static List<SimInstallation> Discover(SystemFolders folders)
        {
            var result = new List<SimInstallation>();
            Add(result, SimVersion.Msfs2024, SimStore.MicrosoftStore,
                Path.Combine(folders.LocalAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe"), store: true);
            Add(result, SimVersion.Msfs2024, SimStore.Steam,
                Path.Combine(folders.RoamingAppData, "Microsoft Flight Simulator 2024"), store: false);
            Add(result, SimVersion.Msfs2020, SimStore.MicrosoftStore,
                Path.Combine(folders.LocalAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe"), store: true);
            Add(result, SimVersion.Msfs2020, SimStore.Steam,
                Path.Combine(folders.RoamingAppData, "Microsoft Flight Simulator"), store: false);
            return result;
        }

        private static void Add(List<SimInstallation> result, SimVersion version, SimStore simStore, string baseFolder, bool store)
        {
            // Store: UserCfg.opt in LocalCache, work data in LocalState. Steam: both directly in the base folder.
            string userCfg = store
                ? Path.Combine(baseFolder, "LocalCache", "UserCfg.opt")
                : Path.Combine(baseFolder, "UserCfg.opt");
            if (!File.Exists(userCfg))
            {
                return;
            }

            string localState = store ? Path.Combine(baseFolder, "LocalState") : baseFolder;
            string packages = ReadPackagesPath(userCfg)
                ?? (store ? Path.Combine(baseFolder, "LocalCache", "Packages") : Path.Combine(baseFolder, "Packages"));
            result.Add(new SimInstallation(version, simStore, userCfg, packages, localState));
        }

        public static string ReadPackagesPath(string userCfgPath)
        {
            try
            {
                Match match = PackagesPathLine.Match(File.ReadAllText(userCfgPath));
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
