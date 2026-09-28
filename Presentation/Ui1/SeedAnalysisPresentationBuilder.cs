using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Presentation.Ui1;

public static class SeedAnalysisPresentationBuilder
{
    public static SeedAnalysisViewModel Build(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(uiText);
        ArgumentNullException.ThrowIfNull(contentNames);

        string characterName = contentNames.Resolve(document.Context.CharacterKey, GameContentKind.Character);
        string unlockSummary = document.Context.SnapshotCompleteness switch
        {
            SnapshotCompleteness.Complete => uiText.Get(Ui1TextKey.UnlockFull),
            SnapshotCompleteness.Partial => uiText.Get(Ui1TextKey.UnlockPartial),
            _ => uiText.Get(Ui1TextKey.UnlockUnknown)
        };

        IReadOnlyList<NeowChoiceViewModel> choices = document.Sections
            .Where(section => section.Kind == PredictionSectionKind.NeowIdentity)
            .SelectMany(section => section.NeowChoices)
            .OrderBy(choice => choice.SlotIndex)
            .Select(choice => BuildChoice(choice, uiText, contentNames, showInternalIds))
            .ToArray();

        NeowChoiceResult[] sourceChoices = document.Sections
            .Where(section => section.Kind == PredictionSectionKind.NeowIdentity)
            .SelectMany(section => section.NeowChoices)
            .ToArray();
        PredictionWarning[] productWarnings = document.Warnings
            .Where(warning => IsDocumentUserProductWarning(warning, sourceChoices))
            .ToArray();
        IReadOnlyList<string> openingWarnings = Array.Empty<string>();
        IReadOnlyList<string> warnings = productWarnings
            .Where(warning => !IsNeowScopedWarningHandledInOpeningOrChoice(warning, sourceChoices))
            .Select(warning => FormatDocumentUserProductWarning(warning, sourceChoices, uiText, contentNames))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Ui1AnalysisState state = document.OverallStatus switch
        {
            SeedPredictionOverallStatus.Completed => Ui1AnalysisState.Success,
            SeedPredictionOverallStatus.CompletedWithWarnings => Ui1AnalysisState.Partial,
            SeedPredictionOverallStatus.Unsupported => Ui1AnalysisState.Unsupported,
            SeedPredictionOverallStatus.InvalidRequest => Ui1AnalysisState.Error,
            _ => Ui1AnalysisState.Unknown
        };

        string contextSummary = uiText.Format(
            Ui1TextKey.AnalysisContextCompact,
            string.IsNullOrWhiteSpace(document.CanonicalSeed) ? document.OriginalSeed : document.CanonicalSeed,
            characterName,
            document.Context.Ascension,
            unlockSummary);

        return new SeedAnalysisViewModel(
            state,
            document.CanonicalSeed,
            characterName,
            document.Context.CharacterKey,
            document.Context.Ascension,
            document.Context.PlayersCount,
            document.Context.PlayerSlotIndex,
            document.ProfileId,
            document.DetectedGameVersion,
            document.Context.RequestId.Serialized,
            unlockSummary,
            contextSummary,
            uiText.Get(UiTextKey.OverallStatus(document.OverallStatus)),
            choices,
            warnings)
        {
            BossDomain = BuildBossDomain(document, uiText, contentNames, showInternalIds),
            EncounterSequences = document.Sections.SelectMany(s => s.EncounterSequences).OrderBy(a => a.Act)
                .Select(a => new ActEncounterSequenceViewModel(a.Act, a.ActKey,
                    a.Normal.Select(e => new EncounterSequenceEntryViewModel(e.Ordinal,
                        GameContentDisplayPresentationBuilder.Build(e.EncounterKey, GameContentKind.Encounter, contentNames, showInternalIds))).ToArray(),
                    a.Elite.Select(e => new EncounterSequenceEntryViewModel(e.Ordinal,
                        GameContentDisplayPresentationBuilder.Build(e.EncounterKey, GameContentKind.Encounter, contentNames, showInternalIds))).ToArray(),
                    a.Precision, a.IssueCode)).ToArray(),
            AncientDomain = BuildAncientDomain(document, uiText, contentNames, showInternalIds),
            EventPoolSequenceDomain = BuildEventPoolSequenceDomain(document, uiText, contentNames, showInternalIds),
            RelicSequenceDomain = BuildRelicSequenceDomain(document, uiText, contentNames, showInternalIds),
            TreasureRoomRelicSequenceDomain = BuildRelicSequenceDomain(document, uiText, contentNames, showInternalIds, treasureRoom: true),
            NormalCombatRewardDomain = BuildNormalCombatRewardDomain(document, uiText, contentNames, showInternalIds),
            OpeningWarnings = openingWarnings
        };
    }

    private static SeedDomainViewModel<BossPredictionViewModel> BuildBossDomain(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        PredictionSection? section = document.Sections.FirstOrDefault(item => item.Kind == PredictionSectionKind.BossIdentity);
        if (section is null)
        {
            return new SeedDomainViewModel<BossPredictionViewModel>(
                SeedDomainEvaluationStatus.Unknown,
                "BossSectionMissing",
                Array.Empty<BossPredictionViewModel>());
        }

        BossPredictionViewModel[] items = section.Bosses
            .OrderBy(item => item.Act)
            .ThenBy(item => item.Ordinal)
            .Select(item => new BossPredictionViewModel(
                item.Act,
                item.Ordinal,
                GameContentDisplayPresentationBuilder.Build(
                    item.BossKey,
                    GameContentKind.Encounter,
                    contentNames,
                    showInternalIds,
                    diagnosticLines: new[]
                    {
                        $"Authority={item.Authority}",
                        $"Completeness={item.Completeness}",
                        $"Rng={item.RngStream}:{item.RngCallCount}",
                        $"AuthorityFingerprint={item.AuthorityFingerprint}",
                        $"RuleFingerprint={item.GenerationRuleFingerprint}",
                        $"Trace={TraceSummary(item.RngTrace)}",
                        item.EvidenceCode.ToString()
                    }),
                item.IdentityPrecision,
                uiText.Get(UiTextKey.Precision(item.IdentityPrecision)),
                item.Authority,
                item.Completeness,
                item.RngStream,
                item.RngCallCount,
                item.EvidenceCode.ToString()))
            .ToArray();
        return new SeedDomainViewModel<BossPredictionViewModel>(section.DomainStatus, section.IssueCode, items);
    }

    private static SeedDomainViewModel<AncientPredictionViewModel> BuildAncientDomain(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        PredictionSection? section = document.Sections.FirstOrDefault(item => item.Kind == PredictionSectionKind.AncientIdentityAndOptions);
        if (section is null)
        {
            return new SeedDomainViewModel<AncientPredictionViewModel>(
                SeedDomainEvaluationStatus.Unknown,
                "AncientSectionMissing",
                Array.Empty<AncientPredictionViewModel>());
        }

        AncientPredictionViewModel[] items = section.Ancients
            .OrderBy(item => item.Act)
            .Select(item => new AncientPredictionViewModel(
                item.Act,
                GameContentDisplayPresentationBuilder.Build(
                    item.AncientKey,
                    GameContentKind.Ancient,
                    contentNames,
                    showInternalIds,
                    diagnosticLines: new[]
                    {
                        $"Authority={item.Authority}",
                        $"Completeness={item.Completeness}",
                        $"IdentityRng={item.IdentityRngStream}:{item.IdentityRngCallCount}",
                        $"OptionRng={item.OptionRngStream}:{item.OptionRngCallCount}",
                        $"AuthorityFingerprint={item.AuthorityFingerprint}",
                        $"RuleFingerprint={item.GenerationRuleFingerprint}",
                        $"IdentityTrace={TraceSummary(item.IdentityRngTrace)}",
                        $"OptionTrace={TraceSummary(item.OptionRngTrace)}",
                        item.IdentityEvidenceCode.ToString(),
                        item.OptionEvidenceCode.ToString()
                    }),
                item.IdentityPrecision,
                uiText.Get(UiTextKey.Precision(item.IdentityPrecision)),
                item.OptionPrecision,
                uiText.Get(UiTextKey.Precision(item.OptionPrecision)),
                item.OptionsEvaluationStatus,
                item.OptionIssueCode,
                item.Authority,
                item.Completeness,
                item.IdentityRngStream,
                item.IdentityRngCallCount,
                item.OptionRngStream,
                item.OptionRngCallCount,
                item.Options.Where(option => option.IsVisible).OrderBy(option => option.Ordinal).Select(option =>
                    BuildAncientOptionViewModel(option, uiText, contentNames, showInternalIds)).ToArray(),
                item.IdentityEvidenceCode.ToString(),
                item.OptionEvidenceCode.ToString()))
            .ToArray();
        return new SeedDomainViewModel<AncientPredictionViewModel>(section.DomainStatus, section.IssueCode, items);
    }

    private static AncientOptionPredictionViewModel BuildAncientOptionViewModel(
        AncientOptionPredictionResult option,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        AncientOptionCharacterTargetProjection? target = option.CharacterTarget;
        GameContentDisplayViewModel? targetDisplay = null;
        string targetName = string.Empty;
        string targetLabel = string.Empty;

        if (target is not null)
        {
            if (target.IsKnown && target.CharacterKey.HasValue)
            {
                targetDisplay = GameContentDisplayPresentationBuilder.Build(
                    target.CharacterKey.Value,
                    GameContentKind.Character,
                    contentNames,
                    showInternalIds,
                    diagnosticLines: new[]
                    {
                        $"CharacterTargetPrecision={target.Precision}",
                        $"CharacterTargetEvidence={target.EvidenceCode}",
                        $"CharacterTargetIssue={target.IssueCode}"
                    });
                targetName = targetDisplay.DisplayName;
            }
            else
            {
                targetName = uiText.Get(UiTextKey.Precision(PredictionPrecision.Unknown));
            }
            targetLabel = uiText.Format(Ui1TextKey.WorldSeaGlassTargetOnly, targetName);
        }

        string[] diagnosticLines =
        {
            option.EvidenceCode.ToString(),
            option.VariantId ?? string.Empty,
            target is null ? string.Empty : $"CharacterTargetKey={target.CharacterKey?.Serialized ?? "unknown"}",
            target is null ? string.Empty : $"CharacterTargetPrecision={target.Precision}",
            target is null ? string.Empty : $"CharacterTargetEvidence={target.EvidenceCode}",
            target is null ? string.Empty : $"CharacterTargetIssue={target.IssueCode}"
        };
        GameContentDisplayViewModel optionDisplay = GameContentDisplayPresentationBuilder.BuildRelic(
            option.OptionKey,
            contentNames,
            showInternalIds,
            diagnosticLines: diagnosticLines);
        if (target is not null)
        {
            // Keep the option identity itself stable for the flat Analysis grid.
            // The target remains a separate typed field and is rendered inline at a
            // lower visual hierarchy instead of being duplicated into the option name.
            optionDisplay = optionDisplay with
            {
                Tooltip = string.Join("\n", new[] { optionDisplay.Tooltip, targetLabel }
                    .Where(line => !string.IsNullOrWhiteSpace(line)))
            };
        }

        return new AncientOptionPredictionViewModel(
            option.Ordinal,
            optionDisplay,
            option.OptionPrecision,
            uiText.Get(UiTextKey.Precision(option.OptionPrecision)),
            option.EvidenceCode.ToString(),
            option.VariantId,
            targetDisplay,
            target?.Precision,
            targetLabel,
            target?.IssueCode ?? string.Empty);
    }


    private static SeedDomainViewModel<EventPoolActSequenceViewModel> BuildEventPoolSequenceDomain(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        PredictionSection? section = document.Sections.FirstOrDefault(item => item.Kind == PredictionSectionKind.EventPoolSequences);
        EventPoolSequencePredictionResult? prediction = section?.EventPoolSequencePrediction;
        if (section is null || prediction is null)
        {
            return new SeedDomainViewModel<EventPoolActSequenceViewModel>(
                SeedDomainEvaluationStatus.Unknown,
                "EventPoolSequenceSectionMissing",
                Array.Empty<EventPoolActSequenceViewModel>());
        }

        EventPoolActSequenceViewModel[] acts = prediction.Acts
            .OrderBy(item => item.Act)
            .Select(item => new EventPoolActSequenceViewModel(
                item.Act,
                item.ActKey,
                uiText.Format(Ui1TextKey.EventSequenceActTitle, item.Act, item.ActKey.Entry),
                item.Entries.OrderBy(entry => entry.Ordinal).Select(entry =>
                    new EventPoolSequenceEntryViewModel(
                        entry.Ordinal,
                        entry.RawOrdinal,
                        entry.SourceOrdinal,
                        GameContentDisplayPresentationBuilder.Build(
                            entry.EventKey,
                            GameContentKind.Event,
                            contentNames,
                            showInternalIds,
                            diagnosticLines: new[]
                            {
                                $"Ordinal={entry.Ordinal}",
                                $"RawOrdinal={entry.RawOrdinal}",
                                $"Source={entry.Source}:{entry.SourceOrdinal}",
                                $"Epochs={string.Join(",", entry.EpochIds)}",
                                entry.EvidenceCode.ToString()
                            }),
                        entry.Source,
                        uiText.Get(entry.Source == EventPoolSourceKind.ActLocal
                            ? Ui1TextKey.EventSequenceSourceActLocal
                            : Ui1TextKey.EventSequenceSourceShared),
                        entry.EpochIds,
                        entry.Precision,
                        uiText.Get(UiTextKey.Precision(entry.Precision)),
                        entry.EvidenceCode.ToString(),
                        entry.EligibilityKind,
                        entry.EligibilityKind == EventCandidateEligibilityKind.RuntimeDependent
                            ? uiText.Get(Ui1TextKey.EventSequenceRuntimeDependent)
                            : string.Empty)).ToArray(),
                item.RawEntries.OrderBy(entry => entry.RawOrdinal).Select(entry =>
                    new EventPoolRawSequenceEntryViewModel(
                        entry.RawOrdinal,
                        entry.SourceOrdinal,
                        GameContentDisplayPresentationBuilder.Build(
                            entry.EventKey,
                            GameContentKind.Event,
                            contentNames,
                            showInternalIds,
                            diagnosticLines: new[]
                            {
                                $"RawOrdinal={entry.RawOrdinal}",
                                $"Source={entry.Source}:{entry.SourceOrdinal}",
                                $"Epochs={string.Join(",", entry.EpochIds)}",
                                entry.EvidenceCode.ToString()
                            }),
                        entry.Source,
                        uiText.Get(entry.Source == EventPoolSourceKind.ActLocal
                            ? Ui1TextKey.EventSequenceSourceActLocal
                            : Ui1TextKey.EventSequenceSourceShared),
                        entry.EpochIds,
                        entry.Precision,
                        uiText.Get(UiTextKey.Precision(entry.Precision)),
                        entry.EvidenceCode.ToString())).ToArray(),
                item.RawActLocalCount,
                item.RawSharedCount,
                item.EligibleCount,
                item.EffectiveCount,
                item.FilteredOutCount,
                item.OpeningAncientCursorOffset,
                item.OpeningAncientSkippedCount,
                item.StaticFilteredOutCount,
                item.DuplicateFilteredOutCount,
                item.EpochFilters,
                item.StaticExclusions,
                item.Precision,
                uiText.Get(UiTextKey.Precision(item.Precision)),
                item.Authority,
                item.Completeness,
                item.RngStream,
                item.RngCallCountBefore,
                item.RngCallCountAfter,
                item.EvidenceCode.ToString()))
            .ToArray();
        return new SeedDomainViewModel<EventPoolActSequenceViewModel>(section.DomainStatus, section.IssueCode, acts);
    }

    private static SeedDomainViewModel<RelicSequenceLaneViewModel> BuildRelicSequenceDomain(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds,
        bool treasureRoom = false)
    {
        PredictionSection? section = document.Sections.FirstOrDefault(item => item.Kind == PredictionSectionKind.RelicSequences);
        RelicSequencePredictionResult? prediction = section?.RelicSequencePrediction;
        if (section is null || prediction is null)
        {
            return new SeedDomainViewModel<RelicSequenceLaneViewModel>(
                SeedDomainEvaluationStatus.Unknown,
                "RelicSequenceSectionMissing",
                Array.Empty<RelicSequenceLaneViewModel>());
        }

        RelicSequenceLaneViewModel[] lanes = (treasureRoom ? prediction.TreasureRoomLanes : prediction.Lanes)
            .Select(lane => new RelicSequenceLaneViewModel(
                lane.Kind,
                lane.RarityCode,
                uiText.Get(RelicSequenceTitleKey(lane.Kind)),
                lane.PullDirection,
                uiText.Get(lane.PullDirection == RelicSequencePullDirection.Front
                    ? Ui1TextKey.RelicSequenceFront
                    : Ui1TextKey.RelicSequenceBack),
                lane.Entries.OrderBy(entry => entry.Position).Select(entry =>
                    BuildRelicSequenceEntry(
                        entry,
                        lane.Kind,
                        lane.PullDirection,
                        contentNames,
                        uiText,
                        showInternalIds)).ToArray(),
                lane.TailEntries.OrderBy(entry => entry.Position).Select(entry =>
                    BuildRelicSequenceEntry(
                        entry,
                        lane.Kind,
                        RelicSequencePullDirection.Back,
                        contentNames,
                        uiText,
                        showInternalIds)).ToArray(),
                lane.TotalCount,
                lane.Precision,
                uiText.Get(UiTextKey.Precision(lane.Precision)),
                lane.Authority,
                lane.Completeness,
                prediction.RngStream,
                prediction.RngCallCount,
                lane.EvidenceCode.ToString())
            {
                FullEntries = lane.FullEntries.Select(entry => BuildRelicSequenceEntry(entry, lane.Kind,
                    lane.PullDirection, contentNames, uiText, showInternalIds)).ToArray()
            })
            .ToArray();
        return new SeedDomainViewModel<RelicSequenceLaneViewModel>(section.DomainStatus, section.IssueCode, lanes);
    }

    private static RelicSequenceEntryViewModel BuildRelicSequenceEntry(
        RelicSequenceEntryResult entry,
        RelicSequenceKind laneKind,
        RelicSequencePullDirection direction,
        IGameContentNameResolver contentNames,
        IUiTextProvider uiText,
        bool showInternalIds) =>
        new(
            entry.Position,
            GameContentDisplayPresentationBuilder.BuildRelic(
                entry.RelicKey,
                contentNames,
                showInternalIds,
                diagnosticLines: new[]
                {
                    $"Position={entry.Position}",
                    $"Lane={laneKind}",
                    $"Direction={direction}",
                    entry.EvidenceCode.ToString()
                }),
            entry.Precision,
            uiText.Get(UiTextKey.Precision(entry.Precision)),
            entry.EvidenceCode.ToString());

    private static SeedDomainViewModel<NormalCombatRewardRouteViewModel> BuildNormalCombatRewardDomain(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        PredictionSection? section = document.Sections.FirstOrDefault(
            item => item.Kind == PredictionSectionKind.NormalCombatRewardSequence);
        NormalCombatRewardSequencePredictionResult? prediction = section?.NormalCombatRewardSequencePrediction;
        if (section is null || prediction is null)
        {
            return new SeedDomainViewModel<NormalCombatRewardRouteViewModel>(
                SeedDomainEvaluationStatus.Unknown,
                "NormalCombatRewardSectionMissing",
                Array.Empty<NormalCombatRewardRouteViewModel>());
        }

        NormalCombatRewardRouteViewModel[] routes = prediction.Routes
            .OrderBy(route => route.RepresentativeContinuation.Route.RouteOrder)
            .ThenBy(route => route.RouteGroupId, StringComparer.Ordinal)
            .Select(route => new NormalCombatRewardRouteViewModel(
                route.RouteGroupId,
                BuildOpeningRewardRouteLabel(route.RepresentativeContinuation.Route, contentNames, showInternalIds),
                route.EquivalentRoutes
                    .OrderBy(item => item.RouteOrder)
                    .Select(item => BuildOpeningRewardRouteLabel(item, contentNames, showInternalIds))
                    .ToArray(),
                route.Status,
                route.IssueCode,
                route.RepresentativeContinuation.RewardsDrawCount,
                route.RepresentativeContinuation.RewardContextFingerprint,
                route.ContinuationFingerprint,
                route.RepresentativeContinuation.Capabilities,
                route.RepresentativeContinuation.UnknownReasonCodes,
                route.Precision,
                uiText.Get(UiTextKey.Precision(route.Precision)),
                route.Authority,
                route.Completeness,
                route.Battles
                    .OrderBy(item => item.BattleOrdinal)
                    .Select(item => BuildNormalCombatRewardBattleViewModel(
                        item,
                        uiText,
                        contentNames,
                        showInternalIds))
                    .ToArray(),
                route.EvidenceCode.ToString())
            {
                EquivalentRouteBindings = route.EquivalentRoutes
                    .OrderBy(item => item.RouteOrder)
                    .ThenBy(item => item.RouteId, StringComparer.Ordinal)
                    .Select(item => BuildOpeningRoutePresentation(item, contentNames, showInternalIds))
                    .ToArray(),
                ActiveRewardImpactSources = route.RepresentativeContinuation.ActiveRewardImpactSources,
                RewardImpactFingerprint = route.RepresentativeContinuation.RewardImpactFingerprint
            })
            .ToArray();

        return new SeedDomainViewModel<NormalCombatRewardRouteViewModel>(
            section.DomainStatus,
            section.IssueCode,
            routes);
    }

    private static NormalCombatRewardBattleViewModel BuildNormalCombatRewardBattleViewModel(
        NormalCombatRewardBattleResult item,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        NormalCombatRewardCardViewModel BuildCard(NormalCombatRewardCardResult card)
        {
            string upgradeSuffix = card.UpgradeState == NormalCombatCardUpgradeState.Upgraded ? "+" : string.Empty;
            string enchantmentSuffix = card.Enchantments.Contains(NormalCombatCardEnchantment.Glam)
                ? uiText.Get(Ui1TextKey.NormalCombatRewardCardGlamSuffix)
                : string.Empty;
            return new NormalCombatRewardCardViewModel(
                card.Ordinal,
                GameContentDisplayPresentationBuilder.BuildCard(
                    card.CardKey,
                    contentNames,
                    showInternalIds,
                    diagnosticLines: new[]
                    {
                        $"Battle={item.BattleOrdinal}",
                        $"Ordinal={card.Ordinal}",
                        $"Rarity={card.Rarity}",
                        $"CardType={card.CardType}",
                        $"IsUpgradable={card.IsUpgradable}",
                        $"UpgradeRoll={card.UpgradeRoll:0.000000}",
                        $"BaseUpgradeOdds={card.BaseUpgradeOdds:0.000000}",
                        $"Upgrade={card.UpgradeState}",
                        $"Enchantments={string.Join(",", card.Enchantments)}",
                        $"IsFromCombat={card.IsFromCombat}",
                        $"RewardImpactSources={string.Join(",", card.AppliedImpactSources.Select(source => source.Serialized))}",
                        card.EvidenceCode.ToString()
                    }),
                card.Rarity,
                card.UpgradeState,
                card.Enchantments,
                upgradeSuffix + enchantmentSuffix,
                card.Precision,
                uiText.Get(UiTextKey.Precision(card.Precision)),
                card.EvidenceCode.ToString());
        }

        NormalCombatRewardCardRewardResult[] cardRewards = item.CardRewards.Count > 0
            ? item.CardRewards.OrderBy(reward => reward.RewardOrdinal).ToArray()
            : new[]
            {
                new NormalCombatRewardCardRewardResult(
                    1,
                    true,
                    item.Cards,
                    item.EvidenceCode)
            };
        NormalCombatRewardCardRewardViewModel[] cardRewardViewModels = cardRewards
            .Select(reward => new NormalCombatRewardCardRewardViewModel(
                reward.RewardOrdinal,
                reward.IsFromCombat,
                reward.Cards.OrderBy(card => card.Ordinal).Select(BuildCard).ToArray(),
                reward.EvidenceCode.ToString()))
            .ToArray();
        IReadOnlyList<NormalCombatRewardCardViewModel> primaryCards =
            cardRewardViewModels.FirstOrDefault()?.Cards ?? Array.Empty<NormalCombatRewardCardViewModel>();
        NormalCombatRewardGoldRewardResult[] goldRewards = item.GoldRewards.Count > 0
            ? item.GoldRewards.OrderBy(reward => reward.RewardOrdinal).ToArray()
            : item.Gold.HasValue
                ? new[]
                {
                    new NormalCombatRewardGoldRewardResult(
                        1,
                        item.Gold.Value,
                        true,
                        item.GoldRngCallConsumed,
                        null,
                        item.EvidenceCode)
                }
                : Array.Empty<NormalCombatRewardGoldRewardResult>();
        NormalCombatRewardGoldRewardViewModel[] goldRewardViewModels = goldRewards
            .Select(reward => new NormalCombatRewardGoldRewardViewModel(
                reward.RewardOrdinal,
                reward.Amount,
                reward.IsBaseReward,
                reward.RngCallConsumed,
                reward.Source,
                reward.IsBaseReward
                    ? uiText.Format(Ui1TextKey.NormalCombatRewardGoldConditional, reward.Amount)
                    : uiText.Format(Ui1TextKey.NormalCombatRewardGoldFixedExtra, reward.Amount),
                reward.EvidenceCode.ToString()))
            .ToArray();

        return new NormalCombatRewardBattleViewModel(
            item.BattleOrdinal,
            item.Act,
            item.ActKey,
            uiText.Format(Ui1TextKey.NormalCombatRewardBattleTitle, item.BattleOrdinal, item.Act),
            uiText.Get(item.ProjectionScope == NormalCombatRewardBattleProjectionScope.OpeningRouteFirstCombat
                ? Ui1TextKey.NormalCombatRewardFirstBattleScope
                : Ui1TextKey.NormalCombatRewardConsecutiveBattleScope),
            item.Gold,
            item.GoldStatus,
            item.GoldRngCallConsumed,
            item.Gold.HasValue
                ? uiText.Format(Ui1TextKey.NormalCombatRewardGoldConditional, item.Gold.Value)
                : uiText.Get(Ui1TextKey.NormalCombatRewardGoldUnavailable),
            new NormalCombatRewardPotionViewModel(
                item.Potion.Generated,
                item.Potion.PotionKey is ModelKey potionKey && potionKey.IsValid
                    ? GameContentDisplayPresentationBuilder.Build(
                        potionKey,
                        GameContentKind.Potion,
                        contentNames,
                        showInternalIds,
                        diagnosticLines: new[]
                        {
                            $"Battle={item.BattleOrdinal}",
                            $"Rarity={item.Potion.Rarity}",
                            item.Potion.EvidenceCode.ToString()
                        })
                    : null,
                item.Potion.Rarity,
                item.Potion.Precision,
                uiText.Get(UiTextKey.Precision(item.Potion.Precision)),
                item.Potion.EvidenceCode.ToString()),
            primaryCards,
            item.PotionOddsBefore,
            item.PotionOddsAfter,
            item.CardRarityOffsetBefore,
            item.CardRarityOffsetAfter,
            item.Precision,
            uiText.Get(UiTextKey.Precision(item.Precision)),
            item.Authority,
            item.Completeness,
            item.RngStream,
            item.RngCallCountBefore,
            item.RngCallCountAfter,
            item.ReasonCode,
            item.ConditionalAssumptions,
            item.EvidenceCode.ToString())
        {
            CardRewards = cardRewardViewModels,
            GoldRewards = goldRewardViewModels,
            AppliedRewardImpactSources = item.AppliedRewardImpactSources,
            RewardImpactFingerprint = item.RewardImpactFingerprint
        };
    }

    private static string BuildOpeningRewardRouteLabel(
        OpeningRewardRouteDescriptor route,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        string Name(ModelKey key) => GameContentDisplayPresentationBuilder.Build(
            key,
            GameContentKind.Relic,
            contentNames,
            showInternalIds).DisplayName;

        if (route.RouteKind == OpeningRewardRouteKind.BonesAcquisitionOrder)
        {
            string order = string.Join(" → ", route.AcquisitionOrder.Select(Name));
            return string.IsNullOrWhiteSpace(order)
                ? Name(route.RootRelicKey)
                : $"{Name(route.RootRelicKey)} · {order}";
        }
        return Name(route.RootRelicKey);
    }

    private static OpeningRoutePresentationViewModel BuildOpeningRoutePresentation(
        OpeningRewardRouteDescriptor route,
        IGameContentNameResolver contentNames,
        bool showInternalIds) => new(
            route.RouteId,
            route.RouteOrder,
            route.RouteKind,
            route.RootRelicKey,
            route.AcquisitionOrder.ToArray(),
            BuildOpeningRewardRouteLabel(route, contentNames, showInternalIds));

    private static string RelicSequenceTitleKey(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => Ui1TextKey.RelicSequenceCommon,
        RelicSequenceKind.Uncommon => Ui1TextKey.RelicSequenceUncommon,
        RelicSequenceKind.Rare => Ui1TextKey.RelicSequenceRare,
        RelicSequenceKind.Shop => Ui1TextKey.RelicSequenceShop,
        _ => Ui1TextKey.ModuleRelicSequences
    };

    private static string TraceSummary(IReadOnlyList<WorldRngTraceEntry> trace)
    {
        if (trace.Count == 0) return "none";
        IEnumerable<WorldRngTraceEntry> tail = trace.Count <= 12 ? trace : trace.Skip(trace.Count - 12);
        string prefix = trace.Count <= 12 ? string.Empty : $"...{trace.Count - 12} prior;";
        return prefix + string.Join(";", tail.Select(item =>
            $"{item.Sequence}:{item.SourceStage}:{item.Operation}:{item.ConsumptionShape}:{item.CallCountAfter}"));
    }

    internal static NeowChoiceViewModel BuildChoice(
        NeowChoiceResult choice,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        bool showInternalIds)
    {
        GameContentDisplayViewModel relicDisplay = GameContentDisplayPresentationBuilder.BuildRelic(
            choice.RelicKey,
            contentNames,
            showInternalIds,
            diagnosticLines: new[]
            {
                choice.EvidenceCode.ToString(),
                choice.EffectEvidenceCode.ToString(),
                $"FullEffect={choice.FullEffectSemanticsCompleteness}"
            });
        string identityLabel = uiText.Get(UiTextKey.Precision(choice.IdentityPrecision));
        string productLabel = choice.ProductRelevantProjectionStatus switch
        {
            ProductRelevantProjectionStatus.NotEvaluatedByPolicy => uiText.Get(Ui1TextKey.ComplexNotEvaluatedByPolicy),
            ProductRelevantProjectionStatus.Unsupported => uiText.Get(Ui1TextKey.EffectUnsupported),
            ProductRelevantProjectionStatus.Unknown => uiText.Get(UiTextKey.Precision(PredictionPrecision.Unknown)),
            _ => uiText.Get(UiTextKey.Precision(choice.ProductRelevantProjectionPrecision))
        };
        string effectLabel = choice.EffectCapability switch
        {
            NeowEffectImplementationStatus.NotImplemented => uiText.Get(Ui1TextKey.EffectNotMigrated),
            NeowEffectImplementationStatus.UnsupportedEffectType => uiText.Get(Ui1TextKey.EffectUnsupported),
            NeowEffectImplementationStatus.NotApplicable => uiText.Get(Ui1TextKey.EffectNotApplicable),
            _ => productLabel
        };
        IReadOnlyList<string> warnings = choice.Warnings
            .Where(warning => IsUserProductWarning(warning.Code) &&
                !IsFullEffectOnlyWarningForExactProduct(choice, warning.Code))
            .Select(warning => uiText.Get(UiTextKey.Warning(warning.Code)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<PredictedEffectGroupViewModel> effectGroups = PredictedEffectPresentationBuilder.BuildGroups(
            choice.EffectGroups.Where(group => !group.IsPlayerChoiceGroup).ToArray(),
            uiText,
            contentNames,
            showInternalIds);
        BonesOutcomeViewModel? bones = BonesOutcomePresentationBuilder.Build(
            choice.BonesOutcome,
            uiText,
            contentNames,
            showInternalIds);
        IReadOnlyList<OpeningRoutePresentationViewModel> openingRoutes = choice.OpeningRewardContinuations?.Routes
            .OrderBy(route => route.Route.RouteOrder)
            .ThenBy(route => route.Route.RouteId, StringComparer.Ordinal)
            .Select(route => BuildOpeningRoutePresentation(route.Route, contentNames, showInternalIds))
            .ToArray() ?? Array.Empty<OpeningRoutePresentationViewModel>();

        return new NeowChoiceViewModel(
            choice.SlotIndex,
            relicDisplay,
            choice.IdentityPrecision,
            identityLabel,
            choice.EffectCapability,
            choice.EffectPrecision,
            effectLabel,
            choice.ProductRelevantProjectionPrecision,
            productLabel,
            choice.ProductRelevantProjectionStatus,
            choice.FullEffectSemanticsCompleteness,
            warnings,
            choice.EvidenceCode.ToString(),
            choice.EffectEvidenceCode.ToString(),
            effectGroups,
            bones,
            openingRoutes,
            choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated
                ? (effectGroups.Any(group => group.ShowInNormalMode) || bones is not null
                    ? NeowPredictionPresentationState.Predicted
                    : NeowPredictionPresentationState.NoAdditionalPredictionNeeded)
                : NeowPredictionPresentationState.PredictionUnavailable,
            effectGroups.Any(group => group.ShowInNormalMode) || bones is not null);
    }

    private static bool IsUserProductWarning(PredictionWarningCode code) =>
        PlayerWarningPresentationPolicy.IsPlayerFacing(code);

    private static bool IsFullEffectOnlyWarningForExactProduct(
        NeowChoiceResult choice,
        PredictionWarningCode code)
    {
        bool isFullEffectOnlyCode = code is
            PredictionWarningCode.EffectSnapshotIncomplete or
            PredictionWarningCode.EffectNestedObtainIncomplete or
            PredictionWarningCode.EffectNotImplemented or
            PredictionWarningCode.ComplexRouteNotEvaluated;
        if (!isFullEffectOnlyCode)
        {
            return false;
        }

        bool choiceProductExact =
            choice.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
            choice.ProductRelevantProjectionPrecision == PredictionPrecision.Exact;
        bool bonesRoutesProductBounded = choice.BonesOutcome is { } bones &&
            bones.OriginalRoutes.Count > 0 &&
            bones.OriginalRoutes.All(route => EffectiveRouteStatus(route) is
                RouteProjectionStatus.Exact or RouteProjectionStatus.NotEvaluatedByPolicy);

        // A policy-disabled opposite order does not turn full/future semantics
        // from an otherwise Exact route into an ordinary product warning.
        return choiceProductExact || bonesRoutesProductBounded;
    }

    private static RouteProjectionStatus EffectiveRouteStatus(BonesAcquisitionRouteResult route)
    {
        if (route.RouteProjectionStatus != RouteProjectionStatus.Unknown)
        {
            return route.RouteProjectionStatus;
        }

        return route.ProductRelevantProjectionStatus switch
        {
            ProductRelevantProjectionStatus.NotEvaluatedByPolicy => RouteProjectionStatus.NotEvaluatedByPolicy,
            ProductRelevantProjectionStatus.Unsupported => RouteProjectionStatus.Unsupported,
            ProductRelevantProjectionStatus.Unknown => RouteProjectionStatus.Unknown,
            ProductRelevantProjectionStatus.Evaluated
                when route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
                     route.SharedContinuation.FinalCursePrecision == PredictionPrecision.Exact =>
                RouteProjectionStatus.Exact,
            ProductRelevantProjectionStatus.Evaluated => RouteProjectionStatus.Partial,
            _ => RouteProjectionStatus.Unknown
        };
    }

    private static bool IsNeowScopedWarningHandledInOpeningOrChoice(
        PredictionWarning warning,
        IReadOnlyList<NeowChoiceResult> choices)
    {
        if (warning.Code == PredictionWarningCode.EffectSnapshotIncomplete)
        {
            return true;
        }
        return warning.RelatedKey.HasValue &&
               choices.Any(choice => choice.RelicKey == warning.RelatedKey.Value);
    }

    private static bool IsDocumentUserProductWarning(
        PredictionWarning warning,
        IReadOnlyList<NeowChoiceResult> choices)
    {
        if (!IsUserProductWarning(warning.Code))
        {
            return false;
        }
        if (!warning.RelatedKey.HasValue)
        {
            if (warning.Code != PredictionWarningCode.EffectSnapshotIncomplete)
            {
                return true;
            }

            // Suppress an unscoped snapshot warning only when every Neow product
            // projection proves that the warning is full-effect-only. Otherwise
            // retain it and add domain/observable provenance during formatting.
            return choices.Count == 0 ||
                   choices.Any(choice => !IsFullEffectOnlyWarningForExactProduct(choice, warning.Code));
        }

        NeowChoiceResult? choice = choices.FirstOrDefault(candidate => candidate.RelicKey == warning.RelatedKey.Value);
        return choice is null || !IsFullEffectOnlyWarningForExactProduct(choice, warning.Code);
    }

    private static string FormatDocumentUserProductWarning(
        PredictionWarning warning,
        IReadOnlyList<NeowChoiceResult> choices,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames)
    {
        string message = uiText.Get(UiTextKey.Warning(warning.Code));
        if (warning.RelatedKey.HasValue)
        {
            string observable = contentNames.Resolve(warning.RelatedKey.Value, GameContentKind.Relic);
            return uiText.Format(Ui1TextKey.WarningNeowScope, observable, message);
        }

        // Only the historically generic EffectSnapshotIncomplete warning is
        // inferred into Neow scope here. Other document-level warnings keep
        // their existing domain-neutral presentation unless they carry an
        // explicit RelatedKey.
        if (warning.Code != PredictionWarningCode.EffectSnapshotIncomplete)
        {
            return message;
        }

        NeowChoiceResult[] affectedChoices = choices
            .Where(choice => !IsFullEffectOnlyWarningForExactProduct(choice, warning.Code))
            .ToArray();
        if (affectedChoices.Length == 1)
        {
            string observable = contentNames.Resolve(affectedChoices[0].RelicKey, GameContentKind.Relic);
            return uiText.Format(Ui1TextKey.WarningNeowScope, observable, message);
        }

        return uiText.Format(Ui1TextKey.WarningNeowDomainScope, message);
    }
}
