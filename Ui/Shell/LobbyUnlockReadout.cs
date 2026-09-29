using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace RolltheSpire2.Ui.Shell;

/// <summary>UI-only, main-thread reading of connected pre-run lobby data. Never synthesizes remote unlocks.</summary>
internal static class LobbyUnlockReadout
{
    internal static StartRunLobby? ConnectedLobby(Node screen)
    {
        var lobby = screen switch
        {
            NCharacterSelectScreen standard => standard.Lobby,
            NCustomRunScreen custom => custom.Lobby,
            _ => null
        };
        return lobby is not null && lobby.NetService.IsConnected &&
            lobby.NetService.Type is NetGameType.Host or NetGameType.Client ? lobby : null;
    }

    public static bool IsCustomMultiplayerLobby(Node screen) => ConnectedLobby(screen)?.GameMode == GameMode.Custom;

    public static StartRunLobby? Find(Node root)
    {
        if (ConnectedLobby(root) is { } connected) return connected;
        foreach (var child in root.GetChildren())
            if (Find(child) is { } lobby) return lobby;
        return null;
    }

    public static SerializableUnlockState Copy(SerializableUnlockState source)
    {
        // UnlockState.FromSerializable(null) means ALL in vanilla; do not use that fallback for a failed read.
        ArgumentNullException.ThrowIfNull(source);
        if (source.UnlockedEpochs is null || source.EncountersSeen is null)
            throw new InvalidOperationException("Incomplete lobby unlock data");
        return new SerializableUnlockState
        {
            UnlockedEpochs = source.UnlockedEpochs.ToList(),
            EncountersSeen = source.EncountersSeen.ToList(),
            NumberOfRuns = source.NumberOfRuns
        };
    }

    public static string Roster(StartRunLobby lobby) => string.Join(";",
        lobby.Players.OrderBy(p => p.slotId).Select(p => $"{p.slotId}:{p.id}"));
}
