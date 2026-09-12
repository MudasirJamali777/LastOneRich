using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

public sealed class BootState : IGameState
{
    readonly StateMachine _sm;
    float _t;

    public BootState(StateMachine sm) => _sm = sm;

    public void Enter() { }
    public void Exit() { }

    public void Update(float dt)
    {
        // dev shortcut for screenshots / fast testing: --goto=menu|intro|game
        var gotoArg = GameServices.Launch?.Goto;
        if (gotoArg == "game")
        {
            var run = Season.SeasonRun.Create();
            run.RoundIdx = 0;
            _sm.Replace(new GameplayState(_sm, run));
            return;
        }
        if (gotoArg == "intro")
        {
            _sm.Replace(new IntroCutsceneState(_sm, Season.SeasonRun.Create()));
            return;
        }
        if (gotoArg == "menu")
        {
            _sm.Replace(new MainMenuState(_sm));
            return;
        }
        _t += dt;
        if (_t > 2.1f || Input.SkipPressed) _sm.Replace(new MainMenuState(_sm));
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        GameServices.Gfx.Clear(new Color(10, 10, 20));
        Ui.Begin(vp);
        float a = MathHelper.Clamp(_t / 0.6f, 0f, 1f) * MathHelper.Clamp((2.1f - _t) / 0.5f, 0f, 1f);
        var f = GameServices.Font;
        var col = Color.White * a;
        f.Draw(GameServices.Sb, "VOLT DOME STUDIOS", new Vector2(640, 320), new Color(255, 210, 63) * a, 1.1f, 0f, new Vector2(f.Measure("VOLT DOME STUDIOS", 1.1f).X / 2, 0));
        f.Draw(GameServices.Sb, "presents", new Vector2(640, 375), col, 0.55f, 0f, new Vector2(f.Measure("presents", 0.55f).X / 2, 0));
        Ui.End();
    }
}
