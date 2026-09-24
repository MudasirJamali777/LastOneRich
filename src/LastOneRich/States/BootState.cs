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
#if DEBUG
        if (GameServices.Launch?.AchieveAll == true)
        {
            foreach (var achievement in Achievements.All)
                Achievements.Unlock(achievement.Id);
            GameServices.Launch.AchieveAll = false;
        }

        // dev shortcut for screenshots / fast testing: --goto=menu|intro|game
        var gotoArg = GameServices.Launch?.Goto;
        if (gotoArg == "game")
        {
            var run = Season.SeasonRun.Create();
            run.RoundIdx = System.Math.Clamp(GameServices.Launch?.Round ?? 1, 1, run.Season.Rounds.Count) - 1;
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
#endif
        _t += dt;
        if (_t > 2.1f || Input.SkipPressed)
        {
            // Priority 7: a brand-new career (no save file yet) or an old save that predates
            // this screen (WelcomeSeen defaults false on migration) sees the welcome/difficulty
            // screen exactly once instead of dropping straight into the main menu.
            bool firstRun = !SaveSystem.HasSave() || !(GameServices.Save?.WelcomeSeen ?? false);
            _sm.Replace(firstRun ? new WelcomeState(_sm) : new MainMenuState(_sm));
        }
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
