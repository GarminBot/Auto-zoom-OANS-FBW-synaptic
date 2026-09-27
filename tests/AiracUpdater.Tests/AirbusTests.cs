using System.IO;
using System.Linq;
using AiracUpdater.Core;
using Xunit;

namespace AiracUpdater.Tests
{
    public class AirbusTests
    {
        [Fact]
        public void ListsAircraftThatUseTheSimulatorsNavdata()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            pc.AddMsfs2024Store(packages);
            pc.AddPackage(Path.Combine(packages, "Community"), "flybywire-aircraft-a380-842", "FlyByWire A380X");
            // The A220 under a package name the tool does not know: found by its SimObjects folder.
            string a220 = pc.AddPackage(Path.Combine(packages, "Community2024"), "some-a220-package", "A220");
            Directory.CreateDirectory(Path.Combine(a220, "SimObjects", "Airplanes", "Synaptic_A220", "presets"));
            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "MSFS"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);

            PlanItem fbw = session.Items.Single(i => i.Target.Name == "FlyByWire A380X");
            Assert.Equal(PlanState.Covered, fbw.State);
            Assert.Contains("MSFS-Navdaten", fbw.Message);
            Assert.False(fbw.CanInstall);
            PlanItem synaptic = session.Items.Single(i => i.Target.Name == "Synaptic A220");
            Assert.Equal(PlanState.Covered, synaptic.State);
            Assert.Equal(a220, synaptic.Target.TargetPath);
            Assert.Contains("NATIVE", synaptic.Message);

            // Only the simulator's navdata gets installed.
            Assert.Equal("MSFS Standard-Navdaten", session.Install(true, null, null).Single().Item.Target.Name);
        }

        [Fact]
        public void IniBuildsWithoutOwnDataUsesTheSimulatorsNavdata()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Store(packages);
            pc.AddPackage(Path.Combine(packages, "Community"), "inibuilds-aircraft-a350", "A350");
            pc.AddPackage(Path.Combine(packages, "Official2024", "OneStore"), "inibuilds-aircraft-a380", "A380");
            Directory.CreateDirectory(Path.Combine(localState, "WASM", "MSFS2024", "inibuilds-aircraft-a350", "work"));
            FakePc.WriteMsfs2024Navdata(dir.Combine("zip", "MSFS"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            foreach (string name in new[] { "iniBuilds A350", "iniBuilds A380" })
            {
                PlanItem item = session.Items.Single(i => i.Target.Name == name);
                Assert.Equal(PlanState.Covered, item.State);
                Assert.Contains("SIM DEFAULT", item.Message);
            }
        }

        [Fact]
        public void InstallsNavigationDataInterfaceDataIntoEveryIniBuildsAircraft()
        {
            using var dir = new TempDir();
            var pc = new FakePc(dir);
            string packages = dir.Combine("MSFS2024");
            string localState = pc.AddMsfs2024Steam(packages);
            foreach (string name in new[] { "inibuilds-aircraft-a350", "inibuilds-aircraft-a340", "inibuilds-aircraft-a380" })
            {
                pc.AddPackage(Path.Combine(packages, "Community"), name, name);
                string work = Path.Combine(localState, "WASM", "MSFS2024", name, "work");
                FakePc.WriteFile(Path.Combine(work, "datastore.dat"), "navigraph login");
                FakePc.WriteNdiData(Path.Combine(work, "NavigationData"), "2508");
            }

            // NDI data next to PMDG data (which also has cycle.json and an .s3db): both must be told apart.
            FakePc.WriteNdiData(dir.Combine("zip", "iniBuilds", "NavigationData"), "2510");
            FakePc.WritePmdgData(dir.Combine("zip", "PMDG"), "2510");

            using var session = new Session(pc.Folders, Catalog.Profiles, Catalog.Formats);
            session.LoadInput(dir.Combine("zip"), null);
            Assert.Equal(new[] { "Navigraph-NDI-Navdaten", "PMDG-Navdaten" }, session.DataSets.Select(d => d.Format.Name).OrderBy(n => n));

            var ini = session.Items.Where(i => i.Target.Profile.Format == Catalog.Ndi).ToList();
            Assert.Equal(3, ini.Count);
            Assert.All(ini, i => Assert.Equal(PlanState.Update, i.State));
            Assert.All(session.Install(true, null, null), r => Assert.True(r.Success, r.Message));

            foreach (string name in new[] { "inibuilds-aircraft-a350", "inibuilds-aircraft-a340", "inibuilds-aircraft-a380" })
            {
                string work = Path.Combine(localState, "WASM", "MSFS2024", name, "work");
                Assert.Contains("\"cycle\":\"2510\"", File.ReadAllText(Path.Combine(work, "NavigationData", "cycle.json")));
                // The Navigraph login next to the data stays untouched.
                Assert.Equal("navigraph login", File.ReadAllText(Path.Combine(work, "datastore.dat")));
            }
        }
    }
}
