using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Presentation.Ui1;

/// <summary>
/// Pure presentation-side icon binding request. It carries only immutable identity
/// and content-kind data; the Godot main thread resolves the actual IconDescriptor.
/// </summary>
public sealed record GameContentIconBindingViewModel(
    ModelKey ModelKey,
    GameContentKind ContentKind);

/// <summary>
/// Shared document-driven display contract for relics, cards, potions, and other
/// catalog content. UI code consumes this contract instead of guessing names,
/// content kinds, internal identities, or icon paths.
/// </summary>
public sealed record GameContentDisplayViewModel(
    GameContentIconBindingViewModel IconBinding,
    string DisplayName,
    string Tooltip)
{
    public ModelKey ModelKey => IconBinding.ModelKey;
    public GameContentKind ContentKind => IconBinding.ContentKind;
}

public static class GameContentDisplayPresentationBuilder
{
    public static GameContentDisplayViewModel Build(
        ModelKey key,
        GameContentKind kind,
        IGameContentNameResolver contentNames,
        bool showInternalIds,
        string? tooltipSummary = null,
        IEnumerable<string>? diagnosticLines = null)
    {
        ArgumentNullException.ThrowIfNull(contentNames);

        string localizedName = contentNames.Resolve(key, kind);
        string displayName = showInternalIds
            ? $"{localizedName} [{key.Serialized}]"
            : localizedName;

        string tooltipPrimary = string.IsNullOrWhiteSpace(tooltipSummary)
            ? localizedName
            : tooltipSummary!;
        var tooltipLines = new List<string> { tooltipPrimary };
        if (showInternalIds)
        {
            tooltipLines.Add(key.Serialized);
            if (diagnosticLines is not null)
            {
                tooltipLines.AddRange(diagnosticLines.Where(line => !string.IsNullOrWhiteSpace(line)));
            }
        }

        return new GameContentDisplayViewModel(
            new GameContentIconBindingViewModel(key, kind),
            displayName,
            string.Join("\n", tooltipLines));
    }

    public static GameContentDisplayViewModel BuildRelic(
        ModelKey key,
        IGameContentNameResolver contentNames,
        bool showInternalIds,
        string? tooltipSummary = null,
        IEnumerable<string>? diagnosticLines = null) =>
        Build(key, GameContentKind.Relic, contentNames, showInternalIds, tooltipSummary, diagnosticLines);

    public static GameContentDisplayViewModel BuildCard(
        ModelKey key,
        IGameContentNameResolver contentNames,
        bool showInternalIds,
        string? tooltipSummary = null,
        IEnumerable<string>? diagnosticLines = null) =>
        Build(key, GameContentKind.Card, contentNames, showInternalIds, tooltipSummary, diagnosticLines);
}
