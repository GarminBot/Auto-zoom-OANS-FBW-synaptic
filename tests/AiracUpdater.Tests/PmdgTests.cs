using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiracUpdater.Core;
using Xunit;

namespace AiracUpdater.Tests
{
    public class PmdgTests
    {
        [Fact]
        public void UpdatesEveryInstalledPmdgAircraftInEverySim()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Store(packages);

            // 737-800 in Community with old data, 777-300ER in Community2024 with an empty work folder,
            // 737-600 installed but never loaded (no work folder yet).
            pc.AddPackage(Path.Combine(packages, "Community"), "pmdg-aircraft-738", "PMDG 737-800");
            FakePc.WritePmdgData(Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-738", "work", "NavigationData"), "2508");
            pc.AddPackage(Path.Combine(packages, "Community2024"), "pmdg-aircraft-77w", "PMDG 777-300ER");
            Directory.CreateDirectory(Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-77w", "work"));
            pc.AddPackage(Path.Combine(packages, "Community"), "pmdg-aircraft-736", "PMDG 737-600");

            // MSFS 2020 (Store) with the 737-800 as well: work folder in LocalState\packages.
            string packages2020 = dir.Combine("MSFS2020");
            string localState2020 = pc.AddMsfs2020Store(packages2020);
            pc.AddPackage(Path.Combine(packages2020, "Community"), "pmdg-aircraft-738", "PMDG 737-800");
            FakePc.WritePmdgData(Path.Combine(localState2020, "packages", "pmdg-aircraft-738", "work", "NavigationData"), "2507");

            // The user's ZIP: folder "PMDG" with Navigraph's NavigationData inside.
            FakePc.WritePmdgData(dir.Combine("zip", "PMDG", "NavigationData"), "2510", "2");
            dir.Write("zip/Readme.txt", "meine AIRAC-Daten");
            string zip = dir.Zip("zip", "airac.zip");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.Refresh();
            Assert.Equal(2, session.Context.Sims.Count);
            session.LoadInput(zip, null);

            NavDataSet data = Assert.Single(session.DataSets);
            Assert.Equal("PMDG/NavigationData", data.DisplayPath);
            Assert.Equal("2510 rev. 2", data.CycleText);

            PlanItem p738 = session.Items.Single(i => i.Target.Name == "PMDG 737-800" && i.Target.Sim.Version == SimVersion.Msfs2024);
            Assert.Equal(PlanState.Update, p738.State);
            Assert.Equal("2508 rev. 1", p738.Target.InstalledCycleText);
            PlanItem p738old = session.Items.Single(i => i.Target.Name == "PMDG 737-800" && i.Target.Sim.Version == SimVersion.Msfs2020);
            Assert.Equal(PlanState.Update, p738old.State);
            PlanItem p77w = session.Items.Single(i => i.Target.Name == "PMDG 777-300ER");
            Assert.Equal(PlanState.Unknown, p77w.State);
            PlanItem p736 = session.Items.Single(i => i.Target.Name == "PMDG 737-600");
            Assert.Equal(PlanState.NotReady, p736.State);
            Assert.False(p736.CanInstall);
            Assert.Equal(4, session.Items.Count);

            var log = new List<string>();
            List<InstallResult> results = session.Install(true, log.Add, null);
            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.True(r.Success, r.Message));

            string nav738 = Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-738", "work", "NavigationData");
            Assert.Contains("\"cycle\":\"2510\"", File.ReadAllText(Path.Combine(nav738, "cycle.json")));
            Assert.Equal(3, Directory.GetFiles(nav738).Length);
            Assert.True(File.Exists(Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-77w", "work", "NavigationData", "e_dfd_PMDG.s3db")));
            Assert.Contains("2510", File.ReadAllText(Path.Combine(localState2020, "packages", "pmdg-aircraft-738", "work", "NavigationData", "cycle_info.txt")));

            // Backup of the replaced 2508 data, nothing left over in the work folders.
            string backup = AddonProfile.BackupFolder(p738.Target, session.Context);
            Assert.Contains("2508", File.ReadAllText(Path.Combine(backup, "cycle.json")));
            Assert.Single(Directory.GetDirectories(Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-738", "work")));

            // After the update everything reads as current.
            session.Refresh();
            Assert.All(session.Items.Where(i => i.State != PlanState.NotReady), i => Assert.Equal(PlanState.UpToDate, i.State));
        }

        [Fact]
        public void RestoreBringsBackTheOldCycle()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Steam(packages);
            pc.AddPackage(Path.Combine(packages, "Community"), "pmdg-aircraft-77f", "PMDG 777F");
            string nav = Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-77f", "work", "NavigationData");
            FakePc.WritePmdgData(nav, "2509");
            FakePc.WritePmdgData(dir.Combine("zip", "PMDG 777", "x"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            PlanItem item = Assert.Single(session.Items);
            Assert.Equal("MSFS 2024 (Steam)", item.Target.Sim.Name);
            Assert.All(session.Install(true, null, null), r => Assert.True(r.Success, r.Message));
            Assert.Contains("2510", File.ReadAllText(Path.Combine(nav, "cycle.json")));

            session.Refresh();
            AddonTarget target = session.Items.Single().Target;
            Assert.True(target.Profile.HasBackup(target, session.Context));
            target.Profile.RestoreBackup(target, new InstallOptions(session.Context, false, null));
            Assert.Contains("2509", File.ReadAllText(Path.Combine(nav, "cycle.json")));
        }

        [Fact]
        public void FindsTheWorkFolderOfA2020BuildInMsfs2024()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Store(packages);
            pc.AddPackage(Path.Combine(packages, "Official2024", "OneStore"), "pmdg-aircraft-77er", "PMDG 777-200ER");
            Directory.CreateDirectory(Path.Combine(localState, "WASM", "MSFS2020", "pmdg-aircraft-77er", "work"));

            var context = ToolContext.Discover(pc.Folders);
            AddonTarget target = Catalog.Profiles.SelectMany(p => p.Locate(context)).Single();
            Assert.Null(target.Problem);
            Assert.Equal(Path.Combine(localState, "WASM", "MSFS2020", "pmdg-aircraft-77er", "work", "NavigationData"), target.TargetPath);
        }

        [Fact]
        public void FindsStreamedMarketplacePackages()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Store(packages);
            Directory.CreateDirectory(Path.Combine(packages, "StreamedPackages", "fs24-pmdg-aircraft-739"));
            Directory.CreateDirectory(Path.Combine(localState, "WASM", "MSFS2024", "pmdg-aircraft-739", "work"));

            var context = ToolContext.Discover(pc.Folders);
            Assert.Equal("PMDG 737-900", Catalog.Profiles.SelectMany(p => p.Locate(context)).Single().Name);
        }
    }
}
