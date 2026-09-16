using Microsoft.Xna.Framework.Input;

namespace LastOneRich.Core;

/// <summary>
/// DTO for content/data/controls.json — remappable keyboard bindings plus camera/mouse tuning,
/// and (Priority 3) the graphics / audio / gameplay knobs the Settings menu exposes.
/// Every field has a default, so an old or partial controls.json still loads.
///
/// NOTE: these must stay auto-properties. The merged state is round-tripped to
/// saves/settings.json with the default serializer, which only sees properties — never fields.
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

    // Advanced look tuning: not exposed as menu rows (the spec's four tabs stay uncluttered),
    // but they live in the same file so a hand-edit does what it says.
    /// <summary>Lowest the boom may drop under the player (negative = looking down at them).</summary>
    public double PitchMinDeg { get; set; } = -25.0;

    /// <summary>Highest the boom may rise over the player.</summary>
    public double PitchMaxDeg { get; set; } = 60.0;

    // ---- graphics (applied by LorGame at startup; live via SettingsStore.MarkPending) ----

    /// <summary>Back-buffer width. The UI canvas is virtual (1280x720) so any value scales cleanly.</summary>
    public int ResolutionWidth { get; set; } = 1280;

    /// <summary>Back-buffer height.</summary>
    public int ResolutionHeight { get; set; } = 720;

    public bool Fullscreen { get; set; } = false;

    /// <summary>When false the frame rate is uncapped (fixed 60 Hz simulation still runs at 60).</summary>
    public bool VSync { get; set; } = true;

    /// <summary>Vertical field of view in degrees — clamped to the Settings range (50..100).</summary>
    public double FovDeg { get; set; } = 62.0;

    /// <summary>Depth fog matched to the sky clear color.</summary>
    public bool FogEnabled { get; set; } = true;

    // ---- audio (0..100, applied live through AudioBank's static volumes) ----
    public int MasterVolume { get; set; } = 100;
    public int MusicVolume { get; set; } = 45;
    public int SfxVolume { get; set; } = 100;

    // ---- gameplay ----

    /// <summary>CASUAL / STANDARD / HARDCORE. Unknown values read as STANDARD.</summary>
    public string Difficulty { get; set; } = "STANDARD";
}

/// <summary>
/// Loads keybinds from controls.json once, overlays the player's saved settings
/// (saves/settings.json), and resolves the bindings to XNA Keys.
/// Invalid names are ignored; a missing file falls back to the defaults above.
/// Non-binding fields (sensitivity, camera distance, fov, volume...) are exposed via <see cref="Settings"/>
/// and the clamped accessors below — nothing else in the game should read the raw doubles.
/// </summary>
public static class Keybinds
{
    static readonly Dictionary<string, Keys[]> _cache = new();
    static bool _loaded;

    /// <summary>Parsed controls.json overlaid with saves/settings.json (or all-defaults if both are missing/broken). Never null after EnsureLoaded.</summary>
    public static ControlsDTO Settings { get; private set; } = new();

    // --- convenience accessors, clamped to sane ranges so a bad JSON value can't break the camera ---
    public static float MouseSensitivity =>
        (float)System.Math.Clamp(Settings.MouseSensitivity, 0.0002, 0.05);

    public static bool InvertY => Settings.InvertY;

    public static float CameraDistance =>
        (float)System.Math.Clamp(Settings.CameraDistance, 2.0, 30.0);

    public static float CameraHeight =>
        (float)System.Math.Clamp(Settings.CameraHeight, 0.2, 12.0);

    // --- Priority 3 accessors (same clamp-everything discipline as above) ---
    public static int ResolutionWidth =>
        (int)System.Math.Clamp(Settings.ResolutionWidth, 640, 3840);

    public static int ResolutionHeight =>
        (int)System.Math.Clamp(Settings.ResolutionHeight, 360, 2160);

    public static bool Fullscreen => Settings.Fullscreen;
    public static bool VSync => Settings.VSync;

    /// <summary>Clamped to the Settings slider range so a hand-edited JSON can't give a fisheye or a telephoto.</summary>
    public static float FovDeg =>
        (float)System.Math.Clamp(Settings.FovDeg, 50.0, 100.0);

    public static bool FogEnabled => Settings.FogEnabled;

    /// <summary>0..1 master fader — every sound in the game passes through it.</summary>
    public static float MasterVolume =>
        (float)System.Math.Clamp(Settings.MasterVolume, 0, 100) / 100f;

    public static float MusicVolume =>
        (float)System.Math.Clamp(Settings.MusicVolume, 0, 100) / 100f;

    public static float SfxVolume =>
        (float)System.Math.Clamp(Settings.SfxVolume, 0, 100) / 100f;

    public static string Difficulty =>
        (Settings.Difficulty ?? "STANDARD").Trim().ToUpperInvariant() switch
        {
            "CASUAL" => "CASUAL",
            "HARDCORE" => "HARDCORE",
            _ => "STANDARD",
        };

    /// <summary>Rival pace multiplier (player speed is never touched — that would change the physics feel).</summary>
    public static float DifficultyBotPaceMult => Difficulty switch
    {
        "CASUAL" => 0.86f,
        "HARDCORE" => 1.10f,
        _ => 1f,
    };

    /// <summary>Drone patrol speed multiplier for StrikesOut rounds.</summary>
    public static float DifficultyHazardMult => Difficulty switch
    {
        "CASUAL" => 0.85f,
        "HARDCORE" => 1.15f,
        _ => 1f,
    };

    /// <summary>
    /// Re-resolve everything from a settings object (used by the Settings menu after an edit,
    /// so changes take effect without a restart). Rebuilds the key cache from scratch — a stale
    /// keybind would otherwise keep working after being remapped.
    /// </summary>
    public static void Apply(ControlsDTO s)
    {
        if (s == null) return;
        Settings = s;
        _cache.Clear();
        foreach (var prop in typeof(ControlsDTO).GetProperties())
        {
            // Only string[] properties are keybinds; scalars (sensitivity, distance...) are skipped.
            if (prop.PropertyType != typeof(string[])) continue;

            var names = prop.GetValue(s) as string[] ?? System.Array.Empty<string>();
            var keys = new List<Keys>();
            foreach (var n in names)
                if (System.Enum.TryParse<Keys>(n, ignoreCase: true, out var k) && k != Keys.None)
                    keys.Add(k);
            _cache[prop.Name] = keys.ToArray();
        }
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        ControlsDTO map;
        try { map = Json.Load<ControlsDTO>("data/controls.json") ?? new ControlsDTO(); }
        catch { map = new ControlsDTO(); }

        // Player preferences win over the shipped defaults. _loaded is already true, so a settings
        // read can never bounce back in here.
        SettingsStore.LoadInto(map);
        Apply(map);
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

/// <summary>
/// Persistent player settings (Priority 3): saves/settings.json next to the exe, same location
/// convention as SaveSystem. This is not a new DTO — it stores the existing <see cref="ControlsDTO"/>
/// merged with the shipped controls.json defaults, so the file always stands on its own
/// (delete it and the game falls back to content/data/controls.json, then to code defaults).
///
/// Every entry point is best-effort and never throws: a missing, locked or corrupt file simply
/// means "no overrides".
/// </summary>
public static class SettingsStore
{
    /// <summary>Set when resolution / fullscreen changed; LorGame applies and clears it in Update.</summary>
    public static bool PendingGraphics { get; set; }
    public static bool LoadFailed { get; private set; }

    /// <summary>
    /// --safe: read no settings file and write none, so a saved mode that this machine cannot
    /// display is never able to lock the player out of booting the game. The file on disk is left
    /// exactly as it was, so the next normal launch retries it.
    /// </summary>
    public static bool IgnoreOverrides { get; set; }

    static string File() => System.IO.Path.Combine(SaveSystem.Dir, "settings.json");
    static bool _saving;

    /// <summary>
    /// Overlay saves/settings.json onto an already-defaulted object (startup path: callers load
    /// controls.json first, then call this). Keeps the caller's values when the file is
    /// missing/broken, and guards against re-entering through Keybinds.EnsureLoaded.
    /// </summary>
    public static void LoadInto(ControlsDTO target)
    {
        if (target == null || IgnoreOverrides) return;
        try
        {
            var p = File();
            if (!System.IO.File.Exists(p)) return;
            var o = System.Text.Json.JsonSerializer.Deserialize<ControlsDTO>(System.IO.File.ReadAllText(p), Json.Options);
            if (o != null) Overlay(o, target);
        }
        catch { LoadFailed = true; }
    }

    /// <summary>Full rewrite of the merged state; the UI calls this after every edit and on close.</summary>
    public static void Persist(ControlsDTO s)
    {
        if (s == null || _saving || IgnoreOverrides) return;   // --safe: session-only, never written
        _saving = true;
        try
        {
            System.IO.Directory.CreateDirectory(SaveSystem.Dir);
            System.IO.File.WriteAllText(File(), System.Text.Json.JsonSerializer.Serialize(s, Json.WriteOptions));
        }
        catch { /* best effort, same policy as SaveSystem.Store */ }
        finally { _saving = false; }
    }

    public static void MarkPending() => PendingGraphics = true;

    // ---- boot safety net ---------------------------------------------------------------------
    // A saved resolution / fullscreen mode this machine cannot display would throw inside
    // base.Initialize() — before any UI exists to fix it with, so the game would just crash on
    // every launch. Instead: the run that APPLIES non-default graphics arms a probe file, and the
    // first successful Draw clears it. A launch that still finds the probe knows the previous
    // attempt never reached a frame, and boots once with the overrides ignored (self-healing,
    // same effect as --safe). One file write + one delete per launch, worst case.
    static string ProbeFile() => System.IO.Path.Combine(SaveSystem.Dir, ".boot-probe");
    static bool _probeArmed;

    /// <summary>True when the settings about to be applied could plausibly break device creation.</summary>
    public static bool GraphicsDifferFromSafeDefaults() =>
        Keybinds.Fullscreen || Keybinds.ResolutionWidth != 1280 || Keybinds.ResolutionHeight != 720;

    public static bool BootProbeStale()
    {
        try { return System.IO.File.Exists(ProbeFile()); } catch { return false; }
    }

    public static void ArmBootProbe()
    {
        if (IgnoreOverrides) return;
        try
        {
            System.IO.Directory.CreateDirectory(SaveSystem.Dir);
            System.IO.File.WriteAllText(ProbeFile(), "armed");
            _probeArmed = true;
        }
        catch { }
    }

    /// <summary>First Draw of a good launch. Guarded, so frames 2..N never touch the disk.</summary>
    public static void ClearBootProbe()
    {
        if (!_probeArmed) return;
        _probeArmed = false;
        try { System.IO.File.Delete(ProbeFile()); } catch { }
    }

    /// <summary>Drops a probe left behind by a launch that died before its first frame.</summary>
    public static void ClearStaleProbe()
    {
        try { if (System.IO.File.Exists(ProbeFile())) System.IO.File.Delete(ProbeFile()); } catch { }
    }

    /// <summary>
    /// Push the pending resolution / fullscreen / VSync choices into the GraphicsDeviceManager.
    /// Called by LorGame.Update — i.e. outside any begin/end draw pair, which is the only safe
    /// place to reset the device mid-run. Fullscreen still cannot flip live without a restart
    /// (SDL owns the swapchain), which is why the menu shows the Apply &amp; Restart banner.
    /// </summary>
    public static void ApplyPendingGraphics(Microsoft.Xna.Framework.GraphicsDeviceManager g)
    {
        if (!PendingGraphics || g == null) return;
        PendingGraphics = false;
        try
        {
            g.IsFullScreen = Keybinds.Fullscreen;
            g.SynchronizeWithVerticalRetrace = Keybinds.VSync;
            g.PreferredBackBufferWidth = Keybinds.ResolutionWidth;
            g.PreferredBackBufferHeight = Keybinds.ResolutionHeight;
            g.ApplyChanges();
        }
        catch { }
    }

    /// <summary>
    /// Copy every property of <paramref name="from"/> onto <paramref name="to"/> by reflection, so
    /// new settings fields are persisted/overlaid automatically.
    ///
    /// Why not rebind: the menu edits Keybinds.Settings in place. If saving reassigned the
    /// reference, the very next write would serialise the pre-edit object and lose that change.
    ///
    /// Keybind arrays (string[]) are skipped when absent from the file: settings.json is written
    /// before any hand-edit of controls.json may happen, and losing a binding to an absent key
    /// would silently fall back to the code default instead of the shipped controls.json value.
    /// </summary>
    static void Overlay(ControlsDTO from, ControlsDTO to)
    {
        foreach (var prop in typeof(ControlsDTO).GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            var v = prop.GetValue(from);
            if (v == null && prop.PropertyType == typeof(string[])) continue;
            prop.SetValue(to, v);
        }
    }
}
