using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Orders a batch of renames so that it can be carried out one setter call at a time, even
    /// when the new names are currently taken by other objects of the same batch (a swap, a
    /// rotation, a chain).
    ///
    /// Callers: Portal.AnetFork.cs (RenameTags, RenameObjects) and Test9SimaticMl. Fork addition.
    /// Pure - it sees names only, which is what lets it be tested without TIA Portal.
    /// </summary>
    public static class BatchRename
    {
        /// <summary>
        /// The setter calls to make, in order: (index into the batch, name to set). An object
        /// whose new name is still held by another object of the batch is first parked under a
        /// temporary name; the parked ones get their final name last, when every name they want
        /// has been vacated.
        /// </summary>
        /// <param name="allNames">Every name of that namespace in the PLC, renamed objects included.</param>
        public static List<(int Index, string Name)> Plan(IList<string> oldNames, IList<string> newNames, IEnumerable<string> allNames)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var moving = new HashSet<string>(oldNames, comparer);
            var twice = oldNames.GroupBy(n => n, comparer).Concat(newNames.GroupBy(n => n, comparer)).FirstOrDefault(g => g.Count() > 1);

            if (twice != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{twice.Key}' appears twice in the batch.");
            }

            var taken = new HashSet<string>(allNames.Where(n => !moving.Contains(n)), comparer);
            var collision = newNames.FirstOrDefault(taken.Contains);

            if (collision != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"The name '{collision}' is already used by an object that is not part of this batch.");
            }

            var steps = new List<(int Index, string Name)>();
            var parked = new List<int>();
            var stamp = Guid.NewGuid().ToString("N").Substring(0, 8);

            for (var i = 0; i < oldNames.Count; i++)
            {
                // A change of casing only is no conflict with itself.
                if (moving.Contains(newNames[i]) && !comparer.Equals(oldNames[i], newNames[i]))
                {
                    steps.Add((i, $"zzTmp_{stamp}_{i}"));
                    parked.Add(i);
                }
            }

            steps.AddRange(Enumerable.Range(0, oldNames.Count)
                .Where(i => !parked.Contains(i) && oldNames[i] != newNames[i])
                .Select(i => (i, newNames[i])));
            steps.AddRange(parked.Select(i => (i, newNames[i])));

            return steps;
        }
    }
}
