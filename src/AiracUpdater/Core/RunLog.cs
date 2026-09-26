using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AiracUpdater.Core
{
    /// <summary>Appends what the tool does to %LOCALAPPDATA%\AIRAC-Updater\AIRAC-Updater.log.</summary>
    public sealed class RunLog
    {
        private const long MaxBytes = 2 * 1024 * 1024;
        private readonly object gate = new object();

        public RunLog(string directory)
        {
            FilePath = Path.Combine(directory, "AIRAC-Updater.log");
        }

        public string FilePath { get; }

        public event Action<string> LineWritten;

        public void Write(string line)
        {
            string stamped = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line;
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        File.Copy(FilePath, FilePath + ".old", true);
                        File.Delete(FilePath);
                    }

                    File.AppendAllText(FilePath, stamped + Environment.NewLine, new UTF8Encoding(false));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // Logging must never stop an update.
                }
            }

            LineWritten?.Invoke(line);
        }
    }
}
