using System.IO;
using System.Text.RegularExpressions;

namespace AiracUpdater.Core
{
    /// <summary>Reads the AIRAC cycle from the small text files that navdata sets carry.</summary>
    public static class CycleText
    {
        // Navigraph cycle_info.txt: "AIRAC cycle    : 2510" and "Version        : 1" (or "Revision").
        private static readonly Regex CycleLine =
            new Regex(@"AIRAC[ \t]*cycle[ \t]*:?[ \t]*(\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RevisionLine =
            new Regex(@"^[ \t]*(?:Version|Revision)[ \t]*:?[ \t]*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        // JSON such as {"cycle": "2510", "revision": "1"} or {"airac": 2510}.
        private static readonly Regex JsonCycle =
            new Regex(@"""(?:cycle|airac|airac_?cycle|navdata_?cycle)""\s*:\s*""?(\d{4})""?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex JsonRevision =
            new Regex(@"""revision""\s*:\s*""?(\d+)""?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex FourDigits = new Regex(@"(?<!\d)(\d{4})(?!\d)", RegexOptions.CultureInvariant);

        public static bool TryParse(string text, out AiracCycle cycle, out int revision)
        {
            cycle = default;
            revision = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            Match match = CycleLine.Match(text);
            if (!match.Success)
            {
                match = JsonCycle.Match(text);
            }

            if (!match.Success || !AiracCycle.TryParse(match.Groups[1].Value, out cycle))
            {
                return false;
            }

            Match rev = RevisionLine.Match(text);
            if (!rev.Success)
            {
                rev = JsonRevision.Match(text);
            }

            if (rev.Success)
            {
                int.TryParse(rev.Groups[1].Value, out revision);
            }

            return true;
        }

        public static bool TryParseFile(string path, out AiracCycle cycle, out int revision)
        {
            cycle = default;
            revision = 0;
            try
            {
                var info = new FileInfo(path);
                // Cycle files are tiny; anything big is not one of them.
                if (!info.Exists || info.Length > 64 * 1024)
                {
                    return false;
                }

                return TryParse(File.ReadAllText(path), out cycle, out revision);
            }
            catch (IOException)
            {
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Finds a cycle in a file or folder name such as "navigraph_pmdg_2510.zip".</summary>
        public static bool TryParseName(string name, out AiracCycle cycle)
        {
            cycle = default;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (Match match in FourDigits.Matches(name))
            {
                if (AiracCycle.TryParse(match.Groups[1].Value, out cycle) && cycle.Year >= 2015)
                {
                    return true;
                }
            }

            cycle = default;
            return false;
        }
    }
}
