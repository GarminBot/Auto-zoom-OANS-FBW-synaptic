using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AiracUpdater.Gui
{
    /// <summary>A few remembered choices in %LOCALAPPDATA%\AIRAC-Updater\settings.txt.</summary>
    internal sealed class Settings
    {
        private readonly string path;

        private Settings(string path)
        {
            this.path = path;
        }

        public string LastFolder { get; set; }

        public bool KeepBackup { get; set; } = true;

        public static Settings Load(string directory)
        {
            var settings = new Settings(Path.Combine(directory, "settings.txt"));
            try
            {
                if (!File.Exists(settings.path))
                {
                    return settings;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(settings.path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                    }
                }

                if (values.TryGetValue("LastFolder", out string folder))
                {
                    settings.LastFolder = folder;
                }

                if (values.TryGetValue("KeepBackup", out string keep))
                {
                    settings.KeepBackup = !string.Equals(keep, "false", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[] { "LastFolder=" + LastFolder, "KeepBackup=" + (KeepBackup ? "true" : "false") }, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
