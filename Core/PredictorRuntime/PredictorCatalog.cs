using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorCatalog(ImmutableArray<NeowEffectCardSnapshot> CharacterCards,
    ImmutableArray<NeowEffectCardSnapshot> ColorlessCards)
{
    public ImmutableArray<NeowEffectCardSnapshot> AllCharacterCards { get; init; } = [];
    public ImmutableArray<PredictorCardDefinition> CardDefinitions { get; init; } = [];
    public ImmutableArray<PredictorCardPool> TransformationPools { get; init; } = [];
    public ImmutableArray<ModelKey> UnlockedCurseCards { get; init; } = [];
    public ImmutableArray<ModelKey> ModifierCurseCards { get; init; } = [];
    public ImmutableArray<PredictorCharacterPool> CharacterPools { get; init; } = [];
    public ImmutableArray<ModelKey> ShopGainsBlock { get; init; } = [];
    public ModelKey? CharacterStrikeKey { get; init; }
    public ModelKey? CharacterDefendKey { get; init; }
    public ImmutableArray<ModelKey> EventPetRelics { get; init; } = [];
    public ImmutableArray<ModelKey> StarterRelics { get; init; } = [];
    public ImmutableArray<ModelKey> DustyTomeCards { get; init; } = [];
    public ImmutableArray<ModelKey> TradableRelicTypes { get; init; } = [];
}

internal sealed record PredictorCharacterPool(ModelKey Character, ImmutableArray<ModelKey> Cards);

internal sealed record PredictorCardDefinition(PredictorCard Prototype, string Rarity, ModelKey TransformationPool,
    bool IsQuest, bool IsStrike, bool IsDefend)
{
    public bool Unplayable { get; init; }
    public bool CostsX { get; init; }
    public bool HasLocalExhaust { get; init; }
    public int CanonicalCost { get; init; }
    public ImmutableArray<PredictorCardLevelMetadata> Levels { get; init; } = [];
}
internal sealed record PredictorCardLevelMetadata(int UpgradeLevel, int LocalCost, bool CostsX, bool Unplayable,
    bool LocalExhaust, bool Innate, bool Retain, bool Eternal);
internal sealed record PredictorCardPool(ModelKey Key, ImmutableArray<ModelKey> Cards);

