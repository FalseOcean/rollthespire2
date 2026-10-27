using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    private void CaptureResultPools(ModelKey character, int ascension, int players,
        Core.Authority.RuntimeContextAuthoritySnapshot? partyAuthority = null,
        MegaCrit.Sts2.Core.Unlocks.UnlockState? unlocks = null)
    {
        _draft.TransformCards = [];
        _draft.CardPickerMetadata = new Dictionary<ModelKey, CardPickerCandidateMetadata>(ModelKeyComparer.Instance);
        _draft.MultiplayerOnlyCards = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        var authority = partyAuthority;
        if (authority is null)
        {
            if (players != 1) return;
            WorkbenchQueryCompiler.CompileAuthoredQuery(SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault,
                _runtime, character, ascension, out authority);
        }
        var scenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredBasics(authority, 2, unlocks);
        if (authority.EffectAuthority is { } effects)
        {
            _draft.CardPickerMetadata = CardPickerMetadataCapture.FromAuthority(effects, character);
            _draft.MultiplayerOnlyCards = (effects.CharacterRewardPool ?? []).Concat(effects.ColorlessRewardPool ?? [])
                .Concat(effects.TransformPool ?? []).Concat((effects.OtherCharacterPools ?? []).SelectMany(pool => pool.Cards))
                .Where(card => card.IsMultiplayerOnly).Select(card => card.CardKey).ToHashSet(ModelKeyComparer.Instance);
        }
        _draft.TransformCards = scenario.Targets.SelectMany(t => t.OrderedSourceCandidates ?? [])
            .Select(c => c.CardKey).Distinct().ToArray();
    }

    private IReadOnlyList<ModelKey> ResultPool(EventResultConditionKind kind) => kind switch
    {
        EventResultConditionKind.FakeMerchantOfferedFakeRelic => Beta111EventResultCatalog.FakeMerchantRelics,
        EventResultConditionKind.TrashHeapGrabCard => Beta111EventResultCatalog.TrashHeapGrabCards,
        EventResultConditionKind.TrashHeapDiveRelic => Beta111EventResultCatalog.TrashHeapDiveRelics,
        _ when EventResultTransformSemantics.IsTransform(kind) => _draft.TransformCards,
        _ => Beta111EventResultCatalog.ColorfulCharacterOrder
    };
}
