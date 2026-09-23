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

    // ---- Priority 7: music fade-in state, advanced by Tick() ----
    // A fade is expressed as "grow BedVolume from 0 to its target over N seconds"; every other
    // volume knob (MusicVolume fader, MasterVolume, per-track bed) is layered on top exactly as
    // PushMusicVolume already does, so a fade-in still respects a mid-fade Settings change.
    float _fadeTargetBed;
    float _fadeElapsed;
    float _fadeDuration;
    bool _fading;

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
        try { _music.Volume = Clamp01(BedVolume * MusicVolume * MasterVolume); } catch { }
    }

    /// <summary>The music bed level set by the last PlayMusic call (defaults to its 0.45 argument).</summary>
    float BedVolume = 0.45f;

    /// <summary>
    /// SoundEffectInstance.Volume throws OutOfRangeException outside 0..1 — every write path
    /// (fade-in, fader math, a hand-edited settings.json with an out-of-range percent) funnels
    /// through here rather than trusting the caller's arithmetic to stay in range.
    /// </summary>
    static float Clamp01(float v) => MathHelper.Clamp(v, 0f, 1f);

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
                sfx.Play(Clamp01(volume * SfxVolume * MasterVolume), MathHelper.Clamp(pitch, -1f, 1f), pan);
        }
        catch { Enabled = false; } // device vanished / never existed — go silent
    }

    public void PlayMusic(float volume = 0.45f)
    {
        if (!Enabled) return;
        _fading = false;       // a direct PlayMusic call always wins over any fade in flight
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
                _music.Volume = Clamp01(volume * MusicVolume * MasterVolume);
                if (_music.State != Microsoft.Xna.Framework.Audio.SoundState.Playing) _music.Play();
            }
            catch { Enabled = false; }
        }
    }

    /// <summary>
    /// Priority 7: start (or restart) the music bed at zero and rise to its remembered bed level
    /// (whatever the last PlayMusic/PlayMusicFadeIn call set, or the 0.45 default) over
    /// <paramref name="fadeSeconds"/>, ticked by <see cref="Tick"/>. Used by the menu/welcome
    /// flow so the bed swells in instead of snapping to full volume the instant a state loads.
    /// A fade in progress is replaced by a new one (e.g. re-entering the menu quickly); a plain
    /// <see cref="PlayMusic"/> call still cancels any fade immediately, as documented there.
    /// </summary>
    public void PlayMusicFadeIn(string id, float fadeSeconds)
    {
        if (!Enabled) return;
        fadeSeconds = System.MathF.Max(0.01f, fadeSeconds);

        if (_music == null && _sfx.TryGetValue(id.ToLowerInvariant(), out var m))
        {
            _music = m.CreateInstance();
            _music.IsLooped = true;
        }
        if (_music == null) return;

        _fadeTargetBed = BedVolume;   // fade toward the bed level already on record (or default)
        BedVolume = 0f;
        _fadeElapsed = 0f;
        _fadeDuration = fadeSeconds;
        _fading = true;

        try
        {
            _music.Volume = 0f;
            if (_music.State != Microsoft.Xna.Framework.Audio.SoundState.Playing) _music.Play();
        }
        catch { Enabled = false; _fading = false; }
    }

    /// <summary>
    /// Advance any fade-in in progress. Called once per frame from <see cref="LorGame.Update"/>;
    /// a no-op whenever nothing is fading, so states that never call PlayMusicFadeIn pay nothing.
    /// </summary>
    public void Tick(float dt)
    {
        if (!_fading || !Enabled || _music == null) return;

        _fadeElapsed += dt;
        float t = MathHelper.Clamp(_fadeElapsed / _fadeDuration, 0f, 1f);
        BedVolume = _fadeTargetBed * t;
        PushMusicVolume();

        if (t >= 1f) _fading = false;
    }

    public void StopMusic() { if (Enabled) _music?.Stop(); _fading = false; }

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
            // Priority 6: glass path. Pitch-jittered so a row of panes going at once reads as
            // several separate panes rather than one flanged mono hit.
            case "glass_crack": Play("glass_crack", 0.55f, Rng.Range(-0.12f, 0.12f)); break;
            case "glass_break": Play("glass_break", 0.75f, Rng.Range(-0.18f, 0.18f)); break;
            case "glass_land": Play("glass_land", 0.34f, Rng.Range(-0.10f, 0.10f)); break;
            case "glass_reform": Play("glass_reform", 0.45f, Rng.Range(-0.08f, 0.08f)); break;
            default: Play(name); break;
        }
    }
}
