using System;
using System.IO;

namespace AiracUpdater.Core
{
    /// <summary>
    /// The Windows folders the tool looks in. Tests point them at a temporary directory.
    /// </summary>
    public sealed class SystemFolders
    {
        public SystemFolders(string localAppData, string roamingAppData, string programData, string temp)
        {
            LocalAppData = localAppData;
            RoamingAppData = roamingAppData;
            ProgramData = programData;
            Temp = temp;
        }

        /// <summary>%LOCALAPPDATA%, e.g. C:\Users\Name\AppData\Local.</summary>
        public string LocalAppData { get; }

        /// <summary>%APPDATA%, e.g. C:\Users\Name\AppData\Roaming.</summary>
        public string RoamingAppData { get; }

        /// <summary>%PROGRAMDATA%, e.g. C:\ProgramData.</summary>
        public string ProgramData { get; }

        public string Temp { get; }

        /// <summary>Where the tool keeps backups, settings and its log.</summary>
        public string ToolData => Path.Combine(LocalAppData, "AIRAC-Updater");

        public static SystemFolders FromEnvironment()
        {
            return new SystemFolders(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.GetTempPath());
        }

        /// <summary>A fake Windows profile below one directory, for tests.</summary>
        public static SystemFolders UnderRoot(string root)
        {
            return new SystemFolders(
                Path.Combine(root, "Users", "Pilot", "AppData", "Local"),
                Path.Combine(root, "Users", "Pilot", "AppData", "Roaming"),
                Path.Combine(root, "ProgramData"),
                Path.Combine(root, "Temp"));
        }
    }
}
