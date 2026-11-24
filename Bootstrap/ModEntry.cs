using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace RolltheSpire2.Bootstrap;

[ModInitializer(nameof(Initialize))]
public partial class ModEntry : Node
{
    public const string ModId = "RolltheSpire2";

    public static void Initialize()
    {
        ModRuntime.Initialize();
    }
}
