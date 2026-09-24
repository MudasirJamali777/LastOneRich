using LastOneRich.Core;
using System.Runtime.InteropServices;

namespace LastOneRich;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Main(string[] args)
    {
        try
        {
            var launch = LaunchArgs.Parse(args);
            using var game = new LorGame(launch);
            game.Run();
        }
        catch (Exception ex)
        {
            SaveSystem.WriteCrashLog(ex);
            ShowCrashMessage();
        }
    }

    static void ShowCrashMessage()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            MessageBoxW(IntPtr.Zero,
                "LAST ONE RICH crashed. A crash report was written to saves/crash.log.",
                "LAST ONE RICH",
                0x10);
        }
        catch { }
    }
}
