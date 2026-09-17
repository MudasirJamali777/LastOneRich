using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>One achievement definition. Static and code-side; unlock STATE lives in SaveData.</summary>
public sealed class AchievementDef
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Desc;

    public AchievementDef(string id, string name, string desc)
    {
        Id = id;
        Name = name;
        Desc = desc;
    }
}

/// <summary>
/// Season 1 achievements (Priority 5).
///
/// Unlock state persists inside save.json (SaveData.Achievements), so trophies survive
/// restarts exactly like career money. Evaluation happens at the per-round and per-season
/// choke points instead of polling every frame:
///
///   ResultsState.Enter    -> EvaluateRound   (first round, podium, round win, heist, ghost, glass)
///   BankRiskState         -> first_bank      (risk_taker is evaluated on the NEXT results screen)
///   CashOutOfferState     -> cash_out
///   SeasonEndState        -> champion
///
/// Every unlock: persists immediately (atomic Store), plays a sting, and queues a golden
/// toast that LorGame draws above whatever state is on screen.
/// </summary>
public static class Achievements
{
    public static readonly AchievementDef[] All =
    {
        new("first_steps",   "FIRST STEPS",           "Survive your first round of the season."),
        new("podium",        "PODIUM FINISH",         "Place in the top 3 of any round."),
        new("round_win",     "CENTER STAGE",          "Win a round outright — 1st of the whole field."),
        new("first_bank",    "SAFE HANDS",            "Choose BANK at the Bank vs Risk decision."),
        new("risk_taker",    "DOUBLE OR NOTHING",     "Risk the pot and survive to collect the multiplier."),
        new("money_bags",    "HEIST MASTER",          "Deposit $100,000 or more in a single heist round."),
        new("drone_ghost",   "GHOST PROTOCOL",        "Clear DRONE DODGE without taking a single strike."),
        new("glass_perfect", "FLAWLESS GLASS",        "Cross GLASS PATH MEMORY without breaking one tile."),
        new("cash_out",      "KNOW WHEN TO FOLD 'EM", "Take Max Volt's cash-out offer and walk away."),
        new("champion",      "LAST ONE RICH",         "Win the $1,000,000 grand prize."),
    };

    public static AchievementDef Find(string id)
    {
        foreach (var a in All)
            if (a.Id == id) return a;
        return null;
    }

    public static int UnlockedCount()
    {
        var s = GameServices.Save;
        return s?.Achievements.Count ?? 0;
    }

    public static bool Has(string id)
    {
        var s = GameServices.Save;
        return s != null && s.Achievements.Contains(id);
    }

    /// <summary>
    /// Idempotent unlock: no-op when already earned or when there is no save (headless runs).
    /// Persists on the spot so an alt-F4 seconds later can't take the trophy back.
    /// </summary>
    public static bool Unlock(string id)
    {
        var save = GameServices.Save;
        if (save == null) return false;
        if (save.Achievements.Contains(id)) return false;
        if (Find(id) == null) return false;      // never write unknown ids into the save

        save.Achievements.Add(id);
        SaveSystem.Store(save);
        QueueToast(Find(id));
        GameServices.Audio?.Event("achievement");
        System.Console.WriteLine($"[achievements] unlocked: {id}");
        return true;
    }

    // ------------------------------------------------------------------ evaluation

    /// <summary>
    /// Round-scoped achievements, evaluated once per results ceremony (ResultsState.Enter).
    /// <paramref name="hadPendingRisk"/> must be captured BEFORE Wallet.ApplyPending() —
    /// it means "the player risked the pot last round and just survived to collect it".
    /// </summary>
    public static void EvaluateRound(SeasonRun season, List<Actor> ranking, int playerRank,
                                     bool playerEliminated, bool hadPendingRisk)
    {
        var player = ranking?.FirstOrDefault(a => a.IsPlayer);
        string mode = season?.CurrentLevel?.Dto.Type ?? "";
        string levelId = season?.Round?.Level ?? "";

        if (!playerEliminated)
        {
            Unlock("first_steps");
            if (playerRank <= 3) Unlock("podium");
            if (playerRank == 1) Unlock("round_win");
            if (hadPendingRisk) Unlock("risk_taker");
        }

        if (playerEliminated || player == null) return;

        switch (mode)
        {
            case "ScoreCollect":
                if (player.Score >= 100000) Unlock("money_bags");
                break;
            case "StrikesOut":
                if (player.Strikes == 0) Unlock("drone_ghost");
                break;
        }

        if (string.Equals(levelId, "level06", StringComparison.OrdinalIgnoreCase) && player.Finished)
        {
            // PRIORITY 6 NOTE: the tiles of GLASS PATH MEMORY do not break yet — the BreakTile
            // hazard, the per-actor TilesBroken counter and the shatter/reform loop all land
            // with Priority 6. Until then this achievement anchors on "finished the race".
            // Priority 6 must re-anchor it to ALSO require `player.TilesBroken == 0`
            // (the counter lives on Actor; BreakTile increments it at shatter time).
            Unlock("glass_perfect");
        }
    }

    // ------------------------------------------------------------------ golden toasts

    sealed class Toast
    {
        public string Title;
        public string Desc;
        public float T;
    }

    static readonly List<Toast> _toasts = new();

    /// <summary>How long one toast rides on screen (slide-in, hold, fade-out).</summary>
    public const float ToastSeconds = 4.2f;

    public static bool HasToasts => _toasts.Count > 0;

    static void QueueToast(AchievementDef def)
    {
        _toasts.Add(new Toast { Title = def.Name, Desc = def.Desc, T = 0f });
        if (_toasts.Count > 3) _toasts.RemoveAt(0);   // never stack the whole screen
    }

    /// <summary>Tick the toast queue. Called once per frame by LorGame, in every state.</summary>
    public static void UpdateToasts(float dt)
    {
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            _toasts[i].T += dt;
            if (_toasts[i].T >= ToastSeconds) _toasts.RemoveAt(i);
        }
    }

    /// <summary>
    /// Draw active toasts (call INSIDE a Ui.Begin/End block — LorGame owns that).
    /// Stacked from the top-center down; each slides in, holds, then fades.
    /// </summary>
    public static void DrawToasts(BitmapFont f, SpriteBatch sb)
    {
        float y = 96f;
        foreach (var t in _toasts)
        {
            float inK = MathHelper.Clamp(t.T / 0.35f, 0f, 1f);
            inK = 1f - (1f - inK) * (1f - inK);                       // ease-out slide
            float fade = t.T > ToastSeconds - 0.7f
                ? MathHelper.Clamp((ToastSeconds - t.T) / 0.7f, 0f, 1f)
                : 1f;
            float alpha = fade;

            string head = "ACHIEVEMENT UNLOCKED!";
            var hs = f.Measure(head, 0.42f);
            var ns = f.Measure(t.Title, 0.72f);
            var ds = f.Measure(t.Desc, 0.44f);
            float w = MathF.Max(ns.X, MathF.Max(hs.X, ds.X)) + 72f;
            float x = 640f - w / 2f;
            float yy = MathHelper.Lerp(y - 78f, y, inK);

            var gold = new Color(255, 210, 63);
            Ui.Rect(new Vector2(x, yy), new Vector2(w, 92), new Color(10, 10, 22, (int)(235 * alpha)));
            Ui.Rect(new Vector2(x, yy), new Vector2(w, 4), gold * alpha);
            Ui.Rect(new Vector2(x, yy), new Vector2(5, 92), gold * alpha);
            Ui.Frame(new Rectangle((int)x, (int)yy, (int)w, 92), 2, gold * (0.8f * alpha));

            f.Draw(sb, head, new Vector2(x + 20, yy + 12), gold * alpha, 0.42f);
            f.DrawOutlined(sb, t.Title, new Vector2(x + 20, yy + 34), Color.White * alpha, 0.72f);
            f.Draw(sb, t.Desc, new Vector2(x + 20, yy + 66), new Color(190, 195, 220) * alpha, 0.44f, 0f, Vector2.Zero, true);

            y += 104f;
        }
    }
}
