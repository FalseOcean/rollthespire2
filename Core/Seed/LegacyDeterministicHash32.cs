namespace RolltheSpire2.Core.Seed;

/// <summary>
/// Pure mirror of the audited STS2 0.107.1 deterministic string hash.
/// It intentionally has no dependency on the game's StringHelper implementation.
/// </summary>
public static class LegacyDeterministicHash32
{
    public static uint Hash(string value)
    {
        string text = value ?? string.Empty;
        unchecked
        {
            uint first = 352654597u;
            uint second = first;
            for (int index = 0; index < text.Length; index += 2)
            {
                first = (first * 33u) ^ text[index];
                if (index == text.Length - 1)
                {
                    break;
                }

                second = (second * 33u) ^ text[index + 1];
            }

            return first + second * 1566083941u;
        }
    }
}
