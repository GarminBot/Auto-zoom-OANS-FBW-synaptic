using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiracUpdater.Core;
using Xunit;

namespace AiracUpdater.Tests
{
    public class SimAndFenixTests
    {
        [Fact]
        public void ReplacesTheSimPackagesAndRemovesBetaLeftovers()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            pc.AddMsfs2024Store(packages);
            string community = Path.Combine(packages, "Community");
            FakePc.WriteMsfs2024Navdata(community, "2508");
            FakePc.WriteMsfs2024Navdata(Path.Combine(dir.Path, "beta"), "2502", 1, false);
            Directory.Move(Path.Combine(dir.Path, "beta", "navigraph-nav-jepp"), Path.Combine(community, "}}}navigraph-nav-jepp"));
            pc.AddPackage(community, "some-other-addon", "Other");

            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "MSFS 2024"), "2510", 2);
            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);

            NavDataSet data = Assert.Single(session.DataSets);
            Assert.Equal("MSFS 2024", data.DisplayPath);
            Assert.Equal("2510 rev. 2", data.CycleText);
            PlanItem item = Assert.Single(session.Items);
            Assert.Equal(community, item.Target.TargetPath);
            Assert.Equal("2508 rev. 1", item.Target.InstalledCycleText);
            Assert.Equal(PlanState.Update, item.State);

            List<InstallResult> results = session.Install(true, null, null);
            Assert.True(results.Single().Success, results.Single().Message);
            Assert.Contains("AIRAC Cycle 2510 rev.2", File.ReadAllText(Path.Combine(community, "navigraph-nav-jepp", "manifest.json")));
            Assert.Contains("\"Cycle\":\"2510\"", File.ReadAllText(Path.Combine(community, "navigraph-nav-base", "ContentInfo", "navigraph-navdata", "cycle.json")));
            Assert.False(Directory.Exists(Path.Combine(community, "}}}navigraph-nav-jepp")));
            Assert.True(Directory.Exists(Path.Combine(community, "some-other-addon")));
            Assert.Equal(3, Directory.GetDirectories(community).Length);

            // Old data kept outside Community, nothing half-done left in the work area.
            string work = Path.Combine(packages, "AIRAC-Updater");
            Assert.Contains("2508", File.ReadAllText(Path.Combine(work, "backup", "msfs2024-navdata", "navigraph-nav-jepp", "manifest.json")));
            Assert.True(Directory.Exists(Path.Combine(work, "backup", "msfs2024-navdata", "entfernt", "Community", "}}}navigraph-nav-jepp")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(work, "new")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(work, "old")));

            session.Refresh();
            Assert.Equal(PlanState.UpToDate, session.Items.Single().State);
            AddonTarget target = session.Items.Single().Target;
            Assert.True(target.Profile.HasBackup(target, session.Context));
            target.Profile.RestoreBackup(target, new InstallOptions(session.Context, false, null));
            Assert.Contains("AIRAC Cycle 2508", File.ReadAllText(Path.Combine(community, "navigraph-nav-jepp", "manifest.json")));
        }

        [Fact]
        public void InstallsSimPackagesForTheFirstTimeIntoCommunity()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            pc.AddMsfs2024Steam(packages);
            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "sim"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            PlanItem item = Assert.Single(session.Items);
            Assert.Equal(PlanState.Unknown, item.State);
            Assert.Equal("noch keine Navdaten installiert", item.Message);
            Assert.True(session.Install(true, null, null).Single().Success);
            Assert.True(File.Exists(Path.Combine(packages, "Community", "navigraph-nav-base", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(packages, "Community", "navigraph-nav-jepp", "manifest.json")));
        }

        [Fact]
        public void KeepsTheSimPackagesInCommunity2024AndRemovesTheCopyInCommunity()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            pc.AddMsfs2024Store(packages);
            FakePc.WriteMsfs2024Navdata(Path.Combine(packages, "Community2024"), "2508");
            FakePc.WriteMsfs2024Navdata(Path.Combine(packages, "Community"), "2507");
            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "MSFS"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            Assert.Equal(Path.Combine(packages, "Community2024"), session.Items.Single().Target.TargetPath);
            Assert.True(session.Install(false, null, null).Single().Success);
            Assert.Contains("2510", File.ReadAllText(Path.Combine(packages, "Community2024", "navigraph-nav-jepp", "manifest.json")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(packages, "Community")));
        }

        [Fact]
        public void RefusesACyclePackageWithoutBase()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            pc.AddMsfs2024Store(dir.Combine("MSFS2024"));
            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "MSFS"), "2510", 1, false);

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            InstallResult result = session.Install(true, null, null).Single();
            Assert.False(result.Success);
            Assert.Contains("navigraph-nav-base fehlt", result.Message);
        }

        [Fact]
        public void UpdatesFenixForAllVariants()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            pc.AddMsfs2024Store(packages);
            pc.AddPackage(Path.Combine(packages, "Community"), "fnx-aircraft-320", "Fenix A320");
            pc.AddPackage(Path.Combine(packages, "Community"), "fnx-aircraft-319-321", "Fenix A319/A321");
            string navdata = Path.Combine(pc.Folders.ProgramData, "Fenix", "Navdata");
            FakePc.WriteFenixData(navdata, "2508", true);
            FakePc.WriteFenixData(dir.Combine("zip", "Fenix A320", "Navdata"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            PlanItem fenix = session.Items.Single(i => i.Target.Profile.Id == "fenix");
            Assert.Equal("MSFS 2024", fenix.Target.SimLabel);
            Assert.Equal(PlanState.Update, fenix.State);
            Assert.True(session.Install(true, null, null).Single(r => r.Item == fenix).Success);

            Assert.Contains("2510", File.ReadAllText(Path.Combine(navdata, "cycle_info.txt")));
            // Like Hub: the whole folder is replaced; Fenix imports nd.db3 again at its next start.
            Assert.False(File.Exists(Path.Combine(navdata, "imported.db3")));
            Assert.Equal(3, Directory.GetFiles(navdata).Length);
        }

        [Fact]
        public void FindsFenixByItsAppFolderAlone()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            Directory.CreateDirectory(Path.Combine(pc.Folders.ProgramData, "Fenix", "App"));
            var context = ToolContext.Discover(pc.Folders);
            AddonTarget target = Catalog.Profiles.SelectMany(p => p.Locate(context)).Single();
            Assert.Equal("Fenix A319/A320/A321", target.Name);
            Assert.Equal("–", target.SimLabel);
            Assert.False(target.HasData);
        }

        /// <summary>The goal: one ZIP with a folder per add-on, one click, everything installed is updated.</summary>
        [Fact]
        public void OneZipUpdatesEveryInstalledAddon()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Store(packages);
            string community = Path.Combine(packages, "Community");
            FakePc.WriteMsfs2024Navdata(community, "2508");
            pc.AddPackage(community, "fnx-aircraft-320", "Fenix A320");
            FakePc.WriteFenixData(Path.Combine(pc.Folders.ProgramData, "Fenix", "Navdata"), "2508", true);
            foreach (string pmdg in new[] { "pmdg-aircraft-738", "pmdg-aircraft-77w", "pmdg-aircraft-77f" })
            {
                pc.AddPackage(community, pmdg, pmdg);
                FakePc.WritePmdgData(Path.Combine(localState, "WASM", "MSFS2024", pmdg, "work", "NavigationData"), "2508");
            }

            // PMDG data packed as its own ZIP inside the big ZIP, like a downloaded file.
            FakePc.WritePmdgData(dir.Combine("pmdgzip", "NavigationData"), "2510");
            dir.Zip("pmdgzip", "zip/PMDG/pmdg_2510.zip");
            FakePc.WriteFenixData(dir.Combine("zip", "Fenix", "Navdata"), "2510");
            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "MSFS 2024 Standard"), "2510");
            dir.Write("zip/iniBuilds A350/liesmich.txt", "nichts");
            string zip = dir.Zip("zip", "AIRAC 2510.zip");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(zip, null);
            Assert.Equal(3, session.DataSets.Count);
            Assert.Contains(session.Notes, n => n.Contains("iniBuilds A350"));
            Assert.Equal(5, session.Items.Count(i => i.Selected && i.CanInstall));

            List<InstallResult> results = session.Install(true, null, null);
            Assert.Equal(5, results.Count);
            Assert.All(results, r => Assert.True(r.Success, r.Item.Target.Name + ": " + r.Message));

            session.Refresh();
            Assert.All(session.Items, i => Assert.Equal("2510 rev. 1", i.Target.InstalledCycleText));
            Assert.All(session.Items, i => Assert.Equal(PlanState.UpToDate, i.State));
        }
    }
}
