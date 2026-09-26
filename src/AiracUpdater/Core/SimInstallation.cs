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
                foreach (string official in new[] { "Official2024", "Official" })
                {
                    string root = Path.Combine(PackagesPath, official);
                    foreach (string sub in new[] { "OneStore", "Steam" })
                    {
                        yield return Path.Combine(root, sub);
                    }
                }
            }
        }

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

        /// <summary>Finds an installed package folder by name in Community or Official folders.</summary>
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

        /// <summary>The work folder of a package, e.g. ...\WASM\MSFS2024\pmdg-aircraft-738\work (may not exist yet).</summary>
        public IEnumerable<string> WorkFolderCandidates(string packageName) =>
            WorkRoots.Select(root => Path.Combine(root, packageName, "work"));

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
