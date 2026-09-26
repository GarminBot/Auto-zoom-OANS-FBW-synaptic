using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>
    /// Turns the user's input (a ZIP or a folder) into a plain folder tree in a temporary directory.
    /// ZIP files inside it (for example Navigraph downloads packed into the big ZIP) are unpacked in
    /// place, into a folder named like the ZIP without ".zip".
    /// </summary>
    public sealed class InputFolder : IDisposable
    {
        private const int MaxNestingDepth = 4;

        private InputFolder(string root, string workDirectory, string sourcePath)
        {
            Root = root;
            WorkDirectory = workDirectory;
            SourcePath = sourcePath;
        }

        /// <summary>The folder to scan.</summary>
        public string Root { get; }

        /// <summary>The temporary folder this instance owns (deleted by Dispose).</summary>
        public string WorkDirectory { get; }

        public string SourcePath { get; }

        public static InputFolder Open(string path, string tempRoot, Action<string> status = null)
        {
            string work = Path.Combine(tempRoot, "AIRAC-Updater", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(work);
            try
            {
                string root = Path.Combine(work, "input");
                if (Directory.Exists(path))
                {
                    status?.Invoke("Kopiere Ordner " + path);
                    FileTools.CopyDirectory(path, root);
                }
                else if (File.Exists(path))
                {
                    status?.Invoke("Entpacke " + Path.GetFileName(path));
                    ExtractZip(path, root);
                }
                else
                {
                    throw new FileNotFoundException("Datei oder Ordner nicht gefunden: " + path, path);
                }

                ExtractNestedZips(root, 1, status);
                return new InputFolder(root, work, path);
            }
            catch
            {
                TryDelete(work);
                throw;
            }
        }

        public static void ExtractZip(string zipPath, string destination)
        {
            Directory.CreateDirectory(destination);
            string fullDestination = Path.GetFullPath(destination);
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string relative = entry.FullName.Replace('\\', '/');
                        if (relative.Length == 0 || IsMacMetadata(relative))
                        {
                            continue;
                        }

                        string target = Path.GetFullPath(Path.Combine(fullDestination, relative.Replace('/', Path.DirectorySeparatorChar)));
                        // Refuse entries such as "../../x" that would land outside the destination ("zip slip").
                        if (!FileTools.IsInside(fullDestination, target))
                        {
                            throw new UnsafeZipException("Die ZIP-Datei " + Path.GetFileName(zipPath) + " enthält einen unzulässigen Pfad: " + entry.FullName);
                        }

                        if (relative.EndsWith("/", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                        File.SetAttributes(target, FileAttributes.Normal);
                    }
                }
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException(
                    "Die ZIP-Datei " + Path.GetFileName(zipPath) + " lässt sich nicht lesen: " + e.Message
                    + " Tipp: die ZIP mit dem Windows-Explorer oder 7-Zip (Format ZIP, Methode Deflate) neu packen"
                    + " oder den entpackten Ordner wählen.", e);
            }
        }

        private static bool IsMacMetadata(string relative) =>
            relative.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
            || relative.IndexOf("/__MACOSX/", StringComparison.OrdinalIgnoreCase) >= 0
            || Path.GetFileName(relative.TrimEnd('/')).StartsWith("._", StringComparison.Ordinal)
            || string.Equals(Path.GetFileName(relative), ".DS_Store", StringComparison.OrdinalIgnoreCase);

        private static void ExtractNestedZips(string directory, int depth, Action<string> status)
        {
            if (depth > MaxNestingDepth)
            {
                return;
            }

            List<string> zips = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(f => string.Equals(Path.GetExtension(f), ".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (string zip in zips)
            {
                string target = Path.Combine(Path.GetDirectoryName(zip), Path.GetFileNameWithoutExtension(zip));
                if (Directory.Exists(target))
                {
                    // A folder with that name already exists next to the ZIP: keep both apart.
                    target += " (zip)";
                }

                status?.Invoke("Entpacke " + Path.GetFileName(zip));
                ExtractZip(zip, target);
                File.Delete(zip);
                ExtractNestedZips(target, depth + 1, status);
            }
        }

        public void Dispose() => TryDelete(WorkDirectory);

        private static void TryDelete(string directory)
        {
            try
            {
                FileTools.DeleteDirectory(directory);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // A temp folder that cannot be removed right now is harmless.
            }
        }
    }

    /// <summary>A ZIP entry that would be written outside the destination folder.</summary>
    public sealed class UnsafeZipException : IOException
    {
        public UnsafeZipException(string message)
            : base(message)
        {
        }
    }
}
