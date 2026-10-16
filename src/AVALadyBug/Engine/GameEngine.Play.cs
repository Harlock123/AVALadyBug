namespace AVALadyBug.Engine;

public sealed partial class GameEngine
{
    public const double LadybugSpeed = 3.2, TintSeconds = 0.9, FreezeSeconds = 6, CatchDistance = 0.55;
    public const double StartSeconds = 2.4, DyingSeconds = 2.4, ClearSeconds = 2.6;
    public const int BugCount = 4, Letters = 3, Hearts = 3;
    public const int DotPoints = 10, LetterPoints = 300, HeartPoints = 100, SpecialBonus = 10000;
    public const string Special = "SPECIAL", Extra = "EXTRA";
    public static readonly int[] Multipliers = [1, 2, 3, 5];

    /// <summary>The garden: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Maze Maze { get; private set; } = Maze.Garden();
    public Ladybug Player { get; private set; } = new();
    public List<Bug> Bugs { get; } = [];
    public List<Item> Items { get; } = [];
    /// <summary>The timer round the garden's edge: each time round it lets out another bug. 0 to 1.</summary>
    public double Lap { get; private set; }
    /// <summary>Which of SPECIAL's letters (eaten red) and EXTRA's (eaten yellow) are lit.</summary>
    public bool[] SpecialLit { get; } = new bool[Special.Length];
    public bool[] ExtraLit { get; } = new bool[Extra.Length];
    /// <summary>How many blue hearts this garden: each steps up what everything's worth.</summary>
    public int HeartsBlue { get; private set; }
    public int Multiplier => Multipliers[Math.Min(HeartsBlue, Multipliers.Length - 1)];
    /// <summary>The vegetable in the nest once every bug is out; eating it stops them for a while.</summary>
    public bool Vegetable { get; private set; }
    public bool VegetableEaten { get; private set; }
    public double Frozen { get; private set; }
    public LadybugPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    /// <summary>For tests: the bugs stay where they're put, and the timer doesn't run.</summary>
    internal bool Calm { get; set; }

    private double _time;

    public double LapSeconds => Math.Max(5, 11 - 0.6 * (Wave - 1));
    public double BugSpeed => Math.Min(3.3, 2.3 + 0.12 * (Wave - 1));
    /// <summary>How often a bug heads for the ladybug at a turning rather than wandering.</summary>
    public double Chase => Math.Min(0.85, 0.45 + 0.06 * (Wave - 1));
    public int SkullCount => Math.Min(6, 2 + (Wave - 1) / 2);
    public int VegetablePoints => Math.Min(9500, 1000 + 500 * (Wave - 1));

    /// <summary>The colour a letter or heart is showing now.</summary>
    public Tint TintOf(Item i) => (Tint)(((int)(_time / TintSeconds) + i.Phase) % 3);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        Array.Fill(SpecialLit, false);
        Array.Fill(ExtraLit, false);
        Build(1);
        SetPhase(Phase.Start);
    }

    /// <summary>
    /// A garden laid out: the gates back as they were, a dot in every square but the nest and the start, and among
    /// them a few letters (ones still wanted), hearts, and skulls, placed the same for the same garden every time.
    /// </summary>
    private void Build(int level)
    {
        var rng = new Random(1981 + level * 211);
        Maze = Maze.Garden();
        Items.Clear();
        var free = new List<(int C, int R)>();
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
            if ((c, r) != (Field.NestC, Field.NestR) && (c, r) != (Field.StartC, Field.StartR)) free.Add((c, r));
        (int, int) Take(Func<(int C, int R), bool> ok)
        {
            var choices = free.Where(ok).ToList();
            var pick = choices[rng.Next(choices.Count)];
            free.Remove(pick);
            return pick;
        }
        bool AwayFromStart((int C, int R) p) => Math.Abs(p.C - Field.StartC) + Math.Abs(p.R - Field.StartR) >= 3;
        var wanted = Special.Where((ch, i) => !SpecialLit[i]).Concat(Extra.Where((ch, i) => !ExtraLit[i])).Distinct().ToList();
        if (wanted.Count == 0) wanted = Special.Concat(Extra).Distinct().ToList();
        for (int i = 0; i < Letters; i++)
        {
            var (c, r) = Take(AwayFromStart);
            Items.Add(new Item { Kind = ItemKind.Letter, C = c, R = r, Letter = wanted[rng.Next(wanted.Count)], Phase = rng.Next(3) });
        }
        for (int i = 0; i < Hearts; i++)
        {
            var (c, r) = Take(AwayFromStart);
            Items.Add(new Item { Kind = ItemKind.Heart, C = c, R = r, Phase = rng.Next(3) });
        }
        // Skulls well away from the start and from the nest's door, and never cutting off any part of the garden.
        var skulls = new HashSet<(int, int)>();
        for (int i = 0; i < SkullCount; i++)
        {
            var (c, r) = Take(p => AwayFromStart(p) && Math.Abs(p.C - Field.NestC) + Math.Abs(p.R - (Field.NestR - 1)) >= 3 && Reachable(skulls.Append(p).ToHashSet()));
            skulls.Add((c, r));
            Items.Add(new Item { Kind = ItemKind.Skull, C = c, R = r });
        }
        foreach (var (c, r) in free) Items.Add(new Item { Kind = ItemKind.Dot, C = c, R = r });
        HeartsBlue = 0;
        VegetableEaten = false;
        Restart();
    }

    /// <summary>Whether every square but these can be reached from the start without stepping on one of them.</summary>
    private bool Reachable(HashSet<(int, int)> skulls)
    {
        var seen = new HashSet<(int, int)> { (Field.StartC, Field.StartR) };
        var todo = new Stack<(int C, int R)>();
        todo.Push((Field.StartC, Field.StartR));
        while (todo.Count > 0)
        {
            var (c, r) = todo.Pop();
            foreach (var d in Dirs.All)
            {
                var n = (c + d.Dx(), r + d.Dy());
                if (Maze.OpenToLadybug(c, r, d) && !skulls.Contains(n) && seen.Add(n)) todo.Push(n);
            }
        }
        return seen.Count == Field.Cols * Field.Rows - skulls.Count;
    }

    /// <summary>The ladybug back at the start, the bugs back in the nest, the timer back to the beginning.</summary>
    private void Restart()
    {
        Player = new Ladybug { C = Field.StartC, R = Field.StartR };
        Bugs.Clear();
        for (int i = 0; i < BugCount; i++) Bugs.Add(new Bug { C = Field.NestC, R = Field.NestR, Id = i });
        Lap = 0.6;
        Vegetable = false;
        Frozen = 0;
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Build(wave);
        SetPhase(Phase.Start);
        Say($"GARDEN {wave}", "EAT EVERY DOT - MIND THE SKULLS", StartSeconds + 0.3);
        Play(Sfx.LevelStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                _time += dt;
                Timer(dt);
                MoveLadybug(dt, c);
                if (Phase != Phase.Play) break;
                MoveBugs(dt);
                if (Phase != Phase.Play) break;
                Catch();
                if (Phase == Phase.Play && !Items.Any(i => !i.Gone && i.Kind != ItemKind.Skull)) Cleared();
                break;
            case Phase.Dying:
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Restart();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        foreach (var g in Maze.Gates) g.Swing = Math.Max(0, g.Swing - dt * 6);
        PhaseTime += dt;
    }

    /// <summary>The timer round the edge: each lap lets the next bug out of the nest; once all are out, a vegetable appears there.</summary>
    private void Timer(double dt)
    {
        if (Calm) return;
        Frozen = Math.Max(0, Frozen - dt);
        Lap += dt / LapSeconds;
        if (Lap < 1) return;
        Lap -= 1;
        if (Bugs.FirstOrDefault(b => !b.Out && !b.Dead) is { } next)
        {
            Release(next);
            if (Bugs.All(b => b.Out || b.Dead) && !VegetableEaten) Vegetable = true;
        }
    }

    private void Release(Bug b)
    {
        b.Out = true;
        b.C = Field.NestC;
        b.R = Field.NestR;
        b.Along = 0;
        b.Heading = Dir.Up;
        Play(Sfx.Release);
    }

    // ───────────────────────────── the ladybug ─────────────────────────────

    /// <summary>The way the stick asks for: with both axes pushed, the one that would turn it.</summary>
    public static Dir Asked(Controls c, Dir heading)
    {
        if (c.Dx != 0 && c.Dy != 0)
            return heading is Dir.Left or Dir.Right ? Dirs.Of(0, c.Dy) : Dirs.Of(c.Dx, 0);
        return Dirs.Of(c.Dx, c.Dy);
    }

    private void MoveLadybug(double dt, Controls c)
    {
        var p = Player;
        if (Asked(c, p.Heading) is var ask and not Dir.None) p.Want = ask;
        double step = LadybugSpeed * dt;
        // Turning straight back can happen anywhere.
        if (p.Heading != Dir.None && p.Want == p.Heading.Back())
        {
            p.C += p.Heading.Dx();
            p.R += p.Heading.Dy();
            p.Along = 1 - p.Along;
            p.Heading = p.Want;
            p.Facing = p.Heading;
        }
        while (step > 1e-9)
        {
            if (p.Heading == Dir.None)
            {
                var go = p.Want != Dir.None && Maze.OpenToLadybug(p.C, p.R, p.Want) ? p.Want : Dir.None;
                if (go == Dir.None) return;
                Set(p, go);
            }
            double room = 1 - p.Along;
            if (step < room) { p.Along += step; p.Walk += step; return; }
            step -= room;
            p.Walk += room;
            p.C += p.Heading.Dx();
            p.R += p.Heading.Dy();
            p.Along = 0;
            Arrive(p.C, p.R);
            if (Phase != Phase.Play) return;
            if (p.Want != Dir.None && p.Want != p.Heading && Maze.OpenToLadybug(p.C, p.R, p.Want)) Set(p, p.Want);
            else if (!Maze.OpenToLadybug(p.C, p.R, p.Heading)) { p.Heading = Dir.None; return; }
            else Set(p, p.Heading);
        }
    }

    /// <summary>Off along an edge from a square: through a gate, the gate swings round.</summary>
    private void Set(Ladybug p, Dir d)
    {
        p.Heading = d;
        p.Facing = d;
        if (Maze.Edge(p.C, p.R, d) is { } e && Maze.GateOn(e) is { } g)
        {
            // Which way it turns, for the drawing: pushed from one side or the other.
            g.SwingWay = (g.Vertical ? d.Dy() * (e.R == g.Cy - 1 ? 1 : -1) : d.Dx() * (e.C == g.Cx - 1 ? -1 : 1));
            g.Vertical = !g.Vertical;
            g.Swing = 1;
            Play(Sfx.Gate);
        }
    }

    /// <summary>The ladybug in a square: what's there gets eaten (a skull is the end of it).</summary>
    private void Arrive(int c, int r)
    {
        var (x, y) = Field.Centre(c, r);
        if ((c, r) == (Field.NestC, Field.NestR) && Vegetable)
        {
            Vegetable = false;
            VegetableEaten = true;
            Frozen = FreezeSeconds;
            Award(VegetablePoints, x, y, multiply: false);
            Play(Sfx.Vegetable);
            return;
        }
        if (Items.FirstOrDefault(i => !i.Gone && i.C == c && i.R == r) is not { } item) return;
        switch (item.Kind)
        {
            case ItemKind.Skull:
                item.Gone = true;
                Lose("a skull");
                Play(Sfx.Skull);
                return;
            case ItemKind.Dot:
                item.Gone = true;
                Score += DotPoints * Multiplier;
                Play(Sfx.Dot);
                return;
            case ItemKind.Heart:
                item.Gone = true;
                if (TintOf(item) == Tint.Blue)
                {
                    HeartsBlue++;
                    Effects.Add(new Effect { Kind = EffectKind.Word, X = x, Y = y - 20, Text = $"x{Multiplier}", Colour = 0xFF6AB0FF, Life = 1.2 });
                }
                Award(HeartPoints, x, y);
                Play(Sfx.Heart);
                return;
            case ItemKind.Letter:
                item.Gone = true;
                Award(LetterPoints, x, y);
                Play(Sfx.Letter);
                Light(item.Letter, TintOf(item));
                return;
        }
    }

    /// <summary>A letter eaten red lights its place in SPECIAL; yellow, in EXTRA. A whole word pays out.</summary>
    private void Light(char letter, Tint tint)
    {
        var (word, lit) = tint switch { Tint.Red => (Special, SpecialLit), Tint.Yellow => (Extra, ExtraLit), _ => ("", Array.Empty<bool>()) };
        for (int i = 0; i < word.Length; i++)
            if (word[i] == letter && !lit[i]) { lit[i] = true; break; }
        if (lit.Length == 0 || !lit.All(x => x)) return;
        Array.Fill(lit, false);
        var (x, y) = Field.Centre(Field.NestC, Field.NestR);
        if (word == Special)
        {
            Award(SpecialBonus, x, y, multiply: false);
            Say("SPECIAL!", $"BONUS {SpecialBonus}", 2);
            Play(Sfx.Special);
        }
        else
        {
            Lives++;
            Say("EXTRA!", "AN EXTRA LADYBUG", 2);
            Play(Sfx.Extra);
        }
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    /// <summary>
    /// Out of the nest, the bugs run the paths. At each turning one picks a way it can go (never through a gate,
    /// never straight back unless it must): towards the ladybug, more often in later gardens, otherwise at random.
    /// A bug that runs onto a skull is done for, and the skull with it.
    /// </summary>
    private void MoveBugs(double dt)
    {
        if (Calm || Frozen > 0) return;
        var far = Distances(Player.Square.C, Player.Square.R);
        foreach (var b in Bugs.Where(b => b.Out && !b.Dead))
        {
            double step = BugSpeed * dt;
            while (step > 1e-9)
            {
                if (b.Heading == Dir.None)
                {
                    b.Heading = Choose(b, far);
                    if (b.Heading == Dir.None) break;
                    b.Facing = b.Heading;
                }
                double room = 1 - b.Along;
                if (step < room) { b.Along += step; b.Walk += step; break; }
                step -= room;
                b.Walk += room;
                b.C += b.Heading.Dx();
                b.R += b.Heading.Dy();
                b.Along = 0;
                if (Items.FirstOrDefault(i => !i.Gone && i.Kind == ItemKind.Skull && i.C == b.C && i.R == b.R) is { } skull)
                {
                    skull.Gone = true;
                    b.Dead = true;
                    var (x, y) = Field.Centre(b.C, b.R);
                    Effects.Add(new Effect { Kind = EffectKind.Puff, X = x, Y = y, Life = 0.8 });
                    Play(Sfx.BugDown);
                    break;
                }
                b.Heading = Choose(b, far);
                if (b.Heading == Dir.None) break;
                b.Facing = b.Heading;
            }
        }
        // A bug lost to a skull goes back to the nest to wait its turn again.
        foreach (var b in Bugs.Where(b => b.Dead))
        {
            b.Dead = false;
            b.Out = false;
            b.C = Field.NestC;
            b.R = Field.NestR;
            b.Along = 0;
            b.Heading = Dir.None;
        }
    }

    private Dir Choose(Bug b, int[,] far)
    {
        var ways = Dirs.All.Where(d => Maze.OpenToBug(b.C, b.R, d)).ToList();
        // Into the nest only when coming out of it.
        ways.RemoveAll(d => (b.C + d.Dx(), b.R + d.Dy()) == (Field.NestC, Field.NestR));
        if (ways.Count == 0) return Dir.None;
        if (ways.Count > 1) ways.Remove(b.Heading.Back());
        if (_rng.NextDouble() < Chase)
        {
            var best = ways.OrderBy(d => far[b.C + d.Dx(), b.R + d.Dy()]).First();
            if (far[b.C + best.Dx(), b.R + best.Dy()] < int.MaxValue) return best;
        }
        return ways[_rng.Next(ways.Count)];
    }

    /// <summary>How many steps each square is from one, going the ways a bug can (round the gates as they stand).</summary>
    public int[,] Distances(int c0, int r0)
    {
        var far = new int[Field.Cols, Field.Rows];
        for (int c = 0; c < Field.Cols; c++) for (int r = 0; r < Field.Rows; r++) far[c, r] = int.MaxValue;
        far[c0, r0] = 0;
        var todo = new Queue<(int, int)>();
        todo.Enqueue((c0, r0));
        while (todo.Count > 0)
        {
            var (c, r) = todo.Dequeue();
            foreach (var d in Dirs.All)
                if (Maze.OpenToBug(c, r, d) && far[c + d.Dx(), r + d.Dy()] == int.MaxValue)
                {
                    far[c + d.Dx(), r + d.Dy()] = far[c, r] + 1;
                    todo.Enqueue((c + d.Dx(), r + d.Dy()));
                }
        }
        return far;
    }

    private void Catch()
    {
        var (px, py) = Player.Pos;
        foreach (var b in Bugs.Where(b => b.Out && !b.Dead))
        {
            var (bx, by) = b.Pos;
            if (Math.Abs(bx - px) + Math.Abs(by - py) < CatchDistance * Field.Cell) { Lose("caught"); Play(Sfx.Caught); return; }
        }
    }

    private void Cleared()
    {
        Play(Sfx.LevelClear);
        Say("GARDEN CLEARED!", null, ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Lose(string cause)
    {
        Cause = cause;
        Player.Alive = false;
        Lives--;
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Award(int points, double x, double y, bool multiply = true)
    {
        int p = multiply ? points * Multiplier : points;
        Score += p;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 16, Text = p.ToString(), Life = 0.9 });
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a garden, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>The ladybug in a square, still.</summary>
    internal void PutLadybug(int c, int r)
    {
        Player = new Ladybug { C = c, R = r };
    }

    internal Bug PutBug(int c, int r, Dir heading = Dir.None)
    {
        var b = Bugs.First(b => !b.Out);
        b.Out = true;
        b.C = c;
        b.R = r;
        b.Along = 0;
        b.Heading = heading;
        return b;
    }

    /// <summary>Everything off the garden but what a test puts there.</summary>
    internal void Clear()
    {
        Items.Clear();
        Items.Add(new Item { Kind = ItemKind.Dot, C = 0, R = 0 });
    }

    internal Item PutItem(ItemKind kind, int c, int r, char letter = ' ', int phase = 0)
    {
        Items.RemoveAll(i => i.C == c && i.R == r);
        var item = new Item { Kind = kind, C = c, R = r, Letter = letter, Phase = phase };
        Items.Add(item);
        return item;
    }

    internal void SetTime(double t) => _time = t;
    internal void SetLap(double lap) => Lap = lap;
    internal void ShowVegetable() => Vegetable = true;
    internal void Freeze(double seconds) => Frozen = seconds;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
