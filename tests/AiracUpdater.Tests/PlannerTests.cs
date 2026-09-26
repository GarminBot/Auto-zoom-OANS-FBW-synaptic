using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiracUpdater.Core;
using Xunit;

namespace AiracUpdater.Tests
{
    public class PlannerTests
    {
        private static readonly FileSignatureFormat TestFormat =
            new FileSignatureFormat("test", "Testformat", new[] { "data.txt" }, null, new[] { "cycle_info.txt" });

        /// <summary>A profile whose single target is a fixed folder.</summary>
        private sealed class FolderProfile : AddonProfile
        {
            private readonly string folder;

            public FolderProfile(string id, string folder, params string[] keywords)
                : base(id, id, TestFormat, keywords)
            {
                this.folder = folder;
            }

            public override IEnumerable<AddonTarget> Locate(ToolContext context)
            {
                yield return Target(null, Name, folder, null);
            }
        }

        private static NavDataSet Data(string display, string cycle)
        {
            AiracCycle.TryParse(cycle, out AiracCycle parsed);
            return new NavDataSet(TestFormat, "/nowhere/" + display, display, parsed, 0);
        }

        [Fact]
        public void PrefersTheFolderNamedAfterTheAddon()
        {
            var p737 = new FolderProfile("pmdg-737", "/x/737", "737");
            var p777 = new FolderProfile("pmdg-777", "/x/777", "777");
            var profiles = new List<AddonProfile> { p737, p777 };
            var sets = new List<NavDataSet> { Data("PMDG 777", "2510"), Data("PMDG 737", "2509") };

            Assert.Equal("PMDG 737", Planner.Choose(p737, sets, profiles).DisplayPath);
            Assert.Equal("PMDG 777", Planner.Choose(p777, sets, profiles).DisplayPath);
        }

        [Fact]
        public void GenericFolderBeatsAFolderNamedAfterAnotherAddon()
        {
            var p737 = new FolderProfile("pmdg-737", "/x/737", "737");
            var p777 = new FolderProfile("pmdg-777", "/x/777", "777");
            var profiles = new List<AddonProfile> { p737, p777 };
            var sets = new List<NavDataSet> { Data("PMDG 777", "2510"), Data("PMDG", "2510") };

            Assert.Equal("PMDG", Planner.Choose(p737, sets, profiles).DisplayPath);
            Assert.Equal("PMDG 777", Planner.Choose(p777, sets, profiles).DisplayPath);
        }

        [Fact]
        public void TakesTheNewestCycleOtherwise()
        {
            var profile = new FolderProfile("fenix", "/x/fenix");
            var sets = new List<NavDataSet> { Data("alt", "2508"), Data("neu", "2510"), Data("mittel", "2509") };
            Assert.Equal("neu", Planner.Choose(profile, sets, new List<AddonProfile> { profile }).DisplayPath);
        }

        [Fact]
        public void StatesFollowTheCycles()
        {
            using var dir = new TempDir();
            dir.Write("old/cycle_info.txt", "AIRAC cycle : 2509");
            dir.Write("same/cycle_info.txt", "AIRAC cycle : 2510");
            dir.Write("newer/cycle_info.txt", "AIRAC cycle : 2511");
            var profiles = new List<AddonProfile>
            {
                new FolderProfile("a", dir.Combine("old")),
                new FolderProfile("b", dir.Combine("same")),
                new FolderProfile("c", dir.Combine("newer")),
                new FolderProfile("d", dir.Combine("missing")),
            };
            var targets = profiles.SelectMany(p => p.Locate(null)).ToList();
            List<PlanItem> items = Planner.Plan(targets, new List<NavDataSet> { Data("zip", "2510") }, profiles);

            Assert.Equal(PlanState.Update, items[0].State);
            Assert.True(items[0].Selected);
            Assert.Equal(PlanState.UpToDate, items[1].State);
            Assert.False(items[1].Selected);
            Assert.Equal(PlanState.Older, items[2].State);
            Assert.False(items[2].Selected);
            Assert.Equal(PlanState.Unknown, items[3].State);
            Assert.True(items[3].Selected);
        }

        [Fact]
        public void NoDataWhenTheZipHasNothingForTheAddon()
        {
            var profile = new FolderProfile("a", "/x/a");
            List<PlanItem> items = Planner.Plan(profile.Locate(null).ToList(), new List<NavDataSet>(), new List<AddonProfile> { profile });
            Assert.Equal(PlanState.NoData, items.Single().State);
            Assert.False(items.Single().CanInstall);
        }

        [Fact]
        public void ScannerFindsDataSetsAndCyclesFromFolderNames()
        {
            using var dir = new TempDir();
            dir.Write("PMDG 737/navigraph_2510/data.txt", "x");
            dir.Write("Fenix/data.txt", "x");
            dir.Write("Fenix/cycle_info.txt", "AIRAC cycle : 2509\nRevision : 2");
            dir.Write("Leer/readme.txt", "nichts");

            List<NavDataSet> sets = DataSetScanner.Scan(dir.Path, new List<NavDataFormat> { TestFormat });
            Assert.Equal(2, sets.Count);
            NavDataSet fenix = sets.Single(s => s.DisplayPath == "Fenix");
            Assert.Equal("2509 rev. 2", fenix.CycleText);
            NavDataSet pmdg = sets.Single(s => s.DisplayPath == "PMDG 737/navigraph_2510");
            Assert.Equal("2510", pmdg.CycleText);
            Assert.Equal(Path.Combine(dir.Path, "PMDG 737", "navigraph_2510"), pmdg.ContentPath);
        }
    }
}
