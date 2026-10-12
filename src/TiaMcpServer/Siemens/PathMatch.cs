using System;
using System.Collections.Generic;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Path lookup for objects whose name may itself contain '/', which TIA Portal allows for tag
    /// tables. Splitting such a path at '/' cannot work, so the tree is walked and whole names
    /// are compared against the start of the path instead.
    ///
    /// Callers: Portal.Tags.cs (GetTagTable, GetTag) and Test9SimaticMl. Fork addition. Generic
    /// over the group and item types so it needs no Openness object and can be tested without
    /// TIA Portal.
    /// </summary>
    public static class PathMatch
    {
        /// <summary>
        /// True when <paramref name="path"/> is <paramref name="name"/> or starts with it as a
        /// whole segment; <paramref name="rest"/> is what follows the separator.
        /// </summary>
        public static bool HasPrefix(string path, string name, out string rest)
        {
            rest = string.Empty;

            if (path.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (name.Length > 0 && path.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase))
            {
                rest = path.Substring(name.Length + 1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Every item whose path below <paramref name="group"/> is a prefix of
        /// <paramref name="path"/>, with the remainder of the path (empty when the path names
        /// the item itself).
        /// </summary>
        public static IEnumerable<(TItem Item, string Tail)> ByPrefix<TGroup, TItem>(
            TGroup group,
            string path,
            Func<TGroup, IEnumerable<TItem>> items,
            Func<TGroup, IEnumerable<TGroup>> groups,
            Func<TItem, string> itemName,
            Func<TGroup, string> groupName)
        {
            foreach (var item in items(group))
            {
                if (HasPrefix(path, itemName(item), out var rest))
                {
                    yield return (item, rest);
                }
            }

            foreach (var subgroup in groups(group))
            {
                if (HasPrefix(path, groupName(subgroup), out var rest) && rest.Length > 0)
                {
                    foreach (var match in ByPrefix(subgroup, rest, items, groups, itemName, groupName))
                    {
                        yield return match;
                    }
                }
            }
        }
    }
}
