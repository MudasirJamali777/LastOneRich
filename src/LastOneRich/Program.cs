using LastOneRich.Core;

namespace LastOneRich;

public static class Program
{
    public static void Main(string[] args)
    {
        var launch = LaunchArgs.Parse(args);
        using var game = new LorGame(launch);
        game.Run();
    }
}
