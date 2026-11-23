using System;
using System.Collections.Generic;

namespace Masonry
{
    /// <summary>
    /// Build flags: plain words a game passes to a build ("exit-gate", "night", "floor-3"...) that Piece Set entries
    /// can require with their condition. Conditions are comma separated; each part must hold; "!word" means the flag
    /// must not be set. Matching ignores case.
    /// </summary>
    public static class Flags
    {
        /// <summary>Set on the level a build starts from.</summary>
        public const string Main = "main";
        /// <summary>Set on levels reached through a <see cref="LevelLink"/>.</summary>
        public const string Linked = "linked";
        /// <summary>Set on levels that may still link to a level below (see <see cref="BuildOptions.maxLinkDepth"/>). Give link pieces this condition so they only appear when there is somewhere to go.</summary>
        public const string Links = "links";

        public static bool Match(string condition, ICollection<string> flags)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return true;
            foreach (var raw in condition.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                    continue;
                bool negate = part[0] == '!';
                var word = negate ? part.Substring(1).Trim() : part;
                bool set = flags != null && Contains(flags, word);
                if (set == negate)
                    return false;
            }
            return true;
        }

        /// <summary>A new flag set holding the given flags and one more.</summary>
        public static HashSet<string> With(IEnumerable<string> flags, string extra)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (flags != null)
                foreach (var f in flags)
                    if (!string.IsNullOrWhiteSpace(f))
                        set.Add(f.Trim());
            if (!string.IsNullOrWhiteSpace(extra))
                set.Add(extra);
            return set;
        }

        /// <summary>Parses "a, b, c" into flags.</summary>
        public static HashSet<string> Parse(string text) => With(text?.Split(','), null);

        static bool Contains(ICollection<string> flags, string word)
        {
            if (flags is HashSet<string> hash && hash.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
                return hash.Contains(word);
            foreach (var f in flags)
                if (string.Equals(f, word, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
