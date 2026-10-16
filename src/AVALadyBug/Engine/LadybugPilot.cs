namespace AVALadyBug.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). At each square it weighs the ways it could go:
/// how far that way is from the nearest thing to eat (never stepping on a skull), and how near the bugs could
/// get to it, going the ways bugs can, round the gates as they stand. Gates it has swung shut behind it count
/// for it, and a bug close ahead turns it straight round.
/// </summary>
public sealed class LadybugPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>Steps since it last turned straight round: it won't again for a moment, or it dithers.</summary>
    private int _sinceTurn = 99;

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; _sinceTurn = 99; return default; }
        _sinceTurn++;
        bool mayTurn = _sinceTurn > 24;
        var m = g.Maze;
        // Where it decides from: the square it's coming to.
        var (nc, nr) = p.Heading == Dir.None ? (p.C, p.R) : (p.C + p.Heading.Dx(), p.R + p.Heading.Dy());
        var food = FoodDistances(g);
        var bugs = g.Bugs.Where(b => b.Out && !b.Dead).Select(b => (Bug: b, Far: g.Distances(b.Square.C, b.Square.R))).ToList();
        double Cost(int c, int r, int extra)
        {
            if (g.Items.Any(i => !i.Gone && i.Kind == ItemKind.Skull && i.C == c && i.R == r)) return 1e6;
            double cost = food[c, r] == int.MaxValue ? 60 : food[c, r] + extra;
            foreach (var (b, far) in bugs)
            {
                int d = far[c, r];
                if (g.Frozen > 0 || d == int.MaxValue) continue;
                cost += d switch { 0 => 200, 1 => 80, 2 => 20, 3 => 4, _ => 0 };
            }
            return cost;
        }
        Dir best = Dir.None;
        double least = double.MaxValue;
        foreach (var d in Dirs.All)
        {
            if (!m.OpenToLadybug(nc, nr, d)) continue;
            if (!mayTurn && d == p.Heading.Back()) continue;
            // Straight back the way it came costs a little, so it doesn't dither.
            double cost = Cost(nc + d.Dx(), nr + d.Dy(), d == p.Heading.Back() ? 2 : 0);
            if (cost < least) { least = cost; best = d; }
        }
        // Nothing but a skull on offer: back after all.
        if (least >= 1e6 && p.Heading != Dir.None && m.OpenToLadybug(nc, nr, p.Heading.Back())) best = p.Heading.Back();
        // A bug close ahead on the way it's going: turn round now.
        if (mayTurn && p.Heading != Dir.None && p.Along > 0.1)
        {
            double ahead = Cost(nc, nr, 0), behind = Cost(p.C, p.R, 1);
            if (behind + 12 < ahead && bugs.Any(x => x.Far[nc, nr] <= 1)) { best = p.Heading.Back(); _sinceTurn = 0; Doing = "running"; return new Controls(best.Dx(), best.Dy()); }
        }
        Doing = least > 40 ? "running" : "eating";
        // Its choice is for the square it's coming to; asking for straight back now would turn it round here
        // instead. So it keeps on, and asks again when it gets there.
        bool skullAhead = g.Items.Any(i => !i.Gone && i.Kind == ItemKind.Skull && i.C == nc && i.R == nr);
        if (skullAhead && p.Heading != Dir.None) { _sinceTurn = 0; return new Controls(p.Heading.Back().Dx(), p.Heading.Back().Dy()); }
        if (best == p.Heading.Back() && p.Heading != Dir.None && p.Along > 0) best = p.Heading;
        else if (best == p.Heading.Back() && best != Dir.None) _sinceTurn = 0;
        return best == Dir.None ? default : new Controls(best.Dx(), best.Dy());
    }

    /// <summary>How many steps each square is from the nearest thing to eat, going the ladybug's ways but never over a skull.</summary>
    private static int[,] FoodDistances(GameEngine g)
    {
        var far = new int[Field.Cols, Field.Rows];
        var todo = new Queue<(int, int)>();
        for (int c = 0; c < Field.Cols; c++) for (int r = 0; r < Field.Rows; r++) far[c, r] = int.MaxValue;
        // The vegetable, while it's there, before anything else: it stops the bugs.
        if (g.Vegetable) { far[Field.NestC, Field.NestR] = 0; todo.Enqueue((Field.NestC, Field.NestR)); }
        else
            foreach (var i in g.Items.Where(i => !i.Gone && i.Kind != ItemKind.Skull))
            {
                far[i.C, i.R] = 0;
                todo.Enqueue((i.C, i.R));
            }
        var skulls = g.Items.Where(i => !i.Gone && i.Kind == ItemKind.Skull).Select(i => (i.C, i.R)).ToHashSet();
        while (todo.Count > 0)
        {
            var (c, r) = todo.Dequeue();
            foreach (var d in Dirs.All)
            {
                int c2 = c + d.Dx(), r2 = r + d.Dy();
                if (!g.Maze.OpenToLadybug(c, r, d) || far[c2, r2] != int.MaxValue || skulls.Contains((c2, r2))) continue;
                far[c2, r2] = far[c, r] + 1;
                todo.Enqueue((c2, r2));
            }
        }
        return far;
    }
}
