using LastOneRich.Cine;
using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Episode intro: cinematic camera over the live arena + JSON timeline beats (GDD 9).</summary>
public sealed class IntroCutsceneState : IGameState
{
    readonly StateMachine _sm;
    readonly SeasonRun _season;
    Level _level;
    CutscenePlayer _cut;
    readonly ScreenFX _screen = new();
    readonly Particles _fx = new();
    bool _confettiDone;
    bool _leaving;

    public IntroCutsceneState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    public void Enter()
    {
        var levelId = _season.Season.Rounds[0].Level;
        _level = Level.Load(levelId, null, n => GameServices.Audio.Event(n));
        _cut = new CutscenePlayer(Json.Load<CutsceneDTO>("cutscenes/intro.json"));
        GameServices.Audio.PlayMusic();
        _screen.LetterboxTo(1f);
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _screen.Update(dt);
        _fx.Update(dt);
        _cut.Update(dt, GameServices.Audio);

        if (_cut.ConfettiFired && !_confettiDone)
        {
            _confettiDone = true;
            _fx.ConfettiBurst(new Vector2(640, 240), 160, 1200, 60);
            _fx.ConfettiBurst(new Vector2(200, 200), 80, 500, 60);
            _fx.ConfettiBurst(new Vector2(1080, 200), 80, 500, 60);
        }

        if (Input.SkipPressed && !_leaving)
        {
            _cut.Skip();
            GameServices.Audio.Event("blip");
        }

        if (_cut.Done && !_leaving)
        {
            _leaving = true;
            _screen.FadeTo(1f, 2.2f);
        }

        if (_leaving && _screen.FadeAlpha > 0.92f)
            _sm.Replace(new GameplayState(_sm, _season));
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        float aspect = vp.Width / (float)vp.Height;
        GameServices.Gfx.Clear(_level.SkyColor);
        var r = GameServices.Renderer;
        r.BeginFrame(_cut.Cam, aspect, _level.SkyColor);
        _level.Draw(r);
        r.EndFrame();

        var f = GameServices.Font;
        var sb = GameServices.Sb;
        Ui.Begin(vp);

        // lower-third subtitles
        if (!string.IsNullOrEmpty(_cut.SubText))
        {
            var text = _cut.SubText;
            var speaker = _cut.SubSpeaker ?? "";
            float scale = 0.72f;
            var size = f.Measure(text, scale);
            var barH = 78;
            var y = 720 - 150;
            Ui.Rect(new Vector2(120, y), new Vector2(1040, barH), new Color(8, 8, 18, 215));
            Ui.Rect(new Vector2(120, y), new Vector2(6, barH), new Color(255, 210, 63));
            f.Draw(sb, speaker, new Vector2(150, y + 10), new Color(255, 210, 63), 0.5f);
            f.Draw(sb, text, new Vector2(150, y + 34), new Color(240, 242, 255), scale);
        }

        // grand prize overlay
        if (_cut.ShowPrize)
        {
            string amount = Ui.Money(_cut.PrizeAmount);
            var aSize = f.Measure(amount, 1.9f);
            float pop = 1f + _cut.PrizePop * 0.6f;
            Ui.Rect(new Vector2(340, 150), new Vector2(600, 150), new Color(8, 8, 18, 200));
            Ui.Frame(new Rectangle(340, 150, 600, 150), 3, new Color(255, 210, 63));
            var g = "GRAND PRIZE";
            f.Draw(sb, g, new Vector2(640, 168), new Color(255, 210, 63), 0.7f, 0f, new Vector2(f.Measure(g, 0.7f).X / 2, 0));
            f.DrawOutlined(sb, amount, new Vector2(640, 210), new Color(255, 235, 150), 1.9f * pop, 0f, new Vector2(aSize.X / 2 * pop, 0));
        }

        _fx.Draw(sb);

        // skip hint
        if (!_cut.Done)
        {
            string hint = "ENTER: SKIP";
            f.Draw(sb, hint, new Vector2(1170, 688), new Color(160, 165, 190, 200), 0.5f, 0f, new Vector2(f.Measure(hint, 0.5f).X, 0));
        }

        Ui.End();
        _screen.Draw(sb, vp);
    }
}
