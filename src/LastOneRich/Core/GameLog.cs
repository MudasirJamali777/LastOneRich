namespace LastOneRich.Core;

/// <summary>Best-effort runtime log for diagnosing a shipped build without a console window.</summary>
public static class GameLog
{
    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(SaveSystem.Dir);
            string line = $"{System.DateTime.UtcNow:o} {msg}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(SaveSystem.Dir, "game.log"), line);
        }
        catch { }
    }
}
