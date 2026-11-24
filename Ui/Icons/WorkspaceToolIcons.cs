using Godot;

namespace RolltheSpire2.Ui.Icons;

/// <summary>Small, consistent line icons for software tools; game objects keep native art.</summary>
internal static class WorkspaceToolIcons
{
    private static readonly Dictionary<string, Texture2D> Cache = [];

    internal static Texture2D Get(string key)
    {
        if (Cache.TryGetValue(key, out var texture)) return texture;
        string shape = key switch
        {
            "search" => "<circle cx='10' cy='10' r='6'/><path d='m15 15 6 6'/>",
            "analysis" => "<path d='M4 4v16h16M8 15v-4m5 4V7m5 8v-6'/>",
            "crystal" => "<circle cx='12' cy='10' r='7'/><path d='M7 20h10M9 17l-2 3m8-3 2 3'/>",
            "library" => "<path d='M5 3h14v18l-7-4-7 4zM9 7h6'/>",
            "book" => "<path d='M12 5Q7 2 3 4v15q5-2 9 1 4-3 9-1V4q-4-2-9 1v15'/>",
            "status" => "<path d='M3 13h4l3-8 4 14 3-8h4'/>",
            "about" => "<circle cx='12' cy='12' r='9'/><path d='M12 11v6m0-10v.2'/>",
            "feedback" => "<path d='M4 4h16v12H9l-5 4zM8 8h8M8 12h5'/>",
            "settings" => "<path d='M4 6h16M4 12h16M4 18h16'/><circle cx='9' cy='6' r='2'/><circle cx='16' cy='12' r='2'/><circle cx='8' cy='18' r='2'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown workspace tool icon")
        };
        using var image = new Image();
        var error = image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><g fill='none' stroke='white' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'>" + shape + "</g></svg>");
        if (error != Error.Ok) throw new InvalidOperationException("Workspace tool icon could not load: " + key);
        return Cache[key] = ImageTexture.CreateFromImage(image);
    }
}
