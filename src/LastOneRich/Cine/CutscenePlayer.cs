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

    public CutscenePlayer(CutsceneDTO dto)
    {
        _beats = dto.Beats.OrderBy(b => b.T).ToList();
        _duration = dto.Duration;
        foreach (var b in _beats)
            if (string.Equals(b.Type, "camera", StringComparison.OrdinalIgnoreCase))
                _camKeys.Add(b);
        if (_camKeys.Count > 0) ApplyCam(_camKeys[0], _camKeys[0], 0);
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
                    SubSpeaker = b.Speaker;
                    SubText = b.Text;
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

        EvalCamera();
        if (_t >= _duration) { Done = true; SubText = null; ShowPrize = false; }
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
