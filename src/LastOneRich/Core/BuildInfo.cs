namespace LastOneRich.Core;

/// <summary>
/// The shipped build stamp, read once from <c>content/version.json</c> at boot.
///
/// Deliberately fail-soft: a missing, empty, unreadable or malformed version.json must never
/// stop the game from booting — it is a cosmetic corner label and a support aid ("what build
/// is the player running?"), not a dependency. Any failure leaves <see cref="Stamp"/> empty
/// and the main menu simply draws nothing, exactly as it did before this file existed. The
/// failure is written to saves/game.log so a support request can still answer the question.
///
/// <see cref="EnsureLoaded"/> is idempotent and marks itself loaded BEFORE it touches the
/// disk, so a throwing read can never turn into a retry-every-frame IO loop.
/// </summary>
public static class BuildInfo
{
    /// <summary>DTO for content/version.json — { "version": "1.0.0", "build": "release" }.</summary>
    public sealed class VersionDTO
    {
        public string Version { get; set; } = "";
        public string Build { get; set; } = "";
    }

    /// <summary>Semantic version from version.json, or "" when it could not be read.</summary>
    public static string Version { get; private set; } = "";

    /// <summary>Build channel ("release", "debug", "demo"...), or "" when it could not be read.</summary>
    public static string Build { get; private set; } = "";

    /// <summary>
    /// Pre-formatted one-line label for the menu corner, e.g. "V1.0.0 - RELEASE".
    /// Empty string means "nothing to show" — callers draw only when it is non-empty.
    /// Uppercase ASCII only, deliberately: content/gfx/font.json carries no glyph for the
    /// interpunct the rest of the UI likes, and a missing glyph renders as a blank gap.
    /// </summary>
    public static string Stamp { get; private set; } = "";

    static bool _loaded;

    /// <summary>Read version.json once. Safe to call from anywhere, at any time.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var dto = Json.Load<VersionDTO>("version.json");
            Version = (dto?.Version ?? "").Trim();
            Build = (dto?.Build ?? "").Trim();
        }
        catch (System.Exception e)
        {
            Version = "";
            Build = "";
            GameLog.Log($"[version] content/version.json unavailable ({e.GetType().Name}: {e.Message}) — no build stamp");
        }

        if (Version.Length == 0 && Build.Length == 0) Stamp = "";
        else if (Version.Length == 0) Stamp = Build.ToUpperInvariant();
        else if (Build.Length == 0) Stamp = "V" + Version;
        else Stamp = $"V{Version} - {Build.ToUpperInvariant()}";

        if (Stamp.Length > 0) GameLog.Log($"[version] {Stamp}");
    }
}
