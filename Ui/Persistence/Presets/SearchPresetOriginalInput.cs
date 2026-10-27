using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search;

namespace RolltheSpire2.Ui.Persistence;

// Historical grammar is checked against the complete original input. This boundary
// never constructs a filtered draft or weakens an authored condition. Current content
// membership remains ResolveWorkbench's responsibility after the real migration.
internal static class SearchPresetOriginalInput
{
    // Missing optional draft blocks use DTO defaults. Explicit nulls and truncated
    // constructor-backed blocks cannot safely be projected as empty conditions.
    private static bool HasCompleteShape(SearchDraft draft) =>
        draft.NeowRouteDraft is { RequiredBonesRelics: not null, EffectConditions: not null } neow &&
        neow.EffectConditions.All(c => c is { OutputKeys: not null, KaleidoscopePositionalSlots: not null }) &&
        draft.AncientMatrixDraft is { Rows: not null } ancient &&
        ancient.Rows.All(row => row is { SelectedOptionKeys: not null, SeaGlassTargetKeys: not null }) &&
        draft.BossMapDraft is { Rows: not null } boss &&
        boss.Rows.All(row => row is { AllBossKeys: not null, FirstBossAny: not null, SecondBossAny: not null }) &&
        draft.CombatRewardDraft is { Cards.Slots: not null, Potions.Slots: not null } combat &&
        combat.Potions.Slots.All(slot => slot is not null) &&
        draft.RelicSequenceDraft is not null && draft.RelicSequenceDraft.All(c => CompleteKeys(c?.Keys)) &&
        draft.EventSequenceDraft is not null && draft.EventSequenceDraft.All(c => CompleteKeys(c?.Keys)) &&
        draft.EventResultDraft is not null && draft.EventResultDraft.All(c => c is not null) &&
        draft.MerchantColorlessDraft is not null && draft.MerchantColorlessDraft.All(c => c is not null) &&
        draft.MerchantColorlessSequenceDraft is not null && draft.MerchantColorlessSequenceDraft.All(c => c is { Slots: not null }) &&
        draft.RelicShopSequenceDraft is not null && draft.RelicShopSequenceDraft.All(c => c is { Slots: not null }) &&
        (draft.TransformationAggregate is null || draft.TransformationAggregate.TargetMultiset is not null);

    private static bool CompleteKeys(ModelKeySetFilter? keys) => keys is { Any: not null, All: not null, Ban: not null };

    internal static void Validate(SearchDraft source, ModelKey character)
    {
        if (!HasCompleteShape(source)) throw new SearchPresetMigrationException("PresetLegacyIntentUnresolved", []);
        var unresolved = new List<SearchPresetUnresolvedReference>();
        Key(character, "CHARACTER", "Character");
        if (source.TransformationAggregate is { } aggregate)
            Keys(aggregate.TargetMultiset, "CARD", "TransformationAggregate.Target");
        if (source.NeowRouteDraft.RouteRelicKey is { } route) Key(route, "RELIC", "Neow.RouteRelic");
        Keys(source.NeowRouteDraft.RequiredBonesRelics, "RELIC", "Neow.RequiredBonesRelics");
        for (int i = 0; i < source.NeowRouteDraft.EffectConditions.Count; i++)
        {
            var effect = source.NeowRouteDraft.EffectConditions[i];
            string path = $"Neow.Effect[{i}]";
            string category = effect.OutputKind switch
            { NeowStructuredOutputKind.Relic => "RELIC", NeowStructuredOutputKind.Potion => "POTION", _ => "CARD" };
            Key(effect.SourceRelicKey, "RELIC", path + ".SourceRelic");
            Keys(effect.OutputKeys, category, path + ".Outputs");
            Keys(effect.KaleidoscopePositionalSlots.OfType<ModelKey>(), category, path + ".Slots");
        }
        for (int i = 0; i < source.AncientMatrixDraft.Rows.Count; i++)
        {
            var row = source.AncientMatrixDraft.Rows[i];
            if (!row.IsActive) continue;
            string path = $"Ancient[{i}]";
            Key(row.AncientKey, "EVENT", path + ".Identity", "Ancients");
            Keys(row.SelectedOptionKeys, "RELIC", path + ".Options");
            Keys(row.SeaGlassTargetKeys, "CHARACTER", path + ".SeaGlassTargets");
        }
        for (int i = 0; i < source.BossMapDraft.Rows.Count; i++)
        {
            var row = source.BossMapDraft.Rows[i];
            if (row.HasSelectableVariant && !row.IsVariantActive) continue;
            Keys(row.FirstBossAny, "ENCOUNTER", $"BossMap[{i}].First");
            Keys(row.SecondBossAny, "ENCOUNTER", $"BossMap[{i}].Second");
        }
        for (int i = 0; i < source.RelicSequenceDraft.Count; i++)
        {
            var condition = source.RelicSequenceDraft[i];
            string path = $"RelicSequence[{i}]";
            if (!condition.Keys.IsEmpty && condition.IsEmpty) unresolved.Add(new(path, "", "SequenceRangeInvalid"));
            Set(condition.Keys, "RELIC", path);
        }
        for (int i = 0; i < source.EventSequenceDraft.Count; i++)
        {
            var condition = source.EventSequenceDraft[i];
            string path = $"EventSequence[{i}]";
            if (!condition.Keys.IsEmpty && condition.IsEmpty) unresolved.Add(new(path, "", "SequenceActOrRangeInvalid"));
            Set(condition.Keys, "EVENT", path);
        }
        for (int i = 0; i < source.EventResultDraft.Count; i++)
        {
            var condition = source.EventResultDraft[i];
            string path = $"EventResult[{i}]";
            if (!condition.IsValid) unresolved.Add(new(path, condition.TargetKey.Serialized, "StableIdentityInvalid"));
            string category = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.FakeMerchantOfferedFakeRelic => "RELIC",
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => "CHARACTER",
                EventResultConditionKind.TrialCase => "EVENT",
                _ => "CARD"
            };
            Key(condition.TargetKey, category, path + ".Target");
            if (condition.MorphicGroveSecondCard is { } second) Key(second, "CARD", path + ".SecondCard");
        }
        if (source.MorphicGroveContainsCard is { } card) Key(card, "CARD", "MorphicGrove.ContainsCard");
        if (source.MorphicGroveSecondCard is { } secondCard) Key(secondCard, "CARD", "MorphicGrove.SecondCard");
        for (int i = 0; i < source.MerchantColorlessDraft.Count; i++)
        {
            var condition = source.MerchantColorlessDraft[i];
            string path = $"MerchantColorless[{i}]";
            if (!condition.IsValid) unresolved.Add(new(path, condition.TargetCardKey.Serialized, "StableIdentityInvalid"));
            Key(condition.TargetCardKey, "CARD", path + ".Target");
        }
        for (int i = 0; i < source.MerchantColorlessSequenceDraft.Count; i++)
        {
            var condition = source.MerchantColorlessSequenceDraft[i];
            Keys(condition.Slots.Take(Math.Max(0, condition.Count)).OfType<ModelKey>(), "CARD", $"MerchantColorlessSequence[{i}]");
        }
        for (int i = 0; i < source.RelicShopSequenceDraft.Count; i++)
        {
            var condition = source.RelicShopSequenceDraft[i];
            Keys(condition.Slots.Take(Math.Max(0, condition.Count)).OfType<ModelKey>(), "RELIC", $"RelicShopSequence[{i}]");
        }
        if (source.CombatRewardDraft.Cards.HasAnyValue)
            Keys(source.CombatRewardDraft.Cards.Slots.OfType<ModelKey>(), "CARD", "CombatReward.Cards");
        if (source.CombatRewardDraft.ExcludedCollection is { } excludedCollection)
            Keys(excludedCollection.Cards, "CARD", "CombatReward.ExcludedCollection");
        if (source.CombatRewardDraft.Potions.HasAnyValue)
            Keys(source.CombatRewardDraft.Potions.Slots.Where(slot => slot.Requirement == CombatPotionSlotRequirement.DropSpecific)
                .Select(slot => slot.PotionKey).OfType<ModelKey>(), "POTION", "CombatReward.Potions");

        TextSet(source.NeowAny, source.NeowAll, source.NeowBan, "RELIC", "Legacy.Neow");
        TextSet(source.BonesAny, source.BonesAll, source.BonesBan, "RELIC", "Legacy.Bones");
        TextSet(source.CapsuleRelicAny, source.CapsuleRelicAll, source.CapsuleRelicBan, "RELIC", "Legacy.Capsule");
        Text(source.RequiredFinalCurse, "CARD", "Legacy.FinalCurse", single: true);
        Text(source.BannedFinalCurses, "CARD", "Legacy.BannedCurses");
        Text(source.BonesAcquisitionOrder, "RELIC", "Legacy.BonesOrder", ordered: true);
        Text(source.EffectOutputSource, "RELIC", "Legacy.Effect.Source", single: true);
        TextSet(source.EffectOutputAny, source.EffectOutputAll, source.EffectOutputBan, null, "Legacy.Effect");
        TextSet(source.BossAny, source.BossAll, source.BossBan, "ENCOUNTER", "Legacy.Boss");
        TextSet(source.BossOrdinalAny, source.BossOrdinalAll, source.BossOrdinalBan, "ENCOUNTER", "Legacy.BossOrdinal");
        TextSet(source.AncientAny, source.AncientAll, source.AncientBan, "EVENT", "Legacy.Ancient", "Ancients");
        TextSet(source.AncientOptionAny, source.AncientOptionAll, source.AncientOptionBan, "RELIC", "Legacy.AncientOption");
        TextSet(source.SeaGlassTargetAny, source.SeaGlassTargetAll, source.SeaGlassTargetBan, "CHARACTER", "Legacy.SeaGlass");
        // Typed sequence lists take precedence in the existing compiler. Validate
        // compatibility text only when it is the actual historical input.
        if (source.RelicSequenceDraft.Count == 0) SequenceText(source.RelicSequenceConditions, "RELIC", "Legacy.RelicSequence", 4);
        if (source.EventSequenceDraft.Count == 0) SequenceText(source.EventSequenceConditions, "EVENT", "Legacy.EventSequence", 5);
        if (unresolved.Count > 0) throw new SearchPresetMigrationException("PresetLegacyIntentUnresolved", unresolved);

        void Key(ModelKey key, string? category, string path, string? domain = null)
        {
            if (key.IsValid && (category is null || key.Category == category)) return;
            unresolved.Add(new(path, key.Serialized, category is null ? "StableIdentityInvalid" :
                "StableIdentityInvalidForDomain:" + (domain ?? Domain(category))));
        }
        void Keys(IEnumerable<ModelKey> keys, string category, string path)
        { foreach (var key in keys.Distinct(ModelKeyComparer.Instance)) Key(key, category, path); }
        void Set(ModelKeySetFilter set, string category, string path)
        { Keys(set.Any, category, path + ".Any"); Keys(set.All, category, path + ".All"); Keys(set.Ban, category, path + ".Ban"); }
        void TextSet(string any, string all, string ban, string? category, string path, string? domain = null)
        { Text(any, category, path + ".Any", domain: domain); Text(all, category, path + ".All", domain: domain); Text(ban, category, path + ".Ban", domain: domain); }
        void Text(string text, string? category, string path, bool single = false, bool ordered = false, string? domain = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            string[] tokens = single ? [text.Trim()] : text.Split(ordered ? ['>', ',', '\n', '\r'] : [',', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0) unresolved.Add(new(path, text, "StableIdentityInvalid"));
            foreach (string token in tokens)
                if (ModelKey.TryParseExact(token, out var key) && key.IsValid) Key(key, category, path, domain);
                else unresolved.Add(new(path, token, "StableIdentityInvalid"));
        }
        void SequenceText(string text, string category, string path, int headerLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var (condition, index) in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select((value, i) => (value, i)))
            {
                string keys = string.Join(' ', condition.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(headerLength));
                if (string.IsNullOrWhiteSpace(keys)) unresolved.Add(new($"{path}[{index}]", condition, "StableIdentityInvalid"));
                else Text(keys, category, $"{path}[{index}]");
            }
        }
    }

    private static string Domain(string category) => category switch
    { "CHARACTER" => "Characters", "CARD" => "Cards", "RELIC" => "Relics", "POTION" => "Potions", "ENCOUNTER" => "Bosses", "EVENT" => "Events", _ => category };
}
