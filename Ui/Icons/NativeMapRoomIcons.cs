using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using GameMapPointType = MegaCrit.Sts2.Core.Map.MapPointType;

namespace RolltheSpire2.Ui.Icons;

internal static class NativeMapRoomIcons
{
    // Both methods were private in 0.111 and became public in 0.113.
    private const BindingFlags NativeStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Func<GameMapPointType, string>? IconName = typeof(NNormalMapPoint)
        .GetMethod("IconName", NativeStatic, [typeof(GameMapPointType)])?.CreateDelegate<Func<GameMapPointType, string>>();
    private static readonly Func<string, string>? IconPath = typeof(NNormalMapPoint)
        .GetMethod("IconPath", NativeStatic, [typeof(string)])?.CreateDelegate<Func<string, string>>();

    internal static string PathFor(GameMapPointType type)
    {
        if (IconName is null || IconPath is null)
            throw new MissingMethodException(typeof(NNormalMapPoint).FullName, IconName is null ? "IconName" : "IconPath");
        return IconPath(IconName(type));
    }

    internal static Texture2D? Resolve(GameMapPointType type)
    {
        var path = PathFor(type);
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    }
}
