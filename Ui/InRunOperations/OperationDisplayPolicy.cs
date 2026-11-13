using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.InRunOperations;

// Owner's 2026-10-16 A/B/C/D + 1–5 display vocabulary. The default
// remains A1; layouts describe the relationship, never a card-count cutoff.
internal static class OperationDisplayPolicy
{
    internal static readonly OperationDisplayStyle A1 = new(OperationTextMode.GroupRows, OperationCardLayout.Parallel);
    private static readonly OperationDisplayStyle D1 = new(OperationTextMode.None, OperationCardLayout.Parallel);
    private static readonly OperationDisplayStyle D2 = new(OperationTextMode.None, OperationCardLayout.Stack);
    private static readonly OperationDisplayStyle D3 = new(OperationTextMode.None, OperationCardLayout.Groups);
    private static readonly OperationDisplayStyle D5 = new(OperationTextMode.None, OperationCardLayout.Spread);

    internal static OperationDisplayStyle Resolve(OperationPreview preview) =>
        preview.Style ?? ForSource(preview.Source, preview.OptionKey);

    internal static OperationDisplayStyle Resolve(OperationPreview preview, OperationPreviewSection section)
    {
        if (TransformOnly(preview))
            return (section.Style ?? Resolve(preview)) with { Text = OperationTextMode.None };
        if (section.Style is { } authored) return authored;
        var root = Resolve(preview);
        if (IsEventCombat(section) &&
            preview.Branches.SelectMany(branch => branch.Sections).Sum(part => part.CardGroups.Length) > 1)
            return new(root.Text is OperationTextMode.Complex or OperationTextMode.CardRows ? root.Text : OperationTextMode.GroupRows, OperationCardLayout.Groups);
        // A complex comparison needs its entire chain in the same readable
        // account, including effects whose ordinary standalone policy is D.
        if (root.Text == OperationTextMode.Complex) return root;
        if (section.Source == preview.Source)
        {
            if (preview.Style != null) return root;
            if (section.Source.Entry == "BATTLEWORN_DUMMY" &&
                section.Rows.Any(r => r.Action == OperationPreviewAction.UpgradeCard)) return D1;
            return root;
        }
        return ForSource(section.Source, preview.OptionKey);
    }

    internal static bool IsRewardReroll(OperationPreview preview, OperationPreviewSection section) =>
        section.Category == RolltheSpire2.Ui.Settings.OperationPredictionCategory.RewardReroll ||
        section.Category == null && preview.OptionKey == "REROLL";

    internal static bool HasRerollCardGroups(OperationPreview preview) => preview.Branches.Any(branch =>
        branch.Sections.Any(section => IsRewardReroll(preview, section) && section.CardGroups.Any(group => !group.Cards.IsDefaultOrEmpty)));

    // Reading order already conveys an unambiguous ordinal. Paths only help
    // distinguish different complete card faces at the same ordinal; an icon's
    // recipe class (including Candy/White Star) does not by itself add meaning.
    // Keep the original paths on the detached preview for their numerical and
    // provenance consumers; this decision only controls visible presentation.
    internal static bool ShowIconPaths(OperationPreview preview, OperationPreviewSection section, OperationPreviewCardGroup group)
    {
        if (group.IconPaths.IsDefaultOrEmpty) return false;
        if (!IsRewardReroll(preview, section)) return true;
        return group.SequenceOrdinal.HasValue && section.CardGroups.Any(other =>
            other.SequenceOrdinal == group.SequenceOrdinal && !other.Cards.IsDefaultOrEmpty &&
            (other.Cards.Length != group.Cards.Length || other.Cards.Zip(group.Cards).Any(pair =>
                (pair.First with { Id = 0, FloorAdded = 0 }) != (pair.Second with { Id = 0, FloorAdded = 0 }))));
    }

    internal static bool IsEventCombat(OperationPreviewSection section) =>
        section.Label is "EventCombatRewards" or "EventCombatNoHunt" ||
        section.Label?.StartsWith("EventHuntTriggers:", StringComparison.Ordinal) == true;

    internal static bool CompactEventCardNames(OperationPreview preview, OperationPreviewSection section,
        bool cardFacesVisible, bool mapPresentation) => cardFacesVisible && !mapPresentation && IsEventCombat(section) &&
        Resolve(preview, section).Text is OperationTextMode.GroupRows or OperationTextMode.None;

    internal static OperationDisplayStyle ForSource(ModelKey source, string? option = null) => DeclaredSourceStyle(source, option) ?? A1;
    internal static bool HasAuthoredSourceStyle(ModelKey source) => source.Entry == "BATTLEWORN_DUMMY" || DeclaredSourceStyle(source, null) != null;

    private static OperationDisplayStyle? DeclaredSourceStyle(ModelKey source, string? option) => source.Entry switch
    {
        "NEOWS_BONES" => new(OperationTextMode.Complex, OperationCardLayout.Branches),
        "NEW_LEAF" => D2,
        "ASTROLABE" or "KALEIDOSCOPE" or "SCROLL_BOXES" or "GLASS_EYE" or "SEA_GLASS" or
        "MORPHIC_GROVE" or "AROMA_OF_CHAOS" => D3,
        "PANDORAS_BOX" or "ROOM_FULL_OF_CHEESE" => D5,
        "COLORFUL_PHILOSOPHERS" or "ORRERY" => new(OperationTextMode.GroupRows, OperationCardLayout.Groups),
        "TOY_BOX" or "ALCHEMICAL_COFFER" => new(OperationTextMode.CardRows, OperationCardLayout.Parallel),
        "ENTROPIC_BREW" => A1,
        "TRIAL" => option is "NONDESCRIPT_INNOCENT" or "NONDESCRIPT_GUILTY" ? D3 : A1,
        "WHISPERING_HOLLOW" => option == "HUG" ? D2 : A1,
        "TRASH_HEAP" => option == "GRAB" ? D1 : A1,
        "MUSEUM_OF_TORTURE" => D1,
        "WATERFALL_OF_OBVIOUS_SECRETS" => option == "NEGOTIATE" ? D1 : A1,
        "WUPPY_IN_TROUBLE" => A1,
        "SYMBIOTE" => D2,
        "ENDLESS_CONVEYOR" => option == "GRAB_JELLY_LIVER" ? D2 : A1,
        "ARCANE_SCROLL" or "HEFTY_TABLET" or "LEAD_PAPERWEIGHT" or "MASSIVE_SCROLL" or
        "SILKEN_TRESS" or "DISTINGUISHED_CAPE" or "LEAFY_POULTICE" or "BEAUTIFUL_BRACELET" or
        "SAND_CASTLE" or "FRAGRANT_MUSHROOM" or "WAR_PAINT" or "WHETSTONE" or
        "ATTACK_POTION" or "SKILL_POTION" or "POWER_POTION" or "COLORLESS_POTION" or
        "COSMIC_CONCOCTION" or "OROBIC_ACID" or "BOTTLED_SONG" or
        "BRAIN_LEECH" or "INFESTED_AUTOMATON" or "THE_FUTURE_OF_POTIONS" or "TINKER_TIME" or
        "DOORS_OF_LIGHT_AND_DARK" or "TABLET_OF_TRUTH" or "SLIPPERY_BRIDGE" or
        "DREAM_CATCHER" or "DRIFTWOOD" => D1,
        _ => null
    };

    internal static bool HiddenSource(ModelKey source) => source.Entry is "ROYAL_STAMP" or "PAELS_LEGION";

    internal static bool TransformOnly(OperationPreview preview) => preview.Source.Entry is
        "ASTROLABE" or "NEW_LEAF" or "PANDORAS_BOX" or "MORPHIC_GROVE" or "AROMA_OF_CHAOS" or "SYMBIOTE" ||
        preview.Source.Entry == "WHISPERING_HOLLOW" && preview.OptionKey == "HUG" ||
        preview.Source.Entry == "TRIAL" && preview.OptionKey == "NONDESCRIPT_INNOCENT" ||
        preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
            .Any(row => row.Action == OperationPreviewAction.TransformCard) &&
        preview.ContainedRelics.IsEmpty && preview.Branches.SelectMany(branch => branch.Sections)
            .SelectMany(section => section.Rows).All(row => row.Action == OperationPreviewAction.TransformCard || !row.ShowCards);
}
