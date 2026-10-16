using AVALadyBug.Engine;

namespace AVALadyBug.Tests;

/// <summary>How the computer player gets on, garden by garden (set LADYBUG_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that turns at random now and then).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("LADYBUG_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "ladybug-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var levels = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 10 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? Wander(t) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"L{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"L{g.Wave} {g.Cause}") + 1;
                if (g.Phase == Phase.Clear && last != Phase.Clear) levels.Add($"L{wave}:{(t - start) / 60:0}s");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: garden {g.Wave} after {t / 60:0}s score {g.Score} (now {g.Items.Count(i => !i.Gone && i.Kind != ItemKind.Skull)} left) | {string.Join(" ", levels)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }

    /// <summary>A new way every second or so, picked by the clock.</summary>
    private static Controls Wander(int t)
    {
        var d = Dirs.All[(t / 50 * 7 + t / 130) % 4];
        return new Controls(d.Dx(), d.Dy());
    }
}
