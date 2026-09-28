using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Compatibility;

/// <summary>
/// Resolves the immutable prediction request mode independently from the current live
/// network session. Main-menu analysis/search predicts the requested virtual run; a
/// stale Host/Client NetService is diagnostic-only and must not override an explicit
/// request contract.
/// </summary>
public readonly record struct PredictionGameModeResolution(
    bool IsMultiplayer,
    bool IsExact,
    bool RequestedGameModeExact,
    string EvidenceCode);

public static class PredictionGameModeResolver
{
    public static PredictionGameModeResolution Resolve(
        WorldGameMode requestedMode,
        PredictionGameModeAuthority authority,
        int playersCount,
        int playerSlotIndex,
        bool runtimeIsMultiplayer,
        bool runtimeModeExact,
        string runtimeEvidenceCode)
    {
        bool explicitAuthority = authority == PredictionGameModeAuthority.ExplicitRequest;
        bool validSlot = playersCount > 0 &&
                         playerSlotIndex >= 0 &&
                         playerSlotIndex < playersCount;
        bool explicitSingleplayer = explicitAuthority &&
                                    requestedMode == WorldGameMode.Singleplayer &&
                                    playersCount == 1 &&
                                    playerSlotIndex == 0;
        bool explicitMultiplayer = explicitAuthority &&
                                   requestedMode == WorldGameMode.Multiplayer &&
                                   playersCount > 1 &&
                                   validSlot;

        if (explicitSingleplayer)
        {
            return new PredictionGameModeResolution(
                IsMultiplayer: false,
                IsExact: true,
                RequestedGameModeExact: true,
                EvidenceCode: "ExplicitPredictionContext:Singleplayer");
        }

        if (explicitMultiplayer)
        {
            return new PredictionGameModeResolution(
                IsMultiplayer: true,
                IsExact: true,
                RequestedGameModeExact: true,
                EvidenceCode: "ExplicitPredictionContext:Multiplayer");
        }

        if (explicitAuthority)
        {
            return new PredictionGameModeResolution(
                IsMultiplayer: false,
                IsExact: false,
                RequestedGameModeExact: false,
                EvidenceCode: "InvalidPredictionContext");
        }

        return new PredictionGameModeResolution(
            IsMultiplayer: runtimeIsMultiplayer,
            IsExact: runtimeModeExact && validSlot,
            RequestedGameModeExact: false,
            EvidenceCode: string.IsNullOrWhiteSpace(runtimeEvidenceCode)
                ? "MissingMultiplayerMode"
                : runtimeEvidenceCode);
    }
}
