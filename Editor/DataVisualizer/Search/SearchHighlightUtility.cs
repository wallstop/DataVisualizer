namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /*
        Collects case-insensitive term matches inside a string and renders them as rich
        text for search-result labels. Kept LINQ-free because the search path builds
        highlight data for every visible result row on each keystroke; value-tuple
        matches avoid the per-match Tuple and enumerator allocations of the previous
        SelectMany/OrderBy pipeline.
    */
    public static class SearchHighlightUtility
    {
        private static readonly StringBuilder CachedStringBuilder = new();

        public static void CollectMatches(
            string fullText,
            IReadOnlyList<string> terms,
            List<(int Start, int Length)> matches
        )
        {
            if (string.IsNullOrEmpty(fullText) || terms == null || 0 == terms.Count)
            {
                return;
            }

            for (int termIndex = 0; termIndex < terms.Count; termIndex++)
            {
                string term = terms[termIndex];
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                int start = 0;
                while (
                    0 <= (start = fullText.IndexOf(term, start, StringComparison.OrdinalIgnoreCase))
                )
                {
                    matches.Add((start, term.Length));
                    start += term.Length;
                }
            }

            /*
                Stable insertion sort by start index. A stable order keeps terms that match
                at the same index in input order, so the first span wins when overlapping
                matches are skipped during rendering.
            */
            for (int i = 1; i < matches.Count; i++)
            {
                (int Start, int Length) current = matches[i];
                int j = i - 1;
                while (0 <= j && current.Start < matches[j].Start)
                {
                    matches[j + 1] = matches[j];
                    j--;
                }

                matches[j + 1] = current;
            }
        }

        public static string BuildHighlightedRichText(
            string fullText,
            IReadOnlyList<(int Start, int Length)> matches,
            bool colorify
        )
        {
            if (string.IsNullOrWhiteSpace(fullText))
            {
                return string.Empty;
            }

            CachedStringBuilder.Clear();
            int currentIndex = 0;
            if (matches != null)
            {
                foreach ((int startIndex, int length) in matches)
                {
                    if (startIndex < currentIndex)
                    {
                        continue;
                    }

                    AppendEscaped(fullText, currentIndex, startIndex - currentIndex);
                    if (colorify)
                    {
                        CachedStringBuilder.Append("<color=yellow>");
                    }

                    CachedStringBuilder.Append("<b>");
                    AppendEscaped(fullText, startIndex, length);
                    CachedStringBuilder.Append("</b>");
                    if (colorify)
                    {
                        CachedStringBuilder.Append("</color>");
                    }

                    currentIndex = startIndex + length;
                }
            }

            if (currentIndex < fullText.Length)
            {
                AppendEscaped(fullText, currentIndex, fullText.Length - currentIndex);
            }

            return CachedStringBuilder.ToString();
        }

        /*
            Appends fullText[start, start + length) with < and > escaped, writing straight
            into the shared builder so segments never allocate substrings or intermediate
            strings. Whitespace-only segments append nothing, matching the previous
            EscapeRichText behavior (see issue #78 for the pinned whitespace-gap drop).
        */
        private static void AppendEscaped(string fullText, int start, int length)
        {
            int end = start + length;
            bool hasContent = false;
            for (int i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(fullText[i]))
                {
                    hasContent = true;
                    break;
                }
            }

            if (!hasContent)
            {
                return;
            }

            for (int i = start; i < end; i++)
            {
                char current = fullText[i];
                if (current == '<')
                {
                    CachedStringBuilder.Append("&lt;");
                }
                else if (current == '>')
                {
                    CachedStringBuilder.Append("&gt;");
                }
                else
                {
                    CachedStringBuilder.Append(current);
                }
            }
        }
    }
}
