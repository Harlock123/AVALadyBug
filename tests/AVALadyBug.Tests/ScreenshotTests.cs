using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVALadyBug.Engine;
using AVALadyBug.Views;
using Controls = AVALadyBug.Engine.Controls;

namespace AVALadyBug.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set LADYBUG_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaladybug-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("LADYBUG_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVALadyBug-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("BUG", 87650, 9);
        g.HighScores.Insert("AVA", 52340, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A garden, and the computer at work in it until something worth showing.</summary>
    private static void Play(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 300)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, busy, seconds));
        Shot(w, c, name);
    }

    private static bool Running(GameEngine g) => g.Phase == Phase.Play && g.Player.Alive;

    [AvaloniaFact]
    public void The_first_garden() => Play(1, "garden", g => Running(g) && g.Bugs.Count(b => b.Out) >= 2 && g.Items.Count(i => i.Gone) is > 20 and < 40);

    [AvaloniaFact]
    public void A_gate_swinging() => Play(2, "gate", g => Running(g) && g.Maze.Gates.Any(x => x.Swing > 0.6) && g.Bugs.Count(b => b.Out) >= 3);

    [AvaloniaFact]
    public void Letters_and_a_multiplier() => Play(3, "letters", g => Running(g) && g.Multiplier >= 2 && g.SpecialLit.Concat(g.ExtraLit).Count(x => x) >= 2, seconds: 600);

    [AvaloniaFact]
    public void The_vegetable() => Play(4, "vegetable", g => Running(g) && g.Vegetable && g.Bugs.All(b => b.Out), seconds: 400);

    [AvaloniaFact]
    public void Frozen() => Play(5, "frozen", g => Running(g) && g.Frozen > 3, seconds: 600);

    [AvaloniaFact]
    public void Caught() => Play(6, "caught", g => g.Phase == Phase.Dying && g.Cause == "caught" && g.PhaseTime > 0.3, seconds: 600);
}
