using Microsoft.Xna.Framework.Input;

namespace LastOneRich.Core;

/// <summary>
/// DTO for content/data/controls.json — remappable keyboard bindings plus camera/mouse tuning.
/// Every field has a default, so an old or partial controls.json still loads.
/// </summary>
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

    // ---- mouse-look tuning (radians per pixel of mouse movement) ----
    public double MouseSensitivity { get; set; } = 0.003;
    public bool InvertY { get; set; } = false;

    /// <summary>Chase camera boom length in world units.</summary>
    public double CameraDistance { get; set; } = 8.0;

    /// <summary>Orbit pivot height above the player's origin.</summary>
    public double CameraHeight { get; set; } = 2.5;
}

/// <summary>
/// Loads keybinds from controls.json once and resolves them to XNA Keys.
/// Invalid names are ignored; a missing file falls back to the defaults above.
/// Non-binding fields (sensitivity, camera distance...) are exposed via <see cref="Settings"/>.
/// </summary>
public static class Keybinds
{
    static readonly Dictionary<string, Keys[]> _cache = new();
    static bool _loaded;

    /// <summary>Parsed controls.json (or all-defaults if it is missing/broken). Never null after EnsureLoaded.</summary>
    public static ControlsDTO Settings { get; private set; } = new();

    // --- convenience accessors, clamped to sane ranges so a bad JSON value can't break the camera ---
    public static float MouseSensitivity =>
        (float)System.Math.Clamp(Settings.MouseSensitivity, 0.0002, 0.05);

    public static bool InvertY => Settings.InvertY;

    public static float CameraDistance =>
        (float)System.Math.Clamp(Settings.CameraDistance, 2.0, 30.0);

    public static float CameraHeight =>
        (float)System.Math.Clamp(Settings.CameraHeight, 0.2, 12.0);

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        ControlsDTO map;
        try { map = Json.Load<ControlsDTO>("data/controls.json") ?? new ControlsDTO(); }
        catch { map = new ControlsDTO(); }
        Settings = map;

        foreach (var prop in typeof(ControlsDTO).GetProperties())
        {
            // Only string[] properties are keybinds; scalars (sensitivity, distance...) are skipped.
            if (prop.PropertyType != typeof(string[])) continue;

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
