using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AiracUpdater.Tests
{
    /// <summary>A temporary directory that is deleted after the test.</summary>
    public sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airac-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts) => System.IO.Path.Combine(Path, System.IO.Path.Combine(parts));

        /// <summary>Writes a file below the directory; "/" in the relative path separates folders.</summary>
        public string Write(string relative, string content)
        {
            string file = Combine(relative.Split('/'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
            File.WriteAllText(file, content, new UTF8Encoding(false));
            return file;
        }

        public string Read(string relative) => File.ReadAllText(Combine(relative.Split('/')));

        public bool Exists(string relative)
        {
            string path = Combine(relative.Split('/'));
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>Packs a folder below this directory into a ZIP file.</summary>
        public string Zip(string folderRelative, string zipRelative)
        {
            string zip = Combine(zipRelative.Split('/'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(zip));
            if (File.Exists(zip))
            {
                File.Delete(zip);
            }

            ZipFile.CreateFromDirectory(Combine(folderRelative.Split('/')), zip, CompressionLevel.Fastest, false);
            return zip;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
