using Microsoft.Xna.Framework.Input;

namespace LastOneRich.Core;

/// <summary>DTO for content/data/controls.json — remappable keyboard bindings.</summary>
public sealed class ControlsDTO
{
    public string[] MoveLeft { get; set; } = { "A", "Left" };
    public string[] MoveRight { get; set; } = { "D", "Right" };
    public string[] MoveForward { get; set; } = { "W", "Up" };
    public string[] MoveBack { get; set; } = { "S", "Down" };
    public string[] Jump { get; set; } = { "Space" };
    public string[] Dive { get; set; } = { "LeftShift", "RightShift" };
    public string[] Confirm { get; set; } = { "Enter", "Space", "E" };
    public string[] Pause { get; set; } = { "Escape" };
    public string[] DebugOverlay { get; set; } = { "F3" };
}

/// <summary>
/// Loads keybinds from controls.json once and resolves them to XNA Keys.
/// Invalid names are ignored; a missing file falls back to the defaults above.
/// </summary>
public static class Keybinds
{
    static readonly Dictionary<string, Keys[]> _cache = new();
    static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        ControlsDTO map;
        try { map = Json.Load<ControlsDTO>("data/controls.json"); }
        catch { map = new ControlsDTO(); }

        foreach (var prop in typeof(ControlsDTO).GetProperties())
        {
            var names = prop.GetValue(map) as string[] ?? System.Array.Empty<string>();
            var keys = new List<Keys>();
            foreach (var n in names)
                if (System.Enum.TryParse<Keys>(n, ignoreCase: true, out var k) && k != Keys.None)
                    keys.Add(k);
            _cache[prop.Name] = keys.ToArray();
        }
    }

    public static Keys[] KeysFor(string action)
    {
        EnsureLoaded();
        return _cache.TryGetValue(action, out var k) ? k : System.Array.Empty<Keys>();
    }

    public static bool Held(KeyboardState cur, string action)
    {
        foreach (var k in KeysFor(action)) if (cur.IsKeyDown(k)) return true;
        return false;
    }

    public static bool Pressed(KeyboardState cur, KeyboardState prev, string action)
    {
        foreach (var k in KeysFor(action)) if (cur.IsKeyDown(k) && !prev.IsKeyDown(k)) return true;
        return false;
    }
}
