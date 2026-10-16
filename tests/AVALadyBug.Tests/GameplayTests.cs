using AVALadyBug.Engine;

namespace AVALadyBug.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaladybug-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Garden 1 in play; calm (no bug comes out, the timer stands still, those put out stay put) unless <paramref name="calm"/> is false.</summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        return g;
    }

    private static Controls Stick(Dir d) => new(d.Dx(), d.Dy());

    /// <summary>Steps to run a number of squares at the ladybug's speed.</summary>
    private static int Squares(double n) => (int)Math.Ceiling(n / GameEngine.LadybugSpeed * 60) + 1;

    // ───────────────────────────── the garden ─────────────────────────────

    [Fact]
    public void Every_square_can_be_reached_whichever_way_the_gates_are_turned()
    {
        var rng = new Random(3);
        for (int trial = 0; trial < 300; trial++)
        {
            var m = Maze.Garden();
            foreach (var gt in m.Gates) gt.Vertical = rng.Next(2) == 0;
            var seen = new HashSet<(int, int)> { (Field.StartC, Field.StartR) };
            var todo = new Stack<(int C, int R)>();
            todo.Push((Field.StartC, Field.StartR));
            while (todo.Count > 0)
            {
                var (c, r) = todo.Pop();
                foreach (var d in Dirs.All)
                    if (m.OpenToBug(c, r, d) && seen.Add((c + d.Dx(), r + d.Dy()))) todo.Push((c + d.Dx(), r + d.Dy()));
            }
            Assert.Equal(Field.Cols * Field.Rows, seen.Count);
        }
    }

    [Fact]
    public void Each_garden_has_its_letters_hearts_and_skulls_the_same_every_time_with_a_flower_everywhere_else()
    {
        for (int wave = 1; wave <= 6; wave++)
        {
            var a = Game(wave: wave, seed: 1);
            var b = Game(wave: wave, seed: 99);
            Assert.Equal(a.Items.Select(i => (i.Kind, i.C, i.R, i.Letter)), b.Items.Select(i => (i.Kind, i.C, i.R, i.Letter)));
            Assert.Equal(GameEngine.Letters, a.Items.Count(i => i.Kind == ItemKind.Letter));
            Assert.Equal(GameEngine.Hearts, a.Items.Count(i => i.Kind == ItemKind.Heart));
            Assert.Equal(a.SkullCount, a.Items.Count(i => i.Kind == ItemKind.Skull));
            Assert.Equal(Field.Cols * Field.Rows - 2, a.Items.Count);
            Assert.DoesNotContain(a.Items, i => (i.C, i.R) == (Field.NestC, Field.NestR) || (i.C, i.R) == (Field.StartC, Field.StartR));
            Assert.All(a.Items.Where(i => i.Kind == ItemKind.Letter), i => Assert.Contains(i.Letter, GameEngine.Special + GameEngine.Extra));
        }
        Assert.True(Game(wave: 9).SkullCount > Game(wave: 1).SkullCount);
    }

    // ───────────────────────────── the ladybug ─────────────────────────────

    [Fact]
    public void The_ladybug_runs_along_the_paths_eating_the_flowers_and_stops_at_a_hedge()
    {
        var g = Game();
        int score = g.Score;
        // From the start the only way is down the corridor below it.
        Assert.False(g.Maze.OpenToLadybug(Field.StartC, Field.StartR, Dir.Up));
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal((5, 9), (g.Player.C, g.Player.R));
        Assert.Equal(score + GameEngine.DotPoints, g.Score);
        // Into a hedge: it stops.
        g.PutLadybug(0, 0);
        g.Run(Squares(3), Stick(Dir.Down));
        Assert.Equal(Dir.None, g.Player.Heading);
        Assert.False(g.Maze.OpenToLadybug(g.Player.C, g.Player.R, Dir.Down));
    }

    [Fact]
    public void A_turn_asked_for_early_is_taken_at_the_next_chance_and_straight_back_at_once()
    {
        var g = Game();
        g.PutLadybug(0, 7);
        g.Run(1, Stick(Dir.Up));
        g.Run(2, Stick(Dir.Right));
        Assert.Equal(Dir.Up, g.Player.Heading);
        g.Run(Squares(1.5), default);
        Assert.Equal((Dir.Right, 0, 6), (g.Player.Heading, g.Player.C, g.Player.R));
        g.Run(3, Stick(Dir.Left));
        Assert.Equal(Dir.Left, g.Player.Heading);
    }

    [Fact]
    public void Pushing_through_a_gate_swings_it_a_quarter_turn_and_then_it_bars_the_bugs()
    {
        var g = Game();
        var gate = g.Maze.Gates.First(x => !x.Vertical);
        // Stand just above one of its arms and go down through it.
        g.PutLadybug(gate.Cx, gate.Cy - 1);
        Assert.False(g.Maze.OpenToBug(gate.Cx, gate.Cy - 1, Dir.Down));
        Assert.True(g.Maze.OpenToLadybug(gate.Cx, gate.Cy - 1, Dir.Down));
        g.Run(2, Stick(Dir.Down));
        Assert.True(gate.Vertical);
        Assert.True(g.Maze.OpenToBug(gate.Cx, gate.Cy - 1, Dir.Down));
        Assert.False(g.Maze.OpenToBug(gate.Cx - 1, gate.Cy - 1, Dir.Right));
    }

    [Fact]
    public void Red_letters_light_SPECIAL_yellow_ones_EXTRA_and_a_whole_word_pays_out()
    {
        var g = Game();
        g.Clear();
        g.PutLadybug(0, 1);
        g.SetTime(0);
        // Phase 0 at time 0 is red; 1 yellow.
        g.PutItem(ItemKind.Letter, 0, 2, 'S', phase: 0);
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.True(g.SpecialLit[0]);
        g.SetTime(0);
        g.PutLadybug(0, 1);
        g.PutItem(ItemKind.Letter, 0, 2, 'X', phase: 1);
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.True(g.ExtraLit[1]);
        // The rest of EXTRA, then the last letter: a life.
        g.ExtraLit[0] = g.ExtraLit[2] = g.ExtraLit[3] = true;
        int lives = g.Lives;
        g.SetTime(0);
        g.PutLadybug(0, 1);
        g.PutItem(ItemKind.Letter, 0, 2, 'A', phase: 1);
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal(lives + 1, g.Lives);
        Assert.All(g.ExtraLit, x => Assert.False(x));
        // SPECIAL: ten thousand.
        for (int i = 0; i < 6; i++) g.SpecialLit[i] = true;
        g.SpecialLit[6] = false;
        int score = g.Score;
        g.SetTime(0);
        g.PutLadybug(0, 1);
        g.PutItem(ItemKind.Letter, 0, 2, 'L', phase: 0);
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal(score + GameEngine.LetterPoints + GameEngine.SpecialBonus, g.Score);
    }

    [Fact]
    public void Blue_hearts_step_up_what_everything_is_worth()
    {
        var g = Game();
        g.Clear();
        int[] seen = new int[4];
        for (int i = 0; i < 4; i++)
        {
            seen[i] = g.Multiplier;
            g.SetTime(0);
            g.PutLadybug(0, 1);
            g.PutItem(ItemKind.Heart, 0, 2, phase: 2);
            g.Run(Squares(1), Stick(Dir.Down));
        }
        Assert.Equal([1, 2, 3, 5], seen);
        int score = g.Score;
        g.PutLadybug(0, 1);
        g.PutItem(ItemKind.Dot, 0, 2);
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal(score + GameEngine.DotPoints * 5, g.Score);
    }

    [Fact]
    public void A_skull_is_the_end_of_the_ladybug()
    {
        var g = Game();
        g.PutLadybug(0, 1);
        g.PutItem(ItemKind.Skull, 0, 2);
        int lives = g.Lives;
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal((Phase.Dying, "a skull", lives - 1), (g.Phase, g.Cause, g.Lives));
        g.Run((int)(GameEngine.DyingSeconds * 60) + 2, default);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal((Field.StartC, Field.StartR), (g.Player.C, g.Player.R));
        Assert.All(g.Bugs, b => Assert.False(b.Out));
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    [Fact]
    public void Each_time_round_the_timer_lets_another_bug_out_and_with_all_out_the_vegetable_appears()
    {
        var g = Game(calm: false);
        g.Bugs.Clear();
        for (int i = 0; i < GameEngine.BugCount; i++) g.Bugs.Add(new Bug { C = Field.NestC, R = Field.NestR, Id = i });
        // The bugs held still, so none comes for the ladybug while the timer runs.
        g.Freeze(9999);
        g.SetLap(0.9999);
        g.Run(3, default);
        Assert.Equal(1, g.Bugs.Count(b => b.Out));
        Assert.False(g.Vegetable);
        g.Run((int)(g.LapSeconds * 60 * 3) + 30, default);
        Assert.True(g.Bugs.All(b => b.Out));
        Assert.True(g.Vegetable);
    }

    [Fact]
    public void A_bug_runs_the_paths_but_never_through_a_gate()
    {
        var g = Game(calm: false);
        g.SetLap(-100);
        var gate = g.Maze.Gates.First(x => !x.Vertical);
        var b = g.PutBug(gate.Cx, gate.Cy - 1);
        for (int i = 0; i < 60 * 20; i++)
        {
            g.Tick(default);
            // Never on either side of the gate's arms in the same stride.
            if (b.Along > 0 && Maze.Edge(b.C, b.R, b.Heading) is { } e) Assert.Null(g.Maze.GateOn(e));
            if (g.Phase != Phase.Play) break;
        }
    }

    [Fact]
    public void A_bug_that_runs_onto_a_skull_is_done_for_and_goes_back_to_wait_in_the_nest()
    {
        var g = Game(calm: false);
        g.SetLap(-100);
        g.Clear();
        g.PutLadybug(10, 10);
        // A straight run along the top: the bug is put heading right with a skull ahead.
        var b = g.PutBug(1, 0, Dir.Right);
        var skull = g.PutItem(ItemKind.Skull, 2, 0);
        g.Run(60, default);
        Assert.True(skull.Gone);
        Assert.False(b.Out);
    }

    [Fact]
    public void A_bug_catching_the_ladybug_costs_a_life()
    {
        var g = Game();
        g.PutLadybug(0, 1);
        g.PutBug(0, 2);
        int lives = g.Lives;
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal((Phase.Dying, "caught", lives - 1), (g.Phase, g.Cause, g.Lives));
    }

    [Fact]
    public void The_bugs_go_for_the_ladybug_more_often_in_later_gardens_and_faster()
    {
        Assert.True(Game(wave: 6).Chase > Game(wave: 1).Chase);
        Assert.True(Game(wave: 6).BugSpeed > Game(wave: 1).BugSpeed);
        Assert.True(Game(wave: 6).LapSeconds < Game(wave: 1).LapSeconds);
        Assert.True(Game(wave: 6).BugSpeed < GameEngine.LadybugSpeed);
    }

    [Fact]
    public void The_vegetable_freezes_the_bugs_for_a_while()
    {
        var g = Game(calm: false);
        g.SetLap(-100);
        g.ShowVegetable();
        var b = g.PutBug(0, 0, Dir.Right);
        g.PutLadybug(Field.NestC, Field.NestR - 1);
        int score = g.Score;
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal(score + g.VegetablePoints, g.Score);
        Assert.True(g.Frozen > 0);
        var at = (b.C, b.R, b.Along);
        g.Run(60, default);
        Assert.Equal(at, (b.C, b.R, b.Along));
        g.Run((int)(GameEngine.FreezeSeconds * 60), default);
        Assert.NotEqual(at, (b.C, b.R, b.Along));
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void Eating_everything_but_the_skulls_clears_the_garden()
    {
        var g = Game();
        g.Items.Clear();
        g.PutItem(ItemKind.Skull, 0, 0);
        g.PutItem(ItemKind.Dot, 5, 9);
        g.Run(Squares(1), Stick(Dir.Down));
        Assert.Equal(Phase.Clear, g.Phase);
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal(2, g.Wave);
    }

    [Fact]
    public void The_last_ladybug_lost_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutLadybug(0, 1);
        g.PutItem(ItemKind.Skull, 0, 2);
        g.Run(Squares(1), Stick(Dir.Down));
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "BUG") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("BUG", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_vary_the_garden()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var waves = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            waves.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal([1, 2], waves);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, 0), (g.Wave, g.Lives, g.Score));
        Assert.Equal((Field.StartC, Field.StartR), (g.Player.C, g.Player.R));
    }

    [Fact]
    public void The_up_key_runs_the_ladybug_up_the_screen_and_the_down_key_down_it()
    {
        using var input = new InputHandler();
        var g = Game();
        g.PutLadybug(0, 1);
        input.OnKeyDown(Avalonia.Input.Key.Up);
        Assert.Equal(-1, input.Controls.Dy);
        g.Run(Squares(1), input.Controls);
        Assert.Equal((0, 0), (g.Player.C, g.Player.R));
        input.OnKeyUp(Avalonia.Input.Key.Up);
        input.OnKeyDown(Avalonia.Input.Key.Down);
        g.Run(Squares(1), input.Controls);
        Assert.Equal((0, 1), (g.Player.C, g.Player.R));
    }

    [Fact]
    public void The_computer_player_clears_the_first_garden()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(1);
            for (int i = 0; i < 60 * 240 && g.Wave == 1 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Wave == 2, $"seed {seed}: {g.Items.Count(x => !x.Gone && x.Kind != ItemKind.Skull)} left");
        }
    }

    [Fact]
    public void Wandering_about_at_random_soon_loses()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        for (int t = 0; t < 60 * 300 && g.State == GameState.Playing; t++)
        {
            var d = Dirs.All[(t / 50 * 7 + t / 130) % 4];
            g.Tick(new Controls(d.Dx(), d.Dy()));
        }
        Assert.NotEqual(GameState.Playing, g.State);
        Assert.Equal(1, g.Wave);
    }
}
