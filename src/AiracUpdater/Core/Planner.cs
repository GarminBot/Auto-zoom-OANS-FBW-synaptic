using System;
using System.Collections.Generic;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>Decides which data set goes to which installed add-on.</summary>
    public static class Planner
    {
        public static List<PlanItem> Plan(IReadOnlyList<AddonTarget> targets, IReadOnlyList<NavDataSet> dataSets, IReadOnlyList<AddonProfile> profiles)
        {
            var items = new List<PlanItem>();
            foreach (AddonTarget target in targets)
            {
                AddonProfile profile = target.Profile;
                if (profile.Format == null)
                {
                    items.Add(new PlanItem(target, null, PlanState.Covered, profile.CoveredBy ?? "keine eigenen Navdaten"));
                    continue;
                }

                NavDataSet data = Choose(profile, dataSets, profiles);
                if (target.Problem != null)
                {
                    items.Add(new PlanItem(target, data, PlanState.NotReady, target.Problem));
                    continue;
                }

                if (data == null)
                {
                    items.Add(new PlanItem(target, null, PlanState.NoData, "keine passenden Daten in der ZIP"));
                    continue;
                }

                items.Add(Compare(target, data));
            }

            return items;
        }

        private static PlanItem Compare(AddonTarget target, NavDataSet data)
        {
            if (!data.Cycle.HasValue || !target.InstalledCycle.HasValue)
            {
                string message = !target.InstalledCycle.HasValue
                    ? (target.HasData ? "installierter Zyklus unbekannt" : "noch keine Navdaten installiert")
                    : "Zyklus der ZIP-Daten unbekannt";
                return new PlanItem(target, data, PlanState.Unknown, message);
            }

            AiracCycle installed = target.InstalledCycle.Value;
            AiracCycle offered = data.Cycle.Value;
            if (offered > installed || (offered == installed && data.Revision > target.InstalledRevision))
            {
                return new PlanItem(target, data, PlanState.Update, "Update " + target.InstalledCycleText + " → " + data.CycleText);
            }

            if (offered == installed)
            {
                return new PlanItem(target, data, PlanState.UpToDate, "bereits aktuell");
            }

            return new PlanItem(target, data, PlanState.Older, "ZIP enthält einen älteren Zyklus");
        }

        /// <summary>
        /// Several data sets of the same format: prefer one whose folder name names this add-on
        /// (e.g. "PMDG 777" for a 777), avoid one that names another add-on, then take the newest.
        /// </summary>
        public static NavDataSet Choose(AddonProfile profile, IReadOnlyList<NavDataSet> dataSets, IReadOnlyList<AddonProfile> profiles)
        {
            List<NavDataSet> candidates = dataSets.Where(d => d.Format.Id == profile.Format.Id).ToList();
            if (candidates.Count <= 1)
            {
                return candidates.FirstOrDefault();
            }

            List<string> otherKeywords = profiles
                .Where(p => p != profile && p.Format != null && p.Format.Id == profile.Format.Id)
                .SelectMany(p => p.Keywords)
                .Where(k => !profile.Keywords.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return candidates
                .OrderByDescending(d => Score(d, profile.Keywords, otherKeywords))
                .ThenByDescending(d => d.Cycle.HasValue ? d.Cycle.Value.EffectiveFrom : DateTime.MinValue)
                .ThenByDescending(d => d.Revision)
                .First();
        }

        /// <summary>
        /// 0: the folder names another add-on more precisely than this one; 1: names no add-on;
        /// above 2: names this add-on, the longer (more specific) the matching word the better.
        /// </summary>
        private static double Score(NavDataSet data, IReadOnlyList<string> own, IReadOnlyList<string> others)
        {
            string path = data.DisplayPath.ToLowerInvariant();
            int ownLength = own.Where(k => path.Contains(k.ToLowerInvariant())).Select(k => k.Length).DefaultIfEmpty(0).Max();
            int otherLength = others.Where(k => path.Contains(k.ToLowerInvariant())).Select(k => k.Length).DefaultIfEmpty(0).Max();
            if (ownLength == 0 && otherLength == 0)
            {
                return 1;
            }

            return ownLength > otherLength ? 2 + ownLength / 100.0 : 0;
        }
    }
}
