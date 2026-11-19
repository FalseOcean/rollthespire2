using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace RolltheSpire2.Bootstrap;

[ModInitializer(nameof(Initialize))]
public partial class ModEntry : Node
{
    public const string ModId = "RolltheSpire2";
    internal const string TargetStableGameVersion = "0.111.1";
    internal const string TargetBetaGameVersion = "0.113.1";

    public static void Initialize()
    {
        ModRuntime.Initialize();
    }
}
