namespace LastOneRich.Core;

/// <summary>Persistent career data — save.json (GDD 17).</summary>
public sealed class SaveData
{
    public double TotalBanked { get; set; }
    public int SeasonsPlayed { get; set; }
    public int Championships { get; set; }
    public int CashOuts { get; set; }
    public int Eliminations { get; set; }
    public int BestFinish { get; set; } = 99;
}

public static class SaveSystem
{
    static string Dir()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData)) return Path.Combine(appData, "LastOneRich");
        }
        catch { }
        return AppContext.BaseDirectory;
    }

    static string PathFor() => Path.Combine(Dir(), "save.json");

    public static SaveData Load()
    {
        try
        {
            var p = PathFor();
            if (File.Exists(p))
                return System.Text.Json.JsonSerializer.Deserialize<SaveData>(File.ReadAllText(p), Json.Options) ?? new SaveData();
        }
        catch { }
        return new SaveData();
    }

    public static void Store(SaveData data)
    {
        try { Json.Save(PathFor(), data); } catch { /* best effort */ }
    }
}
