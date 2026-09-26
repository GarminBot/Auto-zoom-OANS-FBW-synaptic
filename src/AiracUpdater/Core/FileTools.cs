using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>File-system helpers that behave the same on Windows and in the Linux tests.</summary>
    public static class FileTools
    {
        public static bool NameIs(string path, string name) =>
            string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase);

        /// <summary>Finds a file directly inside a folder, ignoring case (Windows semantics).</summary>
        public static string FindFile(string directory, string name)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            return SafeFiles(directory).FirstOrDefault(f => NameIs(f, name));
        }

        /// <summary>Finds a sub-folder directly inside a folder, ignoring case.</summary>
        public static string FindDirectory(string directory, string name)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            return SafeDirectories(directory).FirstOrDefault(d => NameIs(d, name));
        }

        public static IEnumerable<string> SafeFiles(string directory)
        {
            try
            {
                return Directory.GetFiles(directory);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        public static IEnumerable<string> SafeDirectories(string directory)
        {
            try
            {
                return Directory.GetDirectories(directory);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        public static bool HasAnyEntry(string directory) =>
            Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();

        /// <summary>Relative path with the platform separator (Path.GetRelativePath is missing in .NET Framework).</summary>
        public static string RelativePath(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            if (string.Equals(fullRoot, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string prefix = fullRoot + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(prefix.Length);
            }

            return fullPath;
        }

        /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or lies below it.</summary>
        public static bool IsInside(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullRoot, fullPath, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static long CopyDirectory(string source, string target, Action<long> bytesCopied = null)
        {
            Directory.CreateDirectory(target);
            long total = 0;
            foreach (string file in Directory.GetFiles(source))
            {
                string destination = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, destination, true);
                // Files from a download can be read-only; the next update must be able to replace them.
                File.SetAttributes(destination, FileAttributes.Normal);
                long length = new FileInfo(destination).Length;
                total += length;
                bytesCopied?.Invoke(length);
            }

            foreach (string directory in Directory.GetDirectories(source))
            {
                total += CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), bytesCopied);
            }

            return total;
        }

        public static long DirectorySize(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            long total = 0;
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(file).Length;
            }

            return total;
        }

        /// <summary>Deletes a folder even if some files in it are read-only.</summary>
        public static void DeleteDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            // A junction or symbolic link: remove the link only, never the folder it points to.
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory, false);
                return;
            }

            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, true);
        }

        /// <summary>Removes everything inside a folder but keeps the folder itself.</summary>
        public static void ClearDirectory(string directory)
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string sub in Directory.GetDirectories(directory))
            {
                DeleteDirectory(sub);
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1L << 30)
            {
                return (bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " GB";
            }

            if (bytes >= 1L << 20)
            {
                return (bytes / (double)(1L << 20)).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MB";
            }

            return (bytes / 1024.0).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " KB";
        }
    }
}
