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
        public static bool Matches(
            IReadOnlyCollection<string> objectLabels,
            TypeLabelFilterConfig config
        )
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

            HashSet<string> present =
                objectLabels as HashSet<string>
                ?? new HashSet<string>(
                    objectLabels ?? Array.Empty<string>(),
                    StringComparer.Ordinal
                );

            bool andSatisfied = hasAnd && andLabels.TrueForAll(present.Contains);
            bool orSatisfied = hasOr && orLabels.Exists(present.Contains);

            return config.combinationType == LabelCombinationType.Or
                ? andSatisfied || orSatisfied // at least one ACTIVE clause must match
                : (!hasAnd || andSatisfied) && (!hasOr || orSatisfied); // every active clause
        }
    }
}
