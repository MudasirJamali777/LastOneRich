using Microsoft.Xna.Framework;

namespace LastOneRich.Core;

/// <summary>
/// The game's visual language (graphics pass). Every object category has ONE color
/// identity so players instantly read danger / safety / interactivity.
/// Never hardcode ad-hoc colors in draw calls — route them through here.
/// </summary>
public static class ColorPalette
{
    // -- environment --
    public static readonly Color Floor = new(0x1E, 0x1E, 0x24);        // dark charcoal ground
    public static readonly Color Wall = new(0x3A, 0x3A, 0x4A);         // barriers / maze walls
    public static readonly Color Boundary = new(0x2E, 0x2E, 0x3E);     // arena boundary
    public static readonly Color GridLine = new(0x2A, 0x2A, 0x35);     // floor grid (depth cue)
    public static readonly Color EdgeWarn = new(0x7A, 0x52, 0x16);     // dark amber curb on ledges
    public static readonly Color Sky = new(15, 15, 25);                // near-black blue void

    // -- safe vs danger --
    public static readonly Color Safe = new(0x2E, 0xCC, 0x40);         // finish / safe zone green
    public static readonly Color DangerOrange = new(0xFF, 0x6A, 0x00); // hammers
    public static readonly Color DangerRed = new(0x8B, 0x00, 0x00);    // break/danger tiles
    public static readonly Color WindBlue = new(0x00, 0xAA, 0xFF);     // wind cannons
    public static readonly Color ConveyorYellow = new(0xFF, 0xD7, 0x00);
    public static readonly Color IceCyan = new(0xA8, 0xEF, 0xFF);
    public static readonly Color SafeTileTeal = new(0x00, 0xBF, 0xA5); // glass-path safe stones
    public static readonly Color CashGold = new(0xFF, 0xD7, 0x00);     // heist bricks / vault

    // -- characters --
    public static readonly Color Player = new(0xFF, 0x30, 0x30);
    public static readonly Color DroneBody = new(0xE0, 0xE0, 0xE0);
    public static readonly Color DroneScan = new(255, 60, 80);

    /// <summary>Fixed rival identity colors (bots.json mirrors these).</summary>
    public static readonly System.Collections.Generic.Dictionary<string, Color> Bots = new()
    {
        ["NOVA"] = new Color(0xA0, 0x20, 0xF0),
        ["JAX"] = new Color(0xFF, 0x8C, 0x00),
        ["MIRA"] = new Color(0x00, 0xCE, 0xD1),
        ["TANK"] = new Color(0x70, 0x80, 0x90),
        ["LUXE"] = new Color(0xFF, 0xD7, 0x00),
        ["PIXEL"] = new Color(0xFF, 0x69, 0xB4),
    };

    /// <summary>Twist severity color: yellow = minor, orange = medium, red = harsh.</summary>
    public static Color TwistSeverity(double maxAbsLogMult)
        => maxAbsLogMult switch
        {
            < 0.16 => new Color(255, 210, 63),
            < 0.30 => new Color(255, 157, 46),
            _ => new Color(255, 70, 70),
        };
}
