using System.IO;
using System.IO.Compression;
using AiracUpdater.Core;
using Xunit;

namespace AiracUpdater.Tests
{
    public class FileOperationTests
    {
        [Fact]
        public void UnpacksZipsInsideTheZip()
        {
            using var dir = new TempDir();
            dir.Write("inner/data/cycle_info.txt", "AIRAC cycle : 2510");
            dir.Zip("inner", "outer/Fenix/navigraph_fenix_2510.zip");
            dir.Write("outer/PMDG/wpNavAPT.txt", "x");
            string zip = dir.Zip("outer", "input.zip");

            using InputFolder input = InputFolder.Open(zip, dir.Combine("tmp"));
            Assert.True(File.Exists(Path.Combine(input.Root, "PMDG", "wpNavAPT.txt")));
            Assert.True(File.Exists(Path.Combine(input.Root, "Fenix", "navigraph_fenix_2510", "data", "cycle_info.txt")));
            Assert.False(File.Exists(Path.Combine(input.Root, "Fenix", "navigraph_fenix_2510.zip")));

            string work = input.WorkDirectory;
            input.Dispose();
            Assert.False(Directory.Exists(work));
        }

        [Fact]
        public void AcceptsAFolderInsteadOfAZip()
        {
            using var dir = new TempDir();
            dir.Write("in/A/file.txt", "a");
            using InputFolder input = InputFolder.Open(dir.Combine("in"), dir.Combine("tmp"));
            Assert.True(File.Exists(Path.Combine(input.Root, "A", "file.txt")));
            // The user's folder itself is never touched.
            Assert.True(dir.Exists("in/A/file.txt"));
        }

        [Fact]
        public void RefusesZipSlip()
        {
            using var dir = new TempDir();
            string zip = dir.Combine("evil.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new StreamWriter(archive.CreateEntry("../../escaped.txt").Open());
                writer.Write("x");
            }

            Assert.Throws<UnsafeZipException>(() => InputFolder.Open(zip, dir.Combine("tmp")));
            Assert.False(File.Exists(dir.Combine("escaped.txt")));
        }

        [Fact]
        public void SkipsMacMetadata()
        {
            using var dir = new TempDir();
            string zip = dir.Combine("mac.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (string name in new[] { "__MACOSX/PMDG/._wpNavAPT.txt", "PMDG/wpNavAPT.txt", "PMDG/.DS_Store" })
                {
                    using StreamWriter writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write("x");
                }
            }

            using InputFolder input = InputFolder.Open(zip, dir.Combine("tmp"));
            Assert.True(File.Exists(Path.Combine(input.Root, "PMDG", "wpNavAPT.txt")));
            Assert.False(Directory.Exists(Path.Combine(input.Root, "__MACOSX")));
            Assert.False(File.Exists(Path.Combine(input.Root, "PMDG", ".DS_Store")));
        }

        [Fact]
        public void ReplaceSwapsContentAndKeepsBackup()
        {
            using var dir = new TempDir();
            dir.Write("new/cycle_info.txt", "AIRAC cycle : 2510");
            dir.Write("new/SIDSTARS/EDDF.txt", "new");
            dir.Write("target/NavigationData/cycle_info.txt", "AIRAC cycle : 2509");
            dir.Write("target/NavigationData/SIDSTARS/OLD.txt", "old");

            FolderSwap.Replace(dir.Combine("new"), dir.Combine("target", "NavigationData"), dir.Combine("backup", "b1"), null);

            Assert.Equal("AIRAC cycle : 2510", dir.Read("target/NavigationData/cycle_info.txt"));
            Assert.True(dir.Exists("target/NavigationData/SIDSTARS/EDDF.txt"));
            Assert.False(dir.Exists("target/NavigationData/SIDSTARS/OLD.txt"));
            Assert.Equal("AIRAC cycle : 2509", dir.Read("backup/b1/cycle_info.txt"));
            Assert.False(dir.Exists("target/NavigationData" + FolderSwap.NewSuffix));
            Assert.False(dir.Exists("target/NavigationData" + FolderSwap.OldSuffix));
            Assert.Single(Directory.GetDirectories(dir.Combine("target")));
        }

        [Fact]
        public void ReplaceCreatesAMissingTarget()
        {
            using var dir = new TempDir();
            dir.Write("new/cycle_info.txt", "AIRAC cycle : 2510");
            FolderSwap.Replace(dir.Combine("new"), dir.Combine("work", "NavigationData"), dir.Combine("backup"), null);
            Assert.True(dir.Exists("work/NavigationData/cycle_info.txt"));
            Assert.False(dir.Exists("backup"));
        }

        [Fact]
        public void ReplaceWithoutBackupDeletesOldData()
        {
            using var dir = new TempDir();
            dir.Write("new/a.txt", "new");
            dir.Write("target/T/b.txt", "old");
            FolderSwap.Replace(dir.Combine("new"), dir.Combine("target", "T"), null, null);
            Assert.True(dir.Exists("target/T/a.txt"));
            Assert.False(dir.Exists("target/T/b.txt"));
            Assert.Single(Directory.GetDirectories(dir.Combine("target")));
        }

        [Fact]
        public void ReplaceRepairsAnInterruptedRun()
        {
            using var dir = new TempDir();
            // An earlier run stopped after moving the old data away and before moving the new data in.
            dir.Write("target/T" + FolderSwap.OldSuffix + "/b.txt", "old");
            dir.Write("target/T" + FolderSwap.NewSuffix + "/half.txt", "half");
            dir.Write("new/a.txt", "new");
            FolderSwap.Replace(dir.Combine("new"), dir.Combine("target", "T"), dir.Combine("backup"), null);
            Assert.True(dir.Exists("target/T/a.txt"));
            Assert.Equal("old", dir.Read("backup/b.txt"));
            Assert.Single(Directory.GetDirectories(dir.Combine("target")));
        }

        [Fact]
        public void BackupReplacesTheOlderBackup()
        {
            using var dir = new TempDir();
            dir.Write("backup/stale.txt", "stale");
            dir.Write("target/T/b.txt", "old");
            dir.Write("new/a.txt", "new");
            FolderSwap.Replace(dir.Combine("new"), dir.Combine("target", "T"), dir.Combine("backup"), null);
            Assert.True(dir.Exists("backup/b.txt"));
            Assert.False(dir.Exists("backup/stale.txt"));
        }
    }
}
