using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AiracUpdater.Core
{
    /// <summary>Programs that keep navdata files open; updating while they run fails or corrupts data.</summary>
    public static class Blockers
    {
        private static readonly (string Process, string Name)[] Known =
        {
            ("FlightSimulator2024", "Microsoft Flight Simulator 2024"),
            ("FlightSimulator", "Microsoft Flight Simulator 2020"),
        };

        public static List<string> Running()
        {
            var running = new List<string>();
            foreach ((string process, string name) in Known)
            {
                Process[] found;
                try
                {
                    found = Process.GetProcessesByName(process);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (found.Length > 0)
                {
                    running.Add(name);
                }

                foreach (Process p in found)
                {
                    p.Dispose();
                }
            }

            return running.Distinct().ToList();
        }
    }
}
