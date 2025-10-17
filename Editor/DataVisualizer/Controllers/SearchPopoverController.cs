namespace WallstopStudios.DataVisualizer.Editor.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Search;
    using Services;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class SearchPopoverController
    {
        private const float HighConfidenceThreshold = 0.85f;
        private const float MediumConfidenceThreshold = 0.6f;

        private readonly DataVisualizer _dataVisualizer;
        private readonly SearchService _searchService;

        private TextField _searchField;
        private VisualElement _searchPopover;
        private ScrollView _scrollView;
        private VisualElement _listContainer;
        private readonly List<VisualElement> _resultItems = new List<VisualElement>();
        private int _highlightIndex = -1;
        private string _lastSearchText;
        private bool _cachePopulated;
        private VisualElement _optionsContainer;
        private Toggle _fuzzyToggle;
        private Slider _fuzzyThresholdSlider;
        private Label _fuzzyThresholdValueLabel;
        private Toggle _showScoresToggle;
        private Label _legendLabel;

        public SearchPopoverController(DataVisualizer dataVisualizer, SearchService searchService)
        {
            _dataVisualizer =
                dataVisualizer ?? throw new ArgumentNullException(nameof(dataVisualizer));
            _searchService =
                searchService ?? throw new ArgumentNullException(nameof(searchService));
        }

        public void Attach(TextField searchField, VisualElement searchPopover)
        {
            _searchField = searchField ?? throw new ArgumentNullException(nameof(searchField));
            _searchPopover =
                searchPopover ?? throw new ArgumentNullException(nameof(searchPopover));
            EnsureContainers();
        }

        public void MarkCachePopulated(bool populated)
        {
            _cachePopulated = populated;
            if (!populated)
            {
                _lastSearchText = null;
                ClearResults();
            }
        }

        public void HandleSearchValueChanged(string newValue)
        {
            if (_searchField == null || _searchPopover == null)
            {
                return;
            }

            string trimmed = newValue?.Trim();
            if (string.Equals(trimmed, _lastSearchText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastSearchText = trimmed;
            _highlightIndex = -1;
            ClearResults();

            if (!_cachePopulated || string.IsNullOrWhiteSpace(trimmed))
            {
                _dataVisualizer.CloseActivePopover();
                return;
            }

            string[] terms = trimmed
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (terms.Length == 0)
            {
                _dataVisualizer.CloseActivePopover();
                return;
            }

            List<SearchService.SearchMatch> results = _searchService.Search(
                terms,
                DataVisualizer.MaxSearchResults
            );
            EnsureContainers();
            _listContainer.Clear();

            if (results.Count == 0)
            {
                _searchPopover.style.maxHeight = StyleKeyword.Null;
                _listContainer.Add(
                    new Label("No matching objects found.")
                    {
                        style =
                        {
                            color = Color.grey,
                            paddingBottom = 10,
                            paddingTop = 10,
                            paddingLeft = 10,
                            paddingRight = 10,
                            unityTextAlign = TextAnchor.MiddleCenter,
                        },
                    }
                );
                _dataVisualizer.OpenPopover(_searchPopover, _searchField, shouldFocus: false);
                return;
            }

            _searchPopover.style.maxHeight = StyleKeyword.Null;
            for (int index = 0; index < results.Count; index++)
            {
                SearchService.SearchMatch result = results[index];
                DataAssetMetadata metadata = result.Metadata;
                SearchResultMatchInfo matchInfo = result.MatchInfo;
                List<string> highlightTerms = matchInfo
                    .AllMatchedTerms.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                VisualElement resultItem = new VisualElement
                {
                    name = "result-item",
                    userData = metadata,
                };
                resultItem.AddToClassList(DataVisualizer.SearchResultItemClass);
                resultItem.AddToClassList(Styles.StyleConstants.ClickableClass);
                resultItem.style.flexDirection = FlexDirection.Column;
                resultItem.style.paddingBottom = 4;
                resultItem.style.paddingLeft = 4;
                resultItem.style.paddingRight = 4;
                resultItem.style.paddingTop = 4;

                resultItem.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || resultItem.userData is not DataAssetMetadata clicked)
                    {
                        return;
                    }

                    ScriptableObject asset = clicked.LoadAsset();
                    if (asset == null)
                    {
                        return;
                    }

                    _dataVisualizer.NavigateToObject(asset);
                    _searchField.value = string.Empty;
                    evt.StopPropagation();
                });

                VisualElement row = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        justifyContent = Justify.SpaceBetween,
                    },
                };
                row.AddToClassList(Styles.StyleConstants.ClickableClass);

                Label nameLabel = _dataVisualizer.CreateHighlightedLabel(
                    metadata.DisplayName,
                    highlightTerms,
                    "result-name-label",
                    bindToContextHovers: true,
                    resultItem,
                    row
                );
                nameLabel.AddToClassList("search-result-name-label");
                nameLabel.AddToClassList(Styles.StyleConstants.ClickableClass);
                row.Add(nameLabel);

                Label typeLabel = _dataVisualizer.CreateHighlightedLabel(
                    metadata.AssetType?.Name,
                    highlightTerms,
                    "result-type-label",
                    bindToContextHovers: true,
                    resultItem,
                    row
                );
                typeLabel.AddToClassList("search-result-type-label");
                typeLabel.AddToClassList(Styles.StyleConstants.ClickableClass);
                row.Add(typeLabel);

                if (_dataVisualizer.ShouldShowSearchScores())
                {
                    Label confidenceBadge = CreateConfidenceBadge(result.MatchInfo.highestScore);
                    row.Add(confidenceBadge);
                }

                resultItem.Add(row);

                if (!matchInfo.MatchInPrimaryField)
                {
                    VisualElement contextContainer = new VisualElement
                    {
                        style = { marginTop = 2 },
                    };

                    IEnumerable<IGrouping<string, MatchDetail>> groups = matchInfo
                        .matchedFields.Where(mf =>
                            !string.Equals(
                                mf.fieldName,
                                MatchSource.ObjectName,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && !string.Equals(
                                mf.fieldName,
                                MatchSource.TypeName,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && !string.Equals(
                                mf.fieldName,
                                MatchSource.Guid,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .GroupBy(mf => mf.fieldName)
                        .Take(2);

                    foreach (IGrouping<string, MatchDetail> grouping in groups)
                    {
                        string fieldName = grouping.Key;
                        string fieldValue = grouping.First().matchedValue;
                        Label contextLabel = _dataVisualizer.CreateHighlightedLabel(
                            $"{fieldName}: {fieldValue}",
                            highlightTerms,
                            "search-result-context-label",
                            bindToContextHovers: true,
                            resultItem,
                            row
                        );
                        contextContainer.Add(contextLabel);
                    }

                    if (contextContainer.childCount > 0)
                    {
                        resultItem.Add(contextContainer);
                    }
                }

                _listContainer.Add(resultItem);
                _resultItems.Add(resultItem);
            }

            if (_dataVisualizer.ShouldShowShortcutHints())
            {
                Label hintLabel = new("↑/↓ navigate · Enter select · Esc close")
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Italic,
                        color = new Color(0.6f, 0.6f, 0.6f),
                        marginTop = 4,
                        marginBottom = 2,
                        marginLeft = 6,
                        marginRight = 6,
                    },
                };
                hintLabel.AddToClassList("search-shortcut-hint");
                _listContainer.Add(hintLabel);
            }

            _dataVisualizer.OpenPopover(_searchPopover, _searchField, shouldFocus: false);
            UpdateHighlight();
        }

        public void HandleKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _searchPopover == null)
            {
                return;
            }

            if (_listContainer == null || _resultItems.Count == 0)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                {
                    _highlightIndex++;
                    if (_highlightIndex >= _resultItems.Count)
                    {
                        _highlightIndex = 0;
                    }

                    UpdateHighlight();
                    evt.StopPropagation();
                    evt.PreventDefault();
                    break;
                }
                case KeyCode.UpArrow:
                {
                    _highlightIndex--;
                    if (_highlightIndex < 0)
                    {
                        _highlightIndex = _resultItems.Count - 1;
                    }

                    UpdateHighlight();
                    evt.StopPropagation();
                    evt.PreventDefault();
                    break;
                }
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    if (_highlightIndex >= 0 && _highlightIndex < _resultItems.Count)
                    {
                        if (_resultItems[_highlightIndex].userData is DataAssetMetadata metadata)
                        {
                            ScriptableObject asset = metadata.LoadAsset();
                            if (asset != null)
                            {
                                _dataVisualizer.NavigateToObject(asset);
                                _searchField.value = string.Empty;
                                evt.StopPropagation();
                                evt.PreventDefault();
                            }
                        }
                    }
                    break;
                }
                case KeyCode.Escape:
                {
                    _dataVisualizer.CloseActivePopover();
                    evt.StopPropagation();
                    evt.PreventDefault();
                    break;
                }
            }
        }

        public void ClearResults()
        {
            _highlightIndex = -1;
            if (_listContainer != null)
            {
                _listContainer.Clear();
            }

            _resultItems.Clear();
        }

        public bool HasContent()
        {
            return _listContainer != null && _listContainer.childCount > 0;
        }

        private void EnsureContainers()
        {
            if (_searchPopover == null)
            {
                return;
            }

            EnsureOptionControls();
            SyncOptionState();

            _scrollView ??= new ScrollView(ScrollViewMode.Vertical)
            {
                name = "search-scroll",
                style = { flexGrow = 1 },
            };

            _listContainer ??= new VisualElement { name = "search-list-content" };

            if (_scrollView.parent != _searchPopover)
            {
                _searchPopover.Add(_scrollView);
            }

            if (_listContainer.parent != _scrollView)
            {
                _scrollView.Add(_listContainer);
            }
        }

        private void EnsureOptionControls()
        {
            if (_searchPopover == null)
            {
                return;
            }

            if (_optionsContainer == null)
            {
                _optionsContainer = new VisualElement { name = "search-options-container" };
                _optionsContainer.style.flexDirection = FlexDirection.Column;
                _optionsContainer.style.marginBottom = 6;
                _optionsContainer.style.marginLeft = 4;
                _optionsContainer.style.marginRight = 4;

                VisualElement fuzzyRow = new VisualElement { name = "search-fuzzy-row" };
                fuzzyRow.style.flexDirection = FlexDirection.Row;
                fuzzyRow.style.alignItems = Align.Center;

                _fuzzyToggle = new Toggle("Fuzzy Matching")
                {
                    tooltip = "Allow approximate matches when search terms are close.",
                    name = "search-fuzzy-toggle",
                };
                _fuzzyToggle.AddToClassList("search-options-toggle");
                _fuzzyToggle.RegisterValueChangedCallback(evt =>
                    HandleFuzzyToggleChanged(evt.newValue)
                );
                fuzzyRow.Add(_fuzzyToggle);

                _fuzzyThresholdValueLabel = new Label(string.Empty)
                {
                    name = "search-fuzzy-threshold-label",
                };
                _fuzzyThresholdValueLabel.style.marginLeft = 6;
                _fuzzyThresholdValueLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                fuzzyRow.Add(_fuzzyThresholdValueLabel);

                _optionsContainer.Add(fuzzyRow);

                _fuzzyThresholdSlider = new Slider(0.3f, 1f)
                {
                    name = "search-fuzzy-threshold-slider",
                };
                _fuzzyThresholdSlider.style.marginLeft = 18;
                _fuzzyThresholdSlider.style.marginRight = 6;
                _fuzzyThresholdSlider.style.marginTop = 2;
                _fuzzyThresholdSlider.AddToClassList("search-options-slider");
                _fuzzyThresholdSlider.RegisterValueChangedCallback(evt =>
                    HandleFuzzyThresholdChanged(evt.newValue)
                );
                _optionsContainer.Add(_fuzzyThresholdSlider);

                _showScoresToggle = new Toggle("Confidence Badges")
                {
                    tooltip = "Display match confidence badges in the result list.",
                    name = "search-show-scores-toggle",
                };
                _showScoresToggle.style.marginTop = 4;
                _showScoresToggle.AddToClassList("search-options-toggle");
                _showScoresToggle.RegisterValueChangedCallback(evt =>
                    HandleShowScoresToggleChanged(evt.newValue)
                );
                _optionsContainer.Add(_showScoresToggle);

                _legendLabel = new Label("High ≥ 85% • Medium ≥ 60% • Low < 60%")
                {
                    name = "search-confidence-legend",
                };
                _legendLabel.style.marginLeft = 18;
                _legendLabel.style.marginTop = 2;
                _legendLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                _legendLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                _legendLabel.AddToClassList("search-confidence-legend");
                _optionsContainer.Add(_legendLabel);
            }

            if (_optionsContainer.parent != _searchPopover)
            {
                _searchPopover.Insert(0, _optionsContainer);
            }
        }

        private void SyncOptionState()
        {
            if (_optionsContainer == null)
            {
                return;
            }

            bool fuzzyEnabled = _dataVisualizer.ShouldUseFuzzySearch();
            float threshold = _dataVisualizer.GetFuzzyMatchThreshold();
            bool showScores = _dataVisualizer.ShouldShowSearchScores();

            if (_fuzzyToggle != null)
            {
                _fuzzyToggle.SetValueWithoutNotify(fuzzyEnabled);
            }

            if (_fuzzyThresholdSlider != null)
            {
                _fuzzyThresholdSlider.SetValueWithoutNotify(threshold);
                _fuzzyThresholdSlider.SetEnabled(fuzzyEnabled);
            }

            UpdateThresholdValueLabel(threshold);

            if (_showScoresToggle != null)
            {
                _showScoresToggle.SetValueWithoutNotify(showScores);
            }

            if (_legendLabel != null)
            {
                _legendLabel.style.display = showScores ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateThresholdValueLabel(float threshold)
        {
            if (_fuzzyThresholdValueLabel == null)
            {
                return;
            }

            int percent = Mathf.RoundToInt(Mathf.Clamp01(threshold) * 100f);
            _fuzzyThresholdValueLabel.text = $"Threshold {percent}%";
        }

        private void HandleFuzzyToggleChanged(bool newValue)
        {
            bool currentValue = _dataVisualizer.ShouldUseFuzzySearch();
            if (_fuzzyThresholdSlider != null)
            {
                _fuzzyThresholdSlider.SetEnabled(newValue);
            }

            if (newValue == currentValue)
            {
                return;
            }

            _dataVisualizer.PersistSettings(
                settings =>
                {
                    if (settings.enableFuzzySearch == newValue)
                    {
                        return false;
                    }

                    settings.enableFuzzySearch = newValue;
                    return true;
                },
                userState =>
                {
                    if (userState.enableFuzzySearch == newValue)
                    {
                        return false;
                    }

                    userState.enableFuzzySearch = newValue;
                    return true;
                }
            );

            _dataVisualizer.UpdateSearchOptionsFromSettings();
            RefreshActiveResults();
            SyncOptionState();
        }

        private void HandleFuzzyThresholdChanged(float newValue)
        {
            float clampedValue = Mathf.Clamp(newValue, 0.3f, 1f);
            float currentValue = _dataVisualizer.GetFuzzyMatchThreshold();
            UpdateThresholdValueLabel(clampedValue);

            if (Mathf.Approximately(currentValue, clampedValue))
            {
                return;
            }

            _dataVisualizer.PersistSettings(
                settings =>
                {
                    if (Mathf.Approximately(settings.fuzzyMatchThreshold, clampedValue))
                    {
                        return false;
                    }

                    settings.fuzzyMatchThreshold = clampedValue;
                    return true;
                },
                userState =>
                {
                    if (Mathf.Approximately(userState.fuzzyMatchThreshold, clampedValue))
                    {
                        return false;
                    }

                    userState.fuzzyMatchThreshold = clampedValue;
                    return true;
                }
            );

            _dataVisualizer.UpdateSearchOptionsFromSettings();
            RefreshActiveResults();
            SyncOptionState();
        }

        private void HandleShowScoresToggleChanged(bool newValue)
        {
            bool currentValue = _dataVisualizer.ShouldShowSearchScores();
            if (newValue == currentValue)
            {
                if (_legendLabel != null)
                {
                    _legendLabel.style.display = newValue ? DisplayStyle.Flex : DisplayStyle.None;
                }

                return;
            }

            _dataVisualizer.PersistSettings(
                settings =>
                {
                    if (settings.showSearchScores == newValue)
                    {
                        return false;
                    }

                    settings.showSearchScores = newValue;
                    return true;
                },
                userState =>
                {
                    if (userState.showSearchScores == newValue)
                    {
                        return false;
                    }

                    userState.showSearchScores = newValue;
                    return true;
                }
            );

            if (_legendLabel != null)
            {
                _legendLabel.style.display = newValue ? DisplayStyle.Flex : DisplayStyle.None;
            }

            RefreshActiveResults();
            SyncOptionState();
        }

        private void RefreshActiveResults()
        {
            if (_searchField == null)
            {
                return;
            }

            string currentValue = _searchField.value;
            if (string.IsNullOrWhiteSpace(currentValue))
            {
                return;
            }

            _lastSearchText = null;
            HandleSearchValueChanged(currentValue);
        }

        private Label CreateConfidenceBadge(float score)
        {
            int percent = Mathf.RoundToInt(Mathf.Clamp01(score) * 100f);
            string badgeLevel = GetConfidenceBand(score);
            string badgeText = GetConfidenceText(badgeLevel, percent);

            Label badge = new Label(badgeText)
            {
                name = "search-confidence-badge",
                tooltip = $"Match confidence {percent}%",
            };
            badge.AddToClassList("search-result-confidence-badge");
            badge.AddToClassList($"search-result-confidence-badge--{badgeLevel}");
            return badge;
        }

        private static string GetConfidenceBand(float score)
        {
            if (score >= HighConfidenceThreshold)
            {
                return "high";
            }

            if (score >= MediumConfidenceThreshold)
            {
                return "medium";
            }

            return "low";
        }

        private static string GetConfidenceText(string band, int percent)
        {
            switch (band)
            {
                case "high":
                {
                    return $"High {percent}%";
                }
                case "medium":
                {
                    return $"Medium {percent}%";
                }
                default:
                {
                    return $"Low {percent}%";
                }
            }
        }

        private void UpdateHighlight()
        {
            if (_resultItems.Count == 0 || _scrollView == null)
            {
                return;
            }

            for (int index = 0; index < _resultItems.Count; index++)
            {
                VisualElement item = _resultItems[index];
                if (item == null)
                {
                    continue;
                }

                if (index == _highlightIndex)
                {
                    item.AddToClassList(DataVisualizer.SearchResultHighlightClass);
                    _scrollView.ScrollTo(item);
                }
                else
                {
                    item.RemoveFromClassList(DataVisualizer.SearchResultHighlightClass);
                }
            }
        }
    }
}
