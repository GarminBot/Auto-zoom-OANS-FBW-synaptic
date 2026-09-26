using System;
using AiracUpdater.Core;
using Xunit;

namespace AiracUpdater.Tests
{
    public class CycleTests
    {
        [Theory]
        [InlineData("2301", 2023, 1, 26)]
        [InlineData("2313", 2023, 12, 28)]
        [InlineData("2401", 2024, 1, 25)]
        [InlineData("2501", 2025, 1, 23)]
        [InlineData("2509", 2025, 9, 4)]
        [InlineData("2601", 2026, 1, 22)]
        [InlineData("2609", 2026, 9, 3)]
        [InlineData("2610", 2026, 10, 1)]
        [InlineData("2014", 2020, 12, 31)]
        public void EffectiveDates(string ident, int year, int month, int day)
        {
            Assert.True(AiracCycle.TryParse(ident, out AiracCycle cycle));
            Assert.Equal(new DateTime(year, month, day), cycle.EffectiveFrom);
            Assert.Equal(ident, cycle.ToString());
        }

        [Theory]
        [InlineData("2024")] // "MSFS 2024" is no cycle: there is no 24th cycle
        [InlineData("2020")]
        [InlineData("2114")] // 2021 had only 13 cycles
        [InlineData("2500")]
        [InlineData("25100")]
        [InlineData("abcd")]
        [InlineData("")]
        public void RejectsNonCycles(string text)
        {
            Assert.False(AiracCycle.TryParse(text, out _));
        }

        [Theory]
        [InlineData(2026, 9, 26, "2609")]
        [InlineData(2026, 10, 1, "2610")]
        [InlineData(2026, 1, 21, "2513")]
        [InlineData(2026, 1, 22, "2601")]
        [InlineData(2020, 12, 31, "2014")]
        public void CycleForDate(int year, int month, int day, string expected)
        {
            Assert.Equal(expected, AiracCycle.FromDate(new DateTime(year, month, day)).ToString());
        }

        [Fact]
        public void NextAndOrder()
        {
            AiracCycle.TryParse("2513", out AiracCycle last);
            Assert.Equal("2601", last.Next().ToString());
            AiracCycle.TryParse("2601", out AiracCycle first);
            Assert.True(first > last);
            Assert.True(last < first);
        }

        [Fact]
        public void ReadsNavigraphCycleInfo()
        {
            const string text = "AIRAC cycle    : 2510\r\nVersion        : 2\r\nValid (from/to): 02/OCT/2025 - 30/OCT/2025\r\nForum          : http://forum.navigraph.com\r\n";
            Assert.True(CycleText.TryParse(text, out AiracCycle cycle, out int revision));
            Assert.Equal("2510", cycle.ToString());
            Assert.Equal(2, revision);
        }

        [Fact]
        public void ReadsJsonCycle()
        {
            Assert.True(CycleText.TryParse("{ \"cycle\": \"2509\", \"revision\": \"3\" }", out AiracCycle cycle, out int revision));
            Assert.Equal("2509", cycle.ToString());
            Assert.Equal(3, revision);
        }

        [Theory]
        [InlineData("navigraph_pmdg_2510", "2510")]
        [InlineData("MSFS 2024 AIRAC 2509 rev1", "2509")]
        [InlineData("Fenix-2601", "2601")]
        public void CycleFromName(string name, string expected)
        {
            Assert.True(CycleText.TryParseName(name, out AiracCycle cycle));
            Assert.Equal(expected, cycle.ToString());
        }

        [Theory]
        [InlineData("PMDG 737")]
        [InlineData("MSFS 2024")]
        [InlineData("A320")]
        public void NoCycleInName(string name)
        {
            Assert.False(CycleText.TryParseName(name, out _));
        }
    }
}
