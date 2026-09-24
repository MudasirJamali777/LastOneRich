namespace LastOneRich.Core;

/// <summary>
/// Persistent career data — save.json (GDD 17).
///
/// Priority 4: versioned schema + migration, richer career stats, and a durable
/// achievement list (Priority 5 hangs its unlock state here so trophies survive
/// uninstalls/crashes exactly like money does).
/// </summary>
public sealed class SaveData
{
    /// <summary>Schema version. Load() migrates anything older forward (see SaveSystem.Migrate).</summary>
    public int Version { get; set; } = SaveSystem.CurrentVersion;

    // ---- career totals (schema v1) ----
    public double TotalBanked { get; set; }
    public int SeasonsPlayed { get; set; }
    public int Championships { get; set; }
    public int CashOuts { get; set; }
    public int Eliminations { get; set; }
    public int BestFinish { get; set; } = 99;

    // ---- richer career stats (schema v2) ----
    public int RoundsPlayed { get; set; }
    public int RoundsWon { get; set; }
    public int Podiums { get; set; }
    public double BestBank { get; set; }               // largest bank carried out of a season
    public string LastPlayedUtc { get; set; } = "";

    /// <summary>Unlocked achievement ids (Priority 5). Ids are stable; see Achievements.All.</summary>
    public List<string> Achievements { get; set; } = new();

    /// <summary>
    /// Priority 7: has the player clicked through the first-run welcome/difficulty screen?
    /// Defaults false so a save.json written before this field existed (schema v2) still shows
    /// the welcome screen exactly once, the same as a brand-new career would.
    /// </summary>
    public bool WelcomeSeen { get; set; }
}

public static class SaveSystem
{
    /// <summary>Current schema version of <see cref="SaveData"/>.</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// saves/ next to the executable — keeps the Steam build self-contained and portable.
    /// Public so sibling stores (SettingsStore) share one location convention instead of re-deriving it.
    /// </summary>
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "saves");

    static string PathFor() => Path.Combine(Dir, "save.json");
    static string BackupFor() => Path.Combine(Dir, "save.json.bak");
    static string TempFor() => Path.Combine(Dir, "save.json.tmp");

    /// <summary>
    /// Priority 7: true when a career file already exists on disk (main OR backup — either one
    /// means this is a returning player, not a first launch). Deliberately a cheap existence
    /// check rather than a full Load(): BootState calls this before GameServices.Save even
    /// exists, and existence is all a first-run decision needs.
    /// </summary>
    public static bool HasSave()
    {
        try { return File.Exists(PathFor()) || File.Exists(BackupFor()); }
        catch { return false; }
    }

    /// <summary>
    /// Load the career save. Priority 4 hardening:
    ///  - the main file is validated AND migrated (old v1 saves keep every stat),
    ///  - a corrupt/missing main file falls back to save.json.bak (the previous good write),
    ///  - only if both are unreadable do we start a fresh career. Never throws.
    /// </summary>
    public static SaveData Load()
    {
        foreach (var candidate in new[] { PathFor(), BackupFor() })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var d = System.Text.Json.JsonSerializer.Deserialize<SaveData>(File.ReadAllText(candidate), Json.Options);
                if (d != null) return Migrate(d);
            }
            catch { /* fall through to the backup, then to a fresh career */ }
        }
        return new SaveData();
    }

    /// <summary>
    /// Atomic write: serialise to save.json.tmp, back the CURRENT save up to save.json.bak,
    /// then rename the temp over the target. A crash mid-write can only ever lose the temp
    /// file — the on-disk save is always the last complete one (GDD 17 durability).
    /// </summary>
    public static void Store(SaveData data)
    {
        if (data == null) return;
        try
        {
            data.LastPlayedUtc = System.DateTime.UtcNow.ToString("o");
            Directory.CreateDirectory(Dir);
            Json.Save(TempFor(), data);
            var target = PathFor();
            if (File.Exists(target))
                try { File.Copy(target, BackupFor(), true); } catch { /* backup is best-effort */ }
            File.Move(TempFor(), target, true);
        }
        catch { /* best effort, same policy as before */ }
    }

    /// <summary>Best-effort atomic crash report containing the UTC timestamp and full exception chain.</summary>
    public static void WriteCrashLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string temp = Path.Combine(Dir, "crash.log.tmp");
            string target = Path.Combine(Dir, "crash.log");
            string text = $"Crash timestamp (UTC): {System.DateTime.UtcNow:o}{Environment.NewLine}{Environment.NewLine}{exception}{Environment.NewLine}";
            File.WriteAllText(temp, text);
            File.Move(temp, target, true);
        }
        catch { /* crash reporting must never hide the original failure */ }
    }

    static SaveData Migrate(SaveData d)
    {
        // v1 -> v2: the v2 fields are absent from old files, so they arrive as CLR defaults —
        // nothing needs moving. Stamp the version so the next Store writes the new schema.
        if (d.Version < 1) d.Version = 1;
        if (d.Version < CurrentVersion) d.Version = CurrentVersion;
        Validate(d);
        return d;
    }

    /// <summary>Clamp nonsense values (hand-edited or half-corrupt files degrade, never crash).</summary>
    static void Validate(SaveData d)
    {
        if (d.TotalBanked < 0) d.TotalBanked = 0;
        if (d.SeasonsPlayed < 0) d.SeasonsPlayed = 0;
        if (d.Championships < 0) d.Championships = 0;
        if (d.CashOuts < 0) d.CashOuts = 0;
        if (d.Eliminations < 0) d.Eliminations = 0;
        if (d.BestFinish <= 0) d.BestFinish = 99;
        if (d.RoundsPlayed < 0) d.RoundsPlayed = 0;
        if (d.RoundsWon < 0) d.RoundsWon = 0;
        if (d.Podiums < 0) d.Podiums = 0;
        if (d.BestBank < 0) d.BestBank = 0;
        d.Achievements ??= new List<string>();
        d.LastPlayedUtc ??= "";
    }
}
