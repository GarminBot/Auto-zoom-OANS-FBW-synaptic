using System.Collections.Generic;

namespace AiracUpdater.Core
{
    /// <summary>All supported data formats and add-ons.</summary>
    public static class Catalog
    {
        public static IReadOnlyList<NavDataFormat> Formats { get; } = new List<NavDataFormat>();

        public static IReadOnlyList<AddonProfile> Profiles { get; } = new List<AddonProfile>();
    }
}
