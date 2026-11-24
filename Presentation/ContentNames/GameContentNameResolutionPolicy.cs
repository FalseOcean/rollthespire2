using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Presentation.ContentNames;

/// <summary>
/// Selects the live game's name or the full original ID when no useful name exists.
/// This policy is pure and worker-safe; the runtime model lookup itself remains
/// a main-thread Infrastructure responsibility.
/// </summary>
public static class GameContentNameResolutionPolicy
{
    public static string PreferRuntimeLocalizedName(
        ModelKey key,
        string? runtimeLocalizedName)
    {
        if (IsUsefulRuntimeName(runtimeLocalizedName, key))
        {
            return runtimeLocalizedName!.Trim();
        }

        return key.Serialized;
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
