using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace RolltheSpire2.Ui.InRunConsole;

// Public parameterless subtype: discovered by the original game's DevConsole mod scan.
// Local-only by product scope. ProcessCustom independently rejects multiplayer,
// including direct invocation from the original game console.
public sealed class ConsoleActionCommand : AbstractConsoleCmd
{
    public override string CmdName => "rt2_action";
    public override string Args => "<payload>";
    public override string Description => "Execute a singleplayer RT2 in-run test console action.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;
    public override CmdResult Process(Player? issuingPlayer, string[] args) => ConsoleActions.ProcessCustom(issuingPlayer, args);
}
