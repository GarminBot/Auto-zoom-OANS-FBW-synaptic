using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AiracUpdater.Core
{
    /// <summary>
    /// An AIRAC cycle such as 2510 (year 2025, 10th cycle). Cycles start every 28 days; the first
    /// cycle of a year is the first effective date on or after 1 January.
    /// </summary>
    public readonly struct AiracCycle : IComparable<AiracCycle>, IEquatable<AiracCycle>
    {
        // AIRAC 2401 became effective on 25 January 2024; every cycle is exactly 28 days long.
        private static readonly DateTime Reference = new DateTime(2024, 1, 25);

        public AiracCycle(int year, int number)
        {
            Year = year;
            Number = number;
        }

        /// <summary>Four-digit year, e.g. 2025.</summary>
        public int Year { get; }

        /// <summary>Cycle within the year, 1 to 14.</summary>
        public int Number { get; }

        public int Ident => (Year % 100) * 100 + Number;

        public DateTime EffectiveFrom => FirstEffectiveDate(Year).AddDays((Number - 1) * 28);

        /// <summary>Last day on which the cycle is valid.</summary>
        public DateTime EffectiveTo => EffectiveFrom.AddDays(27);

        public bool IsValid =>
            Year >= 2000 && Year <= 2099 && Number >= 1 && Number <= 14 && EffectiveFrom.Year == Year;

        public AiracCycle Next()
        {
            var next = FromDate(EffectiveFrom.AddDays(28));
            return next;
        }

        public static AiracCycle FromDate(DateTime date)
        {
            date = date.Date;
            int year = date.Year;
            DateTime first = FirstEffectiveDate(year);
            if (date < first)
            {
                year--;
                first = FirstEffectiveDate(year);
            }

            int number = (int)((date - first).TotalDays / 28) + 1;
            return new AiracCycle(year, number);
        }

        /// <summary>Parses "2510" (or "25 10"). Returns false for anything that is not a real cycle.</summary>
        public static bool TryParse(string text, out AiracCycle cycle)
        {
            cycle = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string digits = Regex.Replace(text.Trim(), @"\s+", string.Empty);
            if (digits.Length != 4 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            var candidate = new AiracCycle(2000 + value / 100, value % 100);
            if (!candidate.IsValid)
            {
                return false;
            }

            cycle = candidate;
            return true;
        }

        private static DateTime FirstEffectiveDate(int year)
        {
            double days = (new DateTime(year, 1, 1) - Reference).TotalDays;
            long steps = (long)Math.Ceiling(days / 28.0);
            return Reference.AddDays(steps * 28);
        }

        public int CompareTo(AiracCycle other) => EffectiveFrom.CompareTo(other.EffectiveFrom);

        public bool Equals(AiracCycle other) => Year == other.Year && Number == other.Number;

        public override bool Equals(object obj) => obj is AiracCycle other && Equals(other);

        public override int GetHashCode() => Year * 100 + Number;

        public override string ToString() => Ident.ToString("0000", CultureInfo.InvariantCulture);

        public static bool operator ==(AiracCycle a, AiracCycle b) => a.Equals(b);

        public static bool operator !=(AiracCycle a, AiracCycle b) => !a.Equals(b);

        public static bool operator <(AiracCycle a, AiracCycle b) => a.CompareTo(b) < 0;

        public static bool operator >(AiracCycle a, AiracCycle b) => a.CompareTo(b) > 0;
    }
}
