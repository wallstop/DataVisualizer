namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Pure evaluation of a <see cref="TypeLabelFilterConfig"/> against an object's Unity asset
    /// labels. The AND/OR combination rules previously lived in two copies (the live filter loop and
    /// a dead helper) which drifted apart; consolidating them here fixes that and lets the rules be
    /// unit tested without an editor window or real assets.
    ///
    /// Public because the package's editor test assembly cannot see internals of the editor
    /// assembly (InternalsVisibleTo is not honored for that pair in Unity's compilation).
    /// </summary>
    public static class LabelFilterEvaluator
    {
        /// <summary>
        /// Returns whether <paramref name="objectLabels"/> satisfy <paramref name="config"/>. An
        /// empty AND or OR clause is treated as "no constraint". In OR mode an object must satisfy at
        /// least one <em>active</em> clause; an empty clause never counts as a match — otherwise every
        /// object would pass whenever only the other clause was populated (the historical bug where
        /// OR-mode with only OR labels matched everything).
        /// </summary>
        public static bool Matches(IReadOnlyList<string> objectLabels, TypeLabelFilterConfig config)
        {
            if (config == null)
            {
                return true;
            }

            List<string> andLabels = config.andLabels;
            List<string> orLabels = config.orLabels;
            bool hasAnd = andLabels is { Count: > 0 };
            bool hasOr = orLabels is { Count: > 0 };
            if (!hasAnd && !hasOr)
            {
                return true;
            }

            IReadOnlyList<string> present = objectLabels ?? Array.Empty<string>();
            bool andSatisfied = hasAnd && AllPresent(andLabels, present);
            bool orSatisfied = hasOr && AnyPresent(orLabels, present);

            return config.combinationType == LabelCombinationType.Or
                ? andSatisfied || orSatisfied // at least one ACTIVE clause must match
                : (!hasAnd || andSatisfied) && (!hasOr || orSatisfied); // every active clause

            // Local, non-capturing helpers keep this allocation-free: Matches runs once per object
            // during filtering, so a per-call HashSet or closure would add real GC pressure. Asset
            // label sets are tiny, so linear membership over the provided list is cheap.
            static bool AllPresent(List<string> required, IReadOnlyList<string> labels)
            {
                for (int i = 0; i < required.Count; i++)
                {
                    if (!Contains(labels, required[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            static bool AnyPresent(List<string> candidates, IReadOnlyList<string> labels)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (Contains(labels, candidates[i]))
                    {
                        return true;
                    }
                }
                return false;
            }

            static bool Contains(IReadOnlyList<string> labels, string target)
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    if (string.Equals(labels[i], target, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
