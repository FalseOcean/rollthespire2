using System;
using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using Godot;

public partial class Runner : Node
{
    public override void _Ready()
    {
        int result = 1;
        try
        {
            string check = System.Environment.GetEnvironmentVariable("RT2_NATIVE_CHECK") ?? "--authored-morphic";
            var mod = Assembly.Load("RolltheSpire2");
            string location = mod.Location;
            string hash = string.IsNullOrEmpty(location) ? "unavailable:byte-loaded" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location)));
            Console.WriteLine($"Host=AuthoredEventChecks;check={check};mod={(string.IsNullOrEmpty(location) ? "unavailable:byte-loaded" : location)};modMvid={mod.ManifestModule.ModuleVersionId};modSha256={hash}");
            if (System.Environment.GetEnvironmentVariable("RT2_NATIVE_GAME_PCK") is { Length: > 0 } pck &&
                !ProjectSettings.LoadResourcePack(pck, replaceFiles: true))
                throw new InvalidOperationException("Native check resource pack unavailable");
            System.Environment.SetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER", "1");
            var assembly = Assembly.Load("Rt2FunctionalChecks");
            result = (int)assembly.EntryPoint!.Invoke(null, new object[] { new[] { check } })!;
        }
        catch (Exception error) { Console.Error.WriteLine(error); }
        Console.WriteLine($"Host=AuthoredEventChecks;exitCode={result}");
        GetTree().Quit(result);
    }
}
