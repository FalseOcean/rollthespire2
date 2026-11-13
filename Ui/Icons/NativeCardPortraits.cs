using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace RolltheSpire2.Ui.Icons;

internal static class NativeCardPortraits
{
    private static readonly ConditionalWeakTable<CardModel, Texture2D> DisplayPortraits = new();

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.PropertyGetter(typeof(CardModel), nameof(CardModel.Portrait))
            ?? throw new MissingMethodException(typeof(CardModel).FullName, nameof(CardModel.Portrait)),
        prefix: new HarmonyMethod(typeof(NativeCardPortraits), nameof(UseDisplayPortrait)));

    internal static void BindDisplayModel(CardModel detachedModel)
    {
        if (ResolvePackagedBetaPortrait(detachedModel) is { } portrait)
            DisplayPortraits.Add(detachedModel, portrait);
    }

    // Limit the override to the detached CardModel registered by an RT2 card view.
    private static bool UseDisplayPortrait(CardModel __instance, ref Texture2D __result)
    {
        if (!DisplayPortraits.TryGetValue(__instance, out var texture)) return true;
        __result = texture;
        return false;
    }

    internal static Texture2D? ResolvePackagedBetaPortrait(CardModel card)
    {
        if (ResourceLoader.Exists(card.PortraitPath)) return null;
        // Some cards ship only an imported beta image outside the portrait atlas.
        const string atlas = "res://images/atlases/card_atlas.sprites/";
        string beta = card.BetaPortraitPath;
        if (!beta.StartsWith(atlas, StringComparison.Ordinal) || !beta.EndsWith(".tres", StringComparison.Ordinal))
            return null;
        string image = "res://images/packed/card_portraits/" + beta[atlas.Length..^5] + ".png";
        return ResourceLoader.Exists(image) ? ResourceLoader.Load<Texture2D>(image) : null;
    }
}
