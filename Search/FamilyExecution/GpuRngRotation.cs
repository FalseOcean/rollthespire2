namespace RolltheSpire2.Search.FamilyExecution;

// Physical GPU implementation identity; RNG state, draw order and output stay unchanged.
internal static class GpuRngRotation
{
    internal const string Revision = ".Rotate45.SplitUInt32.20261028.v1";
    internal const string SourceMarker = "/*__RT2_XOSHIRO_ROTATE45__*/";
    internal const string ShaderResourceSuffix = "XoshiroRotate45.glsl";
}
