using System.Text;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;

namespace RolltheSpire2.Presentation;

public sealed record SeedAnalysisFormatOptions(
    bool IncludeDiagnostics = false,
    bool ShowIdentityKeys = false);

public static class SeedAnalysisFormatter
{
    public static string Format(
        SeedPredictionDocument document,
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        SeedAnalysisFormatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(uiText);
        ArgumentNullException.ThrowIfNull(contentNames);
        options ??= new SeedAnalysisFormatOptions();

        var builder = new StringBuilder();
        builder.Append(uiText.Get(UiTextKey.Game)).Append(": ").AppendLine(document.DetectedGameVersion);
        builder.Append(uiText.Get(UiTextKey.Profile)).Append(": ").AppendLine(document.ProfileId.ToString());
        builder.Append(uiText.Get(UiTextKey.CanonicalSeed)).Append(": ").AppendLine(
            string.IsNullOrWhiteSpace(document.CanonicalSeed) ? "-" : document.CanonicalSeed);

        string characterName = contentNames.Resolve(document.Context.CharacterKey, GameContentKind.Character);
        builder.Append(uiText.Get(UiTextKey.Request)).Append(": ").AppendLine(uiText.Format(
            UiTextKey.RequestSummary,
            characterName,
            document.Context.Ascension,
            document.Context.PlayersCount,
            uiText.Get(UiTextKey.SourceAuthorityTextKey(document.Context.SourceAuthority)),
            uiText.Get(UiTextKey.Completeness(document.Context.SnapshotCompleteness))));
        builder.Append(uiText.Get(UiTextKey.Status)).Append(": ")
            .AppendLine(uiText.Get(UiTextKey.OverallStatus(document.OverallStatus)));

        foreach (PredictionSection section in document.Sections)
        {
            if (section.Kind != PredictionSectionKind.NeowIdentity)
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine(uiText.Get(UiTextKey.NeowSection) + ":");
            foreach (NeowChoiceResult choice in section.NeowChoices)
            {
                GameContentDisplayViewModel relicDisplay = GameContentDisplayPresentationBuilder.BuildRelic(
                    choice.RelicKey,
                    contentNames,
                    options.ShowIdentityKeys);
                builder.Append(choice.SlotIndex).Append(". ").Append(relicDisplay.DisplayName);
                builder.AppendLine();
                builder.Append("   ").Append(uiText.Get(UiTextKey.Identity)).Append(": ")
                    .AppendLine(uiText.Get(UiTextKey.Precision(choice.IdentityPrecision)));
                string productPrecisionLabel = choice.ProductRelevantProjectionStatus switch
                {
                    ProductRelevantProjectionStatus.NotEvaluatedByPolicy =>
                        uiText.Get(Ui1TextKey.ComplexNotEvaluatedByPolicy),
                    ProductRelevantProjectionStatus.Unsupported => uiText.Get(Ui1TextKey.EffectUnsupported),
                    ProductRelevantProjectionStatus.Unknown =>
                        uiText.Get(UiTextKey.Precision(PredictionPrecision.Unknown)),
                    _ => uiText.Get(UiTextKey.Precision(choice.ProductRelevantProjectionPrecision))
                };
                builder.Append("   ").Append(uiText.Get(UiTextKey.Effect)).Append(": ")
                    .AppendLine(productPrecisionLabel);

                if (choice.BonesOutcome is not null)
                {
                    BonesOutcomeViewModel? bones = BonesOutcomePresentationBuilder.Build(
                        choice.BonesOutcome,
                        uiText,
                        contentNames,
                        options.ShowIdentityKeys);
                    if (bones is not null)
                    {
                        builder.Append("   ")
                            .Append(bones.ResultsCountLabel)
                            .Append(" · ")
                            .AppendLine(bones.OrderComparisonLabel);
                        foreach (BonesOutcomeGroupViewModel outcomeGroup in bones.OutcomeGroups)
                        {
                            builder.Append("   - ").Append(outcomeGroup.Title)
                                .Append(" · ").AppendLine(outcomeGroup.RouteProjectionLabel);
                            foreach (BonesRelicScopedResultViewModel relicResult in outcomeGroup.RelicResults)
                            {
                                builder.Append("     ").Append(relicResult.DisplayName)
                                    .Append(" · ").AppendLine(relicResult.ProductPrecisionLabel);
                                AppendEffectGroups(
                                    builder,
                                    relicResult.EffectGroups,
                                    uiText,
                                    "       ");
                            }
                            builder.Append("     ").AppendLine(outcomeGroup.SharedContinuationLabel);
                            AppendEffectGroups(
                                builder,
                                outcomeGroup.SharedContinuationGroups,
                                uiText,
                                "       ");
                            if (outcomeGroup.RouteWarningLabels.Count > 0)
                            {
                                builder.Append("     △ ").AppendLine(outcomeGroup.RouteWarningLabels[0]);
                            }
                        }
                    }

                    if (!options.IncludeDiagnostics)
                    {
                        continue;
                    }
                }

                AppendEffectGroups(
                    builder,
                    PredictedEffectPresentationBuilder.BuildGroups(
                        choice.EffectGroups,
                        uiText,
                        contentNames,
                        options.ShowIdentityKeys),
                    uiText,
                    "   ");
            }
        }

        PredictionSection? relicSequenceSection = document.Sections
            .FirstOrDefault(section => section.Kind == PredictionSectionKind.RelicSequences);
        if (relicSequenceSection?.RelicSequencePrediction is { } relicSequence)
        {
            builder.AppendLine();
            builder.AppendLine(uiText.Get(Ui1TextKey.ModuleRelicSequences) + ":");
            if (relicSequence.Status != RolltheSpire2.Core.World.SeedDomainEvaluationStatus.Evaluated)
            {
                builder.AppendLine(uiText.Format(
                    Ui1TextKey.WorldUnavailable,
                    relicSequence.Status,
                    relicSequence.IssueCode));
            }
            else
            {
                foreach (var lane in relicSequence.Lanes)
                {
                    string laneKey = lane.Kind switch
                    {
                        RolltheSpire2.Core.Relics.RelicSequenceKind.Common => Ui1TextKey.RelicSequenceCommon,
                        RolltheSpire2.Core.Relics.RelicSequenceKind.Uncommon => Ui1TextKey.RelicSequenceUncommon,
                        RolltheSpire2.Core.Relics.RelicSequenceKind.Rare => Ui1TextKey.RelicSequenceRare,
                        _ => Ui1TextKey.RelicSequenceShop
                    };
                    string directionKey = lane.PullDirection == RolltheSpire2.Core.Relics.RelicSequencePullDirection.Front
                        ? Ui1TextKey.RelicSequenceFront
                        : Ui1TextKey.RelicSequenceBack;
                    builder.Append("- ").Append(uiText.Get(laneKey)).Append(" · ")
                        .AppendLine(uiText.Get(directionKey));
                    foreach (var entry in lane.Entries.OrderBy(item => item.Position))
                    {
                        GameContentDisplayViewModel relicDisplay = GameContentDisplayPresentationBuilder.BuildRelic(
                            entry.RelicKey,
                            contentNames,
                            options.ShowIdentityKeys);
                        builder.Append("  ").Append(entry.Position).Append(". ")
                            .AppendLine(relicDisplay.DisplayName);
                    }
                }
            }
        }

        IEnumerable<PredictionWarning> warningSource = document.Warnings
            .Concat(document.Sections.SelectMany(section => section.NeowChoices).SelectMany(choice => choice.Warnings));
        IReadOnlyList<PredictionWarning> warnings = options.ShowIdentityKeys
            ? warningSource.Distinct().ToArray()
            : warningSource.GroupBy(warning => warning.Code).Select(group => group.First()).ToArray();
        if (warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(uiText.Get(UiTextKey.Warnings) + ":");
            foreach (PredictionWarning warning in warnings)
            {
                builder.Append("- ").Append(uiText.Get(UiTextKey.Warning(warning.Code)));
                if (options.ShowIdentityKeys && warning.RelatedKey.HasValue)
                {
                    builder.Append(" [").Append(warning.RelatedKey.Value.Serialized).Append(']');
                }
                builder.AppendLine();
            }
        }

        if (options.IncludeDiagnostics)
        {
            builder.AppendLine();
            builder.AppendLine(uiText.Get(UiTextKey.Diagnostics) + ":");
            foreach (PredictionDiagnostic diagnostic in document.Diagnostics)
            {
                builder.Append("- ").Append(diagnostic.Code).Append('=').AppendLine(diagnostic.Value);
            }
            foreach (PredictionSection section in document.Sections)
            {
                builder.Append("- ").Append(PredictionDiagnosticCodes.PredictionDomain).Append('=').AppendLine(section.Domain.ToString());
                builder.Append("- ").Append(PredictionDiagnosticCodes.PredictionScope).Append('=').AppendLine(section.Scope.ToString());
                builder.Append("- ").Append(PredictionDiagnosticCodes.SourceState).Append('=').AppendLine(section.SourceState.ToString());
                foreach (NeowChoiceResult choice in section.NeowChoices)
                {
                    builder.Append("- ").Append(PredictionDiagnosticCodes.ChoiceEvidence)
                        .Append('.').Append(choice.SlotIndex).Append('=')
                        .AppendLine(choice.EvidenceCode.ToString());
                    builder.Append("- ").Append(PredictionDiagnosticCodes.EffectCapability)
                        .Append('.').Append(choice.SlotIndex).Append('=')
                        .AppendLine(choice.EffectCapability.ToString());
                    builder.Append("- ").Append(PredictionDiagnosticCodes.EffectEvidence)
                        .Append('.').Append(choice.SlotIndex).Append('=')
                        .AppendLine(choice.EffectEvidenceCode.ToString());
                }
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendEffectGroups(
        StringBuilder builder,
        IReadOnlyList<PredictedEffectGroupViewModel> groups,
        IUiTextProvider uiText,
        string indent)
    {
        foreach (PredictedEffectGroupViewModel group in groups.OrderBy(group => group.GroupOrder))
        {
            builder.Append(indent)
                .Append(uiText.Get(Ui1TextKey.EffectDetails))
                .Append(" · ")
                .Append(group.SelectionPolicyLabel)
                .Append(" · ")
                .AppendLine(group.PrecisionLabel);
            foreach (PredictedEffectItemViewModel effect in group.OrderedItems.OrderBy(effect => effect.ItemOrder))
            {
                builder.Append(indent).Append("- ").AppendLine(effect.DisplayText);
            }
        }
    }

}
