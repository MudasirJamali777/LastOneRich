using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Round 10 intermission (GDD L10 "Auction of Doom"): buy advantages, no elimination.</summary>
public sealed class AuctionState : IGameState
{
    sealed class Offer
    {
        public AuctionItemDTO Item;
        public bool Sold;
        public string Buyer;
    }

    readonly StateMachine _sm;
    readonly SeasonRun _season;
    ArenaBackdrop _bg;
    List<Offer> _offers = new();
    int _sel;
    float _t;
    string _banner;
    float _bannerT;

    public AuctionState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    public void Enter()
    {
        _bg = new ArenaBackdrop(_season.CurrentLevel);
        _offers = _season.Economy.AuctionItems.Select(i => new Offer { Item = i }).ToList();
        GameServices.Audio.Event("stinger_twist");

        // rival flavor buys (personality-driven, cosmetic)
        var rng = new System.Random(20260913 + _season.RoundIdx);
        foreach (var c in _season.Cast.Where(c => !c.IsPlayer && !c.Eliminated))
        {
            if (c.Personality.SpendStyle == "Saver") continue;
            if (rng.NextDouble() > (c.Personality.SpendStyle == "Buyer" ? 0.55 : 0.3)) continue;
            var affordable = _offers.Where(o => !o.Sold && o.Item.Price <= _season.Wallet.Banked + 50000).ToList();
            if (affordable.Count == 0) continue;
            var pick = affordable[rng.Next(affordable.Count)];
            pick.Sold = true;
            pick.Buyer = c.Name;
        }
    }

    public void Exit() { }

    void Banner(string s) { _banner = s; _bannerT = 1.6f; }

    public void Update(float dt)
    {
        _t += dt;
        _bg.Update(dt);
        _bannerT = System.MathF.Max(0, _bannerT - dt);
        if (_offers.Count == 0) return;

        if (Input.UpPressed) { _sel = (_sel + _offers.Count - 1) % _offers.Count; GameServices.Audio.Event("blip"); }
        if (Input.DownPressed) { _sel = (_sel + 1) % _offers.Count; GameServices.Audio.Event("blip"); }

        if (Input.ConfirmPressed)
        {
            var o = _offers[_sel];
            if (o.Sold) { Banner("ALREADY SOLD TO " + o.Buyer + "!"); GameServices.Audio.Event("blip"); return; }
            if (_season.Wallet.Banked < o.Item.Price) { Banner("NOT ENOUGH BANKED CASH!"); GameServices.Audio.Event("stinger_elim"); return; }
            _season.Wallet.Banked -= o.Item.Price;
            _season.Upgrades.Add(o.Item.Id);
            o.Sold = true;
            o.Buyer = "YOU";
            Banner("SOLD — " + o.Item.Name + "!");
            GameServices.Audio.Event("cash");
        }

        if (Input.PausePressed || Input.BackPressed)
        {
            GameServices.Audio.Event("blip");
            _sm.Replace(new TwistRevealState(_sm, _season));
        }
    }

    public void Draw()
    {
        _bg.Draw();
        var vp = GameServices.Gfx.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(10, 6, 20, 215));

        string title = "THE AUCTION OF DOOM";
        var ts = f.Measure(title, 1.5f);
        f.DrawOutlined(sb, title, new Vector2(640, 40), new Color(255, 157, 46), 1.5f, 0f, new Vector2(ts.X / 2, 0));
        string sub = "SPEND BANKED CASH ON ADVANTAGES — SURVIVE TO USE THEM";
        var ss = f.Measure(sub, 0.52f);
        f.Draw(sb, sub, new Vector2(640, 108), new Color(200, 208, 235), 0.52f, 0f, new Vector2(ss.X / 2, 0), true);
        f.DrawOutlined(sb, "YOUR BANK: " + Ui.Money(_season.Wallet.Banked), new Vector2(640, 140), new Color(141, 255, 63), 0.8f, 0f, new Vector2(f.Measure("YOUR BANK: " + Ui.Money(_season.Wallet.Banked), 0.8f).X / 2, 0));

        float y = 190;
        for (int i = 0; i < _offers.Count; i++)
        {
            var o = _offers[i];
            bool sel = i == _sel;
            var r = new Rectangle(240, (int)y, 800, 74);
            Ui.Rect(new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), sel ? new Color(20, 24, 44, 250) : new Color(12, 14, 28, 220));
            Ui.Frame(r, sel ? 3 : 1, o.Sold ? new Color(90, 96, 120) : sel ? new Color(255, 157, 46) : new Color(70, 76, 100));
            f.Draw(sb, o.Item.Name, new Vector2(r.X + 20, r.Y + 10), o.Sold ? new Color(120, 126, 150) : new Color(255, 235, 150), 0.7f);
            f.Draw(sb, o.Item.Desc, new Vector2(r.X + 20, r.Y + 42), new Color(185, 190, 215), 0.46f);
            var price = o.Sold ? "SOLD — " + o.Buyer : Ui.Money(o.Item.Price);
            var ps = f.Measure(price, 0.7f);
            f.DrawOutlined(sb, price, new Vector2(r.Right - 20, r.Y + 22), o.Sold ? new Color(120, 126, 150) : new Color(141, 255, 63), 0.7f, 0f, new Vector2(ps.X, 0));
            y += 86;
        }

        if (_bannerT > 0)
        {
            var bs = f.Measure(_banner, 0.9f);
            f.DrawOutlined(sb, _banner, new Vector2(640, 636), new Color(255, 235, 150), 0.9f, 0f, new Vector2(bs.X / 2, 0));
        }
        else
        {
            string hint = "ENTER: BUY · ESC: DONE";
            var hs = f.Measure(hint, 0.55f);
            float blink = 0.6f + 0.4f * MathF.Sin(_t * 4f);
            f.Draw(sb, hint, new Vector2(640, 662), new Color(220, 225, 245) * blink, 0.55f, 0f, new Vector2(hs.X / 2, 0), true);
        }

        Ui.End();
    }
}
