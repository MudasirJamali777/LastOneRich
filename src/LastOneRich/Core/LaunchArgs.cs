namespace LastOneRich.Core;

public sealed class LaunchArgs
{
    public string ShotPath;    // if set, save a screenshot at ShotFrame then exit
    public int ShotFrame = -1;
    public string Goto;        // dev shortcut: menu | intro | game (skips straight there)

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
                case "goto": a.Goto = val.ToLowerInvariant(); break;
                case "shot-frame": int.TryParse(val, out a.ShotFrame); break;
            }
        }
        return a;
    }
}
