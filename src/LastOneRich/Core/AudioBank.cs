using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>Loads placeholder WAVs at runtime (SoundEffect.FromStream — no Content Pipeline), loops music.</summary>
public sealed class AudioBank
{
    readonly Dictionary<string, SoundEffect> _sfx = new();
    readonly List<MemoryStream> _keepAlive = new();
    SoundEffectInstance _music;

    public bool Enabled { get; private set; } = true;

    // ---- Priority 3: volume knobs driven by the Settings menu (0..1) ----
    // Read on every play, so a slider change is audible on the very next blip — no restart,
    // no re-load of clips. Defaults reproduce the pre-settings behaviour exactly
    // (sfx kept its historical 0.9 headroom dip, music its 0.45 bed level).
    public static float MasterVolume = 1f;
    public static float MusicVolume = 0.45f;
    public static float SfxVolume = 0.9f;

    /// <summary>
    /// The live bank, so the STATIC fader push can reach the one thing statics cannot: the
    /// already-playing music instance. Single-instance service, same idea as GameServices.
    /// </summary>
    static AudioBank _current;

    /// <summary>
    /// Push the stored settings into the three faders and onto the playing music bed.
    /// Safe to call at any time, including before LoadContent (then it only sets the statics).
    /// </summary>
    public static void ApplyVolumes()
    {
        MasterVolume = Keybinds.MasterVolume;
        MusicVolume = Keybinds.MusicVolume;
        SfxVolume = (Keybinds.SfxVolume / 100f) * 0.9f;
        _current?.PushMusicVolume();
    }

    void PushMusicVolume()
    {
        if (_music == null) return;
        try { _music.Volume = BedVolume * MusicVolume * MasterVolume; } catch { }
    }

    /// <summary>The music bed level set by the last PlayMusic call (defaults to its 0.45 argument).</summary>
    float BedVolume = 0.45f;

    public AudioBank(string dir)
    {
        _current = this;    // lets the static fader push reach this bank's playing music
        try
        {
            foreach (var path in Directory.GetFiles(dir, "*.wav"))
            {
                var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var ms = new MemoryStream(File.ReadAllBytes(path), false);
                _keepAlive.Add(ms);
                _sfx[name] = SoundEffect.FromStream(ms);
            }
        }
        catch
        {
            Enabled = false; // no audio device (CI/container) — run silent
        }
    }

    public bool Has(string name) => _sfx.ContainsKey(name.ToLowerInvariant());

    public void Play(string name, float volume = 1f, float pitch = 0f, float pan = 0f)
    {
        if (!Enabled) return;
        try
        {
            if (_sfx.TryGetValue(name.ToLowerInvariant(), out var sfx))
                sfx.Play(volume * SfxVolume * MasterVolume, MathHelper.Clamp(pitch, -1f, 1f), pan);
        }
        catch { Enabled = false; } // device vanished / never existed — go silent
    }

    public void PlayMusic(float volume = 0.45f)
    {
        if (!Enabled) return;
        BedVolume = volume;   // remember the caller's bed so a later fader change keeps it
        ApplyVolumes();       // stays in sync with the Settings menu even if a caller passes a volume
        if (_music == null && _sfx.TryGetValue("music_loop", out var m))
        {
            _music = m.CreateInstance();
            _music.IsLooped = true;
        }
        if (_music != null)
        {
            try
            {
                // BedVolume = the caller's bed level; MusicVolume is the player's music fader,
                // MasterVolume the global one (0.45 and 1 respectively = the old behaviour).
                _music.Volume = volume * MusicVolume * MasterVolume;
                if (_music.State != Microsoft.Xna.Framework.Audio.SoundState.Playing) _music.Play();
            }
            catch { Enabled = false; }
        }
    }

    public void StopMusic() { if (Enabled) _music?.Stop(); }

    /// <summary>Fire-and-forget sfx by event name; tolerant of missing clips.</summary>
    public void Event(string name)
    {
        switch (name)
        {
            case "jump": Play("jump", 0.5f, 0.05f); break;
            case "hammer_hit": Play("hammer_hit", 0.8f); break;
            case "splash": Play("splash", 0.5f); break;
            case "blip": Play("blip", 0.5f); break;
            // Priority 5: golden toast sting for achievement unlocks.
            case "achievement": Play("cheer", 0.55f, 0.15f); Play("cash", 0.5f, 0.1f); break;
            default: Play(name); break;
        }
    }
}
