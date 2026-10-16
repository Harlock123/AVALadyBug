namespace AVALadyBug.Engine;

/// <summary>The stick: each -1, 0 or 1 (up the screen is -1).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0);

public enum Dir { None, Up, Right, Down, Left }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Right, Dir.Down, Dir.Left];
    public static int Dx(this Dir d) => d switch { Dir.Right => 1, Dir.Left => -1, _ => 0 };
    public static int Dy(this Dir d) => d switch { Dir.Down => 1, Dir.Up => -1, _ => 0 };
    public static Dir Back(this Dir d) => d switch { Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None };
    public static Dir Of(int dx, int dy) => dx > 0 ? Dir.Right : dx < 0 ? Dir.Left : dy > 0 ? Dir.Down : dy < 0 ? Dir.Up : Dir.None;
}

/// <summary>The screen: a garden of 11 by 11 squares in the middle, under the score bar.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 60;
    public const int Cols = 11, Rows = 11;
    public const double Cell = 56;
    public const double Left = (Width - Cols * Cell) / 2, Top = Hud + (Height - Hud - Rows * Cell) / 2;
    /// <summary>Where the bugs wait to come out, and where the ladybug starts.</summary>
    public const int NestC = 5, NestR = 5, StartC = 5, StartR = 8;

    public static (double X, double Y) Centre(int c, int r) => (Left + c * Cell + Cell / 2, Top + r * Cell + Cell / 2);
}

/// <summary>
/// A revolving gate: a bar pivoted at a corner of the grid, lying along the row (blocking the two edges either
/// side of the pivot on that line) or along the column. Pushing through it swings it a quarter turn, so it then
/// blocks the other two edges at that corner. The ladybug can always push through; the bugs can't.
/// </summary>
public sealed class Gate
{
    /// <summary>The pivot: the top-left corner of square (Cx, Cy).</summary>
    public int Cx, Cy;
    public bool Vertical;
    /// <summary>Just swung: counts down from 1 while the drawing turns it.</summary>
    public double Swing;
    public int SwingWay;

    /// <summary>The edges its two arms lie on, as (kind, c, r): 'E' east of square (c, r), 'S' south of it.</summary>
    public IEnumerable<(char Kind, int C, int R)> Arms(bool vertical) => vertical
        ? [('E', Cx - 1, Cy - 1), ('E', Cx - 1, Cy)]
        : [('S', Cx - 1, Cy - 1), ('S', Cx, Cy - 1)];
}

/// <summary>The garden's walls (hedges) and gates.</summary>
public sealed class Maze
{
    /// <summary>The layout: corners at even places, squares at odd; '-' and '|' are hedges, 'H' and 'V' gate pivots lying along the row or the column.</summary>
    public static readonly string[] Plan =
    [
        "+-+-+-+-+-+-+-+-+-+-+-+",
        "|   |             |   |",
        "+ + + + H +-+ H + + + +",
        "| |   |         |   | |",
        "+ + H + + +-+ + + H + +",
        "|       |     |       |",
        "+-+ +-+ + +-+ + +-+ +-+",
        "|                     |",
        "+ H +-+ V + + V +-+ H +",
        "|                     |",
        "+ + V + + + + + + V + +",
        "|         | |         |",
        "+-+ + + + +-+ + + + +-+",
        "|   |   |     |   |   |",
        "+ + + V +-+ +-+ V + + +",
        "| |                 | |",
        "+-+ H +-+ +-+ +-+ H +-+",
        "|         | |         |",
        "+ + +-+ H + + H +-+ + +",
        "|     |   | |   |     |",
        "+ V + + + + + + + + V +",
        "|                     |",
        "+-+-+-+-+-+-+-+-+-+-+-+",
    ];

    private readonly bool[,] _eastWall = new bool[Field.Cols, Field.Rows], _southWall = new bool[Field.Cols, Field.Rows];
    public List<Gate> Gates { get; } = [];

    public static Maze Garden()
    {
        var m = new Maze();
        for (int y = 0; y < Plan.Length; y++)
        for (int x = 0; x < Plan[y].Length; x++)
        {
            char ch = Plan[y][x];
            if (x % 2 == 0 && y % 2 == 0)
            {
                if (ch is 'H' or 'V') m.Gates.Add(new Gate { Cx = x / 2, Cy = y / 2, Vertical = ch == 'V' });
            }
            else if (x % 2 == 0 && y % 2 == 1 && x > 0 && x < Plan[y].Length - 1 && ch == '|') m._eastWall[x / 2 - 1, y / 2] = true;
            else if (x % 2 == 1 && y % 2 == 0 && y > 0 && y < Plan.Length - 1 && ch == '-') m._southWall[x / 2, y / 2 - 1] = true;
        }
        return m;
    }

    /// <summary>The edge leaving square (c, r) in a direction, as (kind, c, r), or null at the garden's edge.</summary>
    public static (char Kind, int C, int R)? Edge(int c, int r, Dir d) => d switch
    {
        Dir.Right when c < Field.Cols - 1 => ('E', c, r),
        Dir.Left when c > 0 => ('E', c - 1, r),
        Dir.Down when r < Field.Rows - 1 => ('S', c, r),
        Dir.Up when r > 0 => ('S', c, r - 1),
        _ => null,
    };

    public bool Hedge((char Kind, int C, int R) e) => e.Kind == 'E' ? _eastWall[e.C, e.R] : _southWall[e.C, e.R];

    /// <summary>The gate with an arm across that edge, as it stands now.</summary>
    public Gate? GateOn((char Kind, int C, int R) e) => Gates.FirstOrDefault(g => g.Arms(g.Vertical).Contains(e));

    /// <summary>Whether the ladybug can go that way: only hedges stop it (it pushes gates round).</summary>
    public bool OpenToLadybug(int c, int r, Dir d) => Edge(c, r, d) is { } e && !Hedge(e);

    /// <summary>Whether a bug can go that way: hedges and gates both stop it.</summary>
    public bool OpenToBug(int c, int r, Dir d) => Edge(c, r, d) is { } e && !Hedge(e) && GateOn(e) == null;
}

/// <summary>Something that runs along the paths: in a square, or part way along to the next one.</summary>
public class Runner
{
    public int C, R;
    public Dir Heading = Dir.None;
    /// <summary>How far along to the next square, 0 to 1.</summary>
    public double Along;
    /// <summary>The way it last faced (for drawing).</summary>
    public Dir Facing = Dir.Up;
    public double Walk;

    public (double X, double Y) Pos
    {
        get
        {
            var (x, y) = Field.Centre(C, R);
            return (x + Heading.Dx() * Along * Field.Cell, y + Heading.Dy() * Along * Field.Cell);
        }
    }

    /// <summary>The square it's nearer to.</summary>
    public (int C, int R) Square => Along < 0.5 ? (C, R) : (C + Heading.Dx(), R + Heading.Dy());
}

public sealed class Ladybug : Runner
{
    /// <summary>The way the stick last asked for: it turns that way at the next chance.</summary>
    public Dir Want = Dir.None;
    public bool Alive = true;
}

public sealed class Bug : Runner
{
    public bool Out, Dead;
    public int Id;
}

public enum ItemKind { Dot, Letter, Heart, Skull }

/// <summary>Letters and hearts change colour as you watch: what they're worth depends on the colour when eaten.</summary>
public enum Tint { Red, Yellow, Blue }

public sealed class Item
{
    public ItemKind Kind;
    public int C, R;
    public char Letter;
    /// <summary>Where in the colour cycle it starts, so they don't all change together.</summary>
    public int Phase;
    public bool Gone;
}

public enum EffectKind { Score, Word, Puff }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
