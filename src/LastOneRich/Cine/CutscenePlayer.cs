using LastOneRich.Core;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.Cine;

/// <summary>
/// Data-driven cutscene player (GDD 9): JSON timeline of camera beats, subtitles,
/// overlays, stingers and events. No cinematic editor — it's all JSON.
/// </summary>
public sealed class CutscenePlayer
{
    readonly List<CutsceneBeatDTO> _beats;
    readonly List<CutsceneBeatDTO> _camKeys = new();
    readonly List<CutsceneBeatDTO> _orbitKeys = new();
    readonly Dictionary<string, string> _tokens;
    readonly float _levelMidZ;
    readonly double _duration;
    float _t;
    int _next;

    public bool Done { get; private set; }
    public readonly Camera3D Cam = new();
    public string SubSpeaker, SubText;
    public bool ShowPrize;
    public double PrizeAmount;
    public bool ConfettiFired;
    public float PrizePop;

    public CutscenePlayer(CutsceneDTO dto, Dictionary<string, string> tokens = null, float levelMidZ = 100f)
    {
        _beats = dto.Beats.OrderBy(b => b.T).ToList();
        _duration = dto.Duration;
        _tokens = tokens ?? new Dictionary<string, string>();
        _levelMidZ = levelMidZ;
        foreach (var b in _beats)
        {
            if (string.Equals(b.Type, "camera", StringComparison.OrdinalIgnoreCase))
                _camKeys.Add(b);
            if (string.Equals(b.Type, "cameraOrbit", StringComparison.OrdinalIgnoreCase))
                _orbitKeys.Add(b);
        }
        if (_camKeys.Count > 0) ApplyCam(_camKeys[0], _camKeys[0], 0);
        else if (_orbitKeys.Count > 0) ApplyOrbit(_orbitKeys[0], 0);
    }

    string Tok(string s)
    {
        if (s == null || _tokens.Count == 0) return s;
        foreach (var kv in _tokens)
            s = s.Replace("{" + kv.Key + "}", kv.Value);
        return s;
    }

    public void Update(float dt, AudioBank audio)
    {
        if (Done) return;
        _t += dt;
        PrizePop = System.MathF.Max(0, PrizePop - dt);

        while (_next < _beats.Count && _beats[_next].T <= _t)
        {
            var b = _beats[_next++];
            switch ((b.Type ?? "").ToLowerInvariant())
            {
                case "subtitle":
                    SubSpeaker = Tok(b.Speaker);
                    SubText = Tok(b.Text);
                    _subUntil = (float)b.T + (float)b.Dur;
                    break;
                case "overlay":
                    if (string.Equals(b.Graphic, "prize", StringComparison.OrdinalIgnoreCase))
                    {
                        ShowPrize = true;
                        PrizeAmount = b.Amount;
                        PrizePop = 0.5f;
                        _prizeUntil = (float)b.T + (float)b.Dur;
                    }
                    break;
                case "audio":
                    if (!string.IsNullOrEmpty(b.Sfx)) audio?.Event(b.Sfx);
                    break;
                case "event":
                    if (string.Equals(b.Name, "confetti", StringComparison.OrdinalIgnoreCase)) ConfettiFired = true;
                    break;
            }
        }

        if (_t >= _subUntil) { SubText = null; }
        if (_t >= _prizeUntil) ShowPrize = false;

        if (_orbitKeys.Count > 0) { ApplyOrbitCurrent(); } else { EvalCamera(); }
        if (_t >= _duration) { Done = true; SubText = null; ShowPrize = false; }
    }

    void ApplyOrbitCurrent()
    {
        int i = 0;
        for (int k = 0; k < _orbitKeys.Count; k++)
            if (_orbitKeys[k].T <= _t) i = k;
        ApplyOrbit(_orbitKeys[i], _t - (float)_orbitKeys[i].T);
    }

    /// <summary>cameraOrbit beat: code-driven slow orbit around the arena mid — works for ANY level.</summary>
    void ApplyOrbit(CutsceneBeatDTO b, float localT)
    {
        float centerZ = b.CenterZ >= 0 ? (float)b.CenterZ : _levelMidZ;
        float radius = (float)b.Radius, height = (float)b.Height, speed = (float)b.Speed;
        float ang = speed * localT;
        Cam.Position = new Vector3(MathF.Sin(ang) * radius, height, centerZ + MathF.Cos(ang) * radius);
        Cam.LookAt = new Vector3(0, 1.5f, centerZ);
        Cam.FovDeg = (float)b.Fov;
    }

    float _subUntil, _prizeUntil;

    void EvalCamera()
    {
        if (_camKeys.Count == 0) return;
        int i = 0;
        for (int k = 0; k < _camKeys.Count; k++)
            if (_camKeys[k].T <= _t) i = k;
        var cur = _camKeys[i];
        var next = i + 1 < _camKeys.Count ? _camKeys[i + 1] : null;
        if (next == null) { ApplyCam(cur, cur, 1f); return; }
        float span = (float)System.Math.Min(cur.Dur, next.T - cur.T);
        float u = span <= 0.0001f ? 1f : MathHelper.Clamp((float)(_t - cur.T) / span, 0f, 1f);
        u = u * u * (3f - 2f * u); // smoothstep
        ApplyCam(cur, next, u);
    }

    void ApplyCam(CutsceneBeatDTO a, CutsceneBeatDTO b, float u)
    {
        Cam.Position = Vector3.Lerp(a.Pos.ToVec3(), b.Pos.ToVec3(), u);
        Cam.LookAt = Vector3.Lerp((a.LookAt ?? a.Pos).ToVec3(), (b.LookAt ?? b.Pos).ToVec3(), u);
        Cam.FovDeg = MathHelper.Lerp((float)a.Fov, (float)b.Fov, u);
    }

    public void Skip()
    {
        _t = (float)_duration;
        Done = true;
        SubText = null;
        ShowPrize = false;
    }
}
