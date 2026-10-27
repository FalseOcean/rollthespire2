using System.Collections.Immutable;

namespace RolltheSpire2.Core.Authority;

/// <summary>
/// Official 0.111.0's 57 Epoch IDs, source-audited from EpochModel's explicit
/// static constructor type list and those Epoch types' literal Id properties.
/// This frozen universe excludes Mod-added Epochs and is independent of discovery.
/// </summary>
public static class Vanilla111EpochBaseline
{
    public static ImmutableArray<string> EpochIds { get; } =
    [
        "ACT2_B_EPOCH",
        "ACT3_B_EPOCH",
        "COLORLESS1_EPOCH",
        "COLORLESS2_EPOCH",
        "COLORLESS3_EPOCH",
        "COLORLESS4_EPOCH",
        "COLORLESS5_EPOCH",
        "CUSTOM_AND_SEEDS_EPOCH",
        "DAILY_RUN_EPOCH",
        "DARV_EPOCH",
        "DEFECT1_EPOCH",
        "DEFECT2_EPOCH",
        "DEFECT3_EPOCH",
        "DEFECT4_EPOCH",
        "DEFECT5_EPOCH",
        "DEFECT6_EPOCH",
        "DEFECT7_EPOCH",
        "EVENT1_EPOCH",
        "EVENT2_EPOCH",
        "EVENT3_EPOCH",
        "IRONCLAD2_EPOCH",
        "IRONCLAD3_EPOCH",
        "IRONCLAD4_EPOCH",
        "IRONCLAD5_EPOCH",
        "IRONCLAD6_EPOCH",
        "IRONCLAD7_EPOCH",
        "NECROBINDER1_EPOCH",
        "NECROBINDER2_EPOCH",
        "NECROBINDER3_EPOCH",
        "NECROBINDER4_EPOCH",
        "NECROBINDER5_EPOCH",
        "NECROBINDER6_EPOCH",
        "NECROBINDER7_EPOCH",
        "NEOW_EPOCH",
        "OROBAS_EPOCH",
        "POTION1_EPOCH",
        "POTION2_EPOCH",
        "REGENT1_EPOCH",
        "REGENT2_EPOCH",
        "REGENT3_EPOCH",
        "REGENT4_EPOCH",
        "REGENT5_EPOCH",
        "REGENT6_EPOCH",
        "REGENT7_EPOCH",
        "RELIC1_EPOCH",
        "RELIC2_EPOCH",
        "RELIC3_EPOCH",
        "RELIC4_EPOCH",
        "RELIC5_EPOCH",
        "SILENT1_EPOCH",
        "SILENT2_EPOCH",
        "SILENT3_EPOCH",
        "SILENT4_EPOCH",
        "SILENT5_EPOCH",
        "SILENT6_EPOCH",
        "SILENT7_EPOCH",
        "UNDERDOCKS_EPOCH",
    ];
}
