using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Presentation.ContentNames;

/// <summary>
/// Selects the user-facing content name without allowing an embedded catalog
/// fallback to pre-empt a name copied from the live game's localization model.
/// This policy is pure and worker-safe; the runtime model lookup itself remains
/// a main-thread Infrastructure responsibility.
/// </summary>
public static class GameContentNameResolutionPolicy
{
    public static string PreferRuntimeLocalizedName(
        ModelKey key,
        string? runtimeLocalizedName,
        string? fallbackName)
    {
        if (IsUsefulRuntimeName(runtimeLocalizedName, key))
        {
            return runtimeLocalizedName!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            return fallbackName.Trim();
        }

        return key.IsValid ? key.Entry : key.Serialized;
    }

    public static bool IsUsefulRuntimeName(string? value, ModelKey key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        if (string.Equals(text, key.Entry, StringComparison.Ordinal) ||
            string.Equals(text, key.Serialized, StringComparison.Ordinal))
        {
            return false;
        }

        return !(text.All(character =>
                    char.IsUpper(character) ||
                    char.IsDigit(character) ||
                    character == '_') &&
                 text.Contains('_'));
    }
}
