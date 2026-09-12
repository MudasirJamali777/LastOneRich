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

    public AudioBank(string dir)
    {
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
                sfx.Play(volume * 0.9f, MathHelper.Clamp(pitch, -1f, 1f), pan);
        }
        catch { Enabled = false; } // device vanished / never existed — go silent
    }

    public void PlayMusic(float volume = 0.45f)
    {
        if (!Enabled) return;
        if (_music == null && _sfx.TryGetValue("music_loop", out var m))
        {
            _music = m.CreateInstance();
            _music.IsLooped = true;
        }
        if (_music != null)
        {
            try
            {
                _music.Volume = volume;
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
            default: Play(name); break;
        }
    }
}
