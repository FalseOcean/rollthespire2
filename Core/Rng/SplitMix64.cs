namespace RolltheSpire2.Core.Rng;

public static class SplitMix64
{
    public static ulong Next(ref ulong state)
    {
        unchecked
        {
            ulong result = state += 11400714819323198485UL;
            result = (result ^ (result >> 30)) * 13787848793156543929UL;
            result = (result ^ (result >> 27)) * 10723151780598845931UL;
            return result ^ (result >> 31);
        }
    }
}
