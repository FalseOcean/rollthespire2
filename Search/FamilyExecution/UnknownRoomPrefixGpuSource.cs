namespace RolltheSpire2.Search.FamilyExecution;

// Event requests keep the original shader source and parameters. Monster uses
// the same RNG/ordinal transport with a specialized acceptance threshold.
internal static class UnknownRoomPrefixGpuSource
{
    internal static string RequireMonster(string source, int flagOffset)
    {
        string flag = $"bool juzu=plan.v[{flagOffset}]!=0u;";
        const string predicate = "if(next_float()<=total)return false;";
        if (!source.Contains(flag, StringComparison.Ordinal) || !source.Contains(predicate, StringComparison.Ordinal))
            throw new InvalidOperationException("UnknownRoomMonster.ShaderSeam");
        return source.Replace(flag, flag + "if(juzu)return false;", StringComparison.Ordinal)
            .Replace(predicate, "if(next_float()>0.1)return false;", StringComparison.Ordinal);
    }
}
