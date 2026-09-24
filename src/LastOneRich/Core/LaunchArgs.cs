namespace LastOneRich.Core;

public sealed class LaunchArgs
{
    public string ShotPath;    // if set, save a screenshot at ShotFrame then exit
    public int ShotFrame = -1;

#if DEBUG
    public string Goto;        // dev shortcut: menu | intro | game (skips straight there)
    public bool Overlay;       // start with the F3 debug overlay visible
    public bool AchieveAll;    // dev shortcut: unlock every achievement on boot
    public int Round = 1;      // dev shortcut with --goto=game: start at this season round

    /// <summary>
    /// --safe: ignore saves/settings.json's resolution / fullscreen for this run. A bad saved
    /// mode should never be able to lock a player (or a reviewer) out of the game; settings.json
    /// stays untouched, so the next normal launch retries it.
    /// </summary>
    public bool SafeMode;
#endif

    public static LaunchArgs Parse(string[] args)
    {
        var a = new LaunchArgs();
        foreach (var raw in args)
        {
            var arg = raw;
            int eq = arg.IndexOf('=');
            string val = eq >= 0 ? arg[(eq + 1)..] : "";
            string key = (eq >= 0 ? arg[..eq] : arg).TrimStart('-').ToLowerInvariant();
            switch (key)
            {
                case "shot": a.ShotPath = val; break;
                case "shot-frame": int.TryParse(val, out a.ShotFrame); break;
#if DEBUG
                case "goto": a.Goto = val.ToLowerInvariant(); break;
                case "overlay": a.Overlay = true; break;
                case "achieve-all": a.AchieveAll = true; break;
                case "safe": a.SafeMode = true; break;
                case "round": int.TryParse(val, out a.Round); break;
#endif
            }
        }
        return a;
    }
}
