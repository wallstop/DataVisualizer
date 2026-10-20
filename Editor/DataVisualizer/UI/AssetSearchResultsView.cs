namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Extensions;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.Styles;

    public sealed class AssetSearchResultsView : VisualElement
    {
        public ListView ResultsList { get; }
        public int ResultCount => _hits.Count;

        private readonly List<(AssetSearchDocument Document, SearchResultMatchInfo Match)> _hits = new();
        private readonly Action<AssetSearchDocument> _navigate;
        private readonly Action _close;
        private readonly Label _status;
        private IReadOnlyList<(AssetSearchDocument Document, SearchResultMatchInfo Match)> _source;
        private string _preferredSelectionGuid;

        public AssetSearchResultsView(Action<AssetSearchDocument> navigate, Action back, Action close)
        {
            _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
            _close = close ?? throw new ArgumentNullException(nameof(close));
            name = "all-search-results";
            style.width = 420;
            style.maxWidth = new Length(100, LengthUnit.Percent);
            style.minWidth = 0;
            style.minHeight = 0;
            style.flexShrink = 1;
            Button backButton = new(back ?? throw new ArgumentNullException(nameof(back)))
            {
                name = "search-back-button",
                text = "Back to quick results",
                tooltip = "Return to the first results without changing your query",
            };
            backButton.AddToClassList(StyleConstants.ClickableClass);
            Add(backButton);
            _status = new Label { name = "search-status", style = { whiteSpace = WhiteSpace.Normal } };
            Add(_status);
            ResultsList = new ListView
            {
                name = "all-search-results-list",
                itemsSource = _hits,
                fixedItemHeight = 48,
                selectionType = SelectionType.Single,
                makeItem = MakeItem,
                bindItem = BindItem,
                style =
                {
                    height = 320,
                    minHeight = 0,
                    flexShrink = 1,
                },
            };
            ResultsList.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
            Add(ResultsList);
        }

        private static Label MakeLabel(string name)
        {
            return new Label
            {
                name = name,
                enableRichText = true,
                style =
                {
                    whiteSpace = WhiteSpace.NoWrap,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                },
            };
        }

        private static string Highlight(string text, IReadOnlyList<string> terms)
        {
            List<(int Start, int Length)> matches = new();
            SearchHighlightUtility.CollectMatches(text, terms, matches);
            return SearchHighlightUtility.BuildHighlightedRichText(text, matches, true);
        }

        public bool SetResults(IReadOnlyList<(AssetSearchDocument Document, SearchResultMatchInfo Match)> hits, string status)
        {
            bool reset = !ReferenceEquals(_source, hits) || hits.Count < _hits.Count;
            int previousCount = _hits.Count;
            int previousSelection = ResultsList.selectedIndex;
            if (0 <= ResultsList.selectedIndex && ResultsList.selectedIndex < _hits.Count)
            {
                _preferredSelectionGuid = _hits[ResultsList.selectedIndex].Document.Guid;
            }
            if (reset)
            {
                _hits.Clear();
                _source = hits;
            }
            for (int index = _hits.Count; index < hits.Count; index++)
            {
                _hits.Add(hits[index]);
            }
            _status.text = status;
            bool changed = reset || previousCount != _hits.Count;
            if (changed)
            {
                ResultsList.RefreshItems();
                if (_preferredSelectionGuid != null)
                {
                    int selectedIndex = -1;
                    for (int index = 0; index < _hits.Count; index++)
                    {
                        if (string.Equals(_hits[index].Document.Guid, _preferredSelectionGuid, StringComparison.Ordinal))
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                    ResultsList.selectedIndex = selectedIndex;
                    if (0 <= selectedIndex && (reset || selectedIndex != previousSelection))
                    {
                        ResultsList.ScrollToItem(selectedIndex);
                    }
                }
            }
            return changed;
        }

        public bool HandleNavigation(KeyCode key)
        {
            if (key == KeyCode.Escape)
            {
                _close();
                return true;
            }
            if (_hits.Count == 0)
            {
                return false;
            }
            int selected = ResultsList.selectedIndex;
            if (key == KeyCode.DownArrow || key == KeyCode.UpArrow)
            {
                selected = key == KeyCode.DownArrow ? (selected + 1) % _hits.Count : (selected <= 0 ? _hits.Count - 1 : selected - 1);
                ResultsList.selectedIndex = selected;
                ResultsList.ScrollToItem(selected);
                return true;
            }
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && 0 <= selected && selected < _hits.Count)
            {
                _navigate(_hits[selected].Document);
                return true;
            }
            return false;
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (HandleNavigation(evt.keyCode))
            {
                evt.PreventDefaultCompat();
                evt.StopPropagation();
            }
        }

        private VisualElement MakeItem()
        {
            VisualElement row = new()
            {
                style =
                {
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 2,
                    paddingBottom = 2,
                },
            };
            row.AddToClassList("search-result-item");
            VisualElement primary = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = 20,
                },
            };
            Label title = MakeLabel("search-result-name");
            title.AddToClassList("search-result-name-label");
            title.style.flexGrow = 1;
            title.style.minWidth = 0;
            title.style.flexShrink = 1;
            Label type = MakeLabel("search-result-type");
            type.AddToClassList("search-result-type-label");
            type.style.maxWidth = 160;
            type.style.marginLeft = 8;
            primary.Add(title);
            primary.Add(type);
            row.Add(primary);
            Label context = MakeLabel("search-result-context");
            context.AddToClassList("search-result-context-label");
            row.Add(context);
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && row.panel != null && row.userData is AssetSearchDocument document)
                {
                    _navigate(document);
                    evt.StopPropagation();
                }
            });
            return row;
        }

        private void BindItem(VisualElement row, int index)
        {
            var hit = _hits[index];
            row.userData = hit.Document;
            List<string> terms = new(hit.Match.AllMatchedTerms);
            row.Q<Label>("search-result-name").text = Highlight(hit.Document.Name, terms);
            row.Q<Label>("search-result-type").text = Highlight(hit.Document.TypeName, terms);
            string context = string.Empty;
            if (!hit.Match.MatchInPrimaryField && 0 < hit.Match.MatchedFields.Count)
            {
                MatchDetail field = hit.Match.MatchedFields[0];
                context = $"{field.fieldName}: {field.matchedValue}";
            }
            row.Q<Label>("search-result-context").text = Highlight(context, terms);
            row.tooltip = $"{hit.Document.Name}\n{hit.Document.TypeFullName}\nGUID: {hit.Document.Guid}\n{context}";
        }
    }
}
