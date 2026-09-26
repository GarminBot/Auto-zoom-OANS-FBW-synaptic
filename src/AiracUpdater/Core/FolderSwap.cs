using System;
using System.IO;

namespace AiracUpdater.Core
{
    /// <summary>
    /// Replaces a folder with new content so that a failure never leaves half-written data behind:
    /// copy the new data next to the target, then swap by renaming, then move the old data away.
    /// </summary>
    public static class FolderSwap
    {
        public const string NewSuffix = ".airac-new";
        public const string OldSuffix = ".airac-old";

        /// <param name="source">Folder whose content becomes the target's content.</param>
        /// <param name="target">Folder to replace (created if missing).</param>
        /// <param name="backup">Where the old content goes (replacing an older backup); null to delete it.</param>
        public static void Replace(string source, string target, string backup, Action<string> log)
        {
            target = Path.GetFullPath(target);
            string staging = target + NewSuffix;
            string old = target + OldSuffix;
            Replace(source, target, staging, old, backup, log);
        }

        /// <param name="staging">Temporary folder for the new data, on the same drive as the target.</param>
        /// <param name="old">Temporary folder for the old data, on the same drive as the target.</param>
        public static void Replace(string source, string target, string staging, string old, string backup, Action<string> log)
        {
            log = log ?? (_ => { });
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            Directory.CreateDirectory(Path.GetDirectoryName(staging));
            Directory.CreateDirectory(Path.GetDirectoryName(old));

            // Leftovers of an interrupted earlier run.
            FileTools.DeleteDirectory(staging);
            if (Directory.Exists(old))
            {
                if (!Directory.Exists(target))
                {
                    // The previous run stopped between the two renames: the old data is the real data.
                    Directory.Move(old, target);
                }
                else
                {
                    FileTools.DeleteDirectory(old);
                }
            }

            log("kopiere neue Daten nach " + staging);
            long bytes = FileTools.CopyDirectory(source, staging);
            log("  " + FileTools.FormatBytes(bytes) + " kopiert");

            bool hadOld = Directory.Exists(target);
            if (hadOld)
            {
                try
                {
                    Directory.Move(target, old);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    FileTools.DeleteDirectory(staging);
                    throw new IOException(
                        "Der Ordner " + target + " ist gesperrt (Simulator oder Addon-Programm noch offen?) oder schreibgeschützt: " + e.Message, e);
                }
            }

            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (hadOld && !Directory.Exists(target))
                {
                    Directory.Move(old, target);
                }

                FileTools.DeleteDirectory(staging);
                throw;
            }

            if (!hadOld)
            {
                return;
            }

            if (backup == null)
            {
                FileTools.DeleteDirectory(old);
                return;
            }

            try
            {
                FileTools.DeleteDirectory(backup);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                MoveDirectory(old, backup);
                log("  alte Daten gesichert in " + backup);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The new data is in place; a failed backup must not turn that into an error.
                log("  Sicherung nicht möglich (" + e.Message + "), alte Daten gelöscht");
                TryDelete(old);
            }
        }

        /// <summary>Directory.Move only works on one drive; across drives copy and delete.</summary>
        public static void MoveDirectory(string source, string destination)
        {
            string sourceRoot = Path.GetPathRoot(Path.GetFullPath(source));
            string destinationRoot = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (IOException)
                {
                    // Different volumes mounted below the same root (or similar): fall back to copying.
                }
            }

            FileTools.CopyDirectory(source, destination);
            FileTools.DeleteDirectory(source);
        }

        private static void TryDelete(string directory)
        {
            try
            {
                FileTools.DeleteDirectory(directory);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
