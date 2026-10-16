using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVALadyBug.Engine;
using Effect = AVALadyBug.Engine.Effect;

namespace AVALadyBug.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVALadyBug/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Leaf = 0xFF6AD06A, Blue = 0xFF5AA0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private static uint TintColour(Tint t) => t switch { Tint.Red => 0xFFE8382A, Tint.Yellow => 0xFFF0C020, _ => Blue };

    /// <summary>The bugs change with the garden.</summary>
    private static readonly string[] BugNames = ["BEETLE", "WEEVIL", "WASP", "MANTIS", "HORNET"];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0C2410), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawGarden(dc, g.Maze);
            DrawTimer(dc, g.Lap, g.Wave);
            DrawNest(dc);
            foreach (var i in g.Items.Where(i => !i.Gone)) DrawItem(dc, i);
            foreach (var b in g.Bugs.Where(b => b.Out))
            {
                var (x, y) = b.Pos;
                DrawBug(dc, x, y, b.Facing, b.Walk, (g.Wave - 1) % BugNames.Length, g.Frozen > 0, 1);
            }
            DrawGates(dc, g.Maze);
            DrawPlayer(dc);
            DrawSides(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the garden ─────────────────────────────

    /// <summary>Lawn squares, and hedges along the walls, leafy and rounded at the ends.</summary>
    private static void DrawGarden(DrawingContext dc, Maze m)
    {
        double L = Field.Left, T = Field.Top, C = Field.Cell, W = Field.Cols * C, H = Field.Rows * C;
        dc.FillRectangle(Brush(0xFF2A5C2C), new Rect(L, T, W, H));
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            if ((c + r) % 2 == 0) dc.FillRectangle(Brush(0xFF2E6230), new Rect(L + c * C, T + r * C, C, C));
            uint q = Hash(c, r, 4);
            for (int k = 0; k < 3; k++)
            {
                uint w = Hash(c * 7 + k, r, 5);
                double x = L + c * C + 6 + w % 44, y = T + r * C + 6 + (w >> 8) % 44;
                dc.DrawLine(Pen(0xFF3E7A3A, 1.5), new Point(x, y), new Point(x - 2 + q % 4, y - 5));
            }
        }
        void Hedge(Point a, Point b)
        {
            dc.DrawLine(Pen(0xFF0E2E12, 13), a, b);
            dc.DrawLine(Pen(0xFF1E5A22, 9), a, b);
            double len = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            for (double s = 4; s < len; s += 9)
            {
                double f = s / len;
                var p = new Point(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
                uint q = Hash((int)p.X, (int)p.Y, 6);
                dc.DrawEllipse(Brush(q % 2 == 0 ? 0xFF3A8A3Au : 0xFF2E722E), null, new Point(p.X + (q % 5) - 2, p.Y + ((q >> 4) % 5) - 2), 2.5, 2.5);
            }
        }
        // The outside.
        Hedge(new Point(L, T), new Point(L + W, T));
        Hedge(new Point(L, T + H), new Point(L + W, T + H));
        Hedge(new Point(L, T), new Point(L, T + H));
        Hedge(new Point(L + W, T), new Point(L + W, T + H));
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            if (c < Field.Cols - 1 && m.Hedge(('E', c, r))) Hedge(new Point(L + (c + 1) * C, T + r * C), new Point(L + (c + 1) * C, T + (r + 1) * C));
            if (r < Field.Rows - 1 && m.Hedge(('S', c, r))) Hedge(new Point(L + c * C, T + (r + 1) * C), new Point(L + (c + 1) * C, T + (r + 1) * C));
        }
    }

    /// <summary>The gates: wooden bars on a post, turning as they swing.</summary>
    private static void DrawGates(DrawingContext dc, Maze m)
    {
        foreach (var gt in m.Gates)
        {
            double px = Field.Left + gt.Cx * Field.Cell, py = Field.Top + gt.Cy * Field.Cell;
            double a = (gt.Vertical ? Math.PI / 2 : 0) - gt.SwingWay * gt.Swing * Math.PI / 2;
            double dx = Math.Cos(a) * (Field.Cell - 6), dy = Math.Sin(a) * (Field.Cell - 6);
            dc.DrawLine(Pen(0xFF5A3A12, 9), new Point(px - dx, py - dy), new Point(px + dx, py + dy));
            dc.DrawLine(Pen(0xFFE8B048, 5), new Point(px - dx, py - dy), new Point(px + dx, py + dy));
            dc.DrawEllipse(Brush(0xFF8A5A22), Pen(0xFF3A2208, 1.5), new Point(px, py), 6, 6);
            dc.DrawEllipse(Brush(0xFFF0D080), null, new Point(px - 1.5, py - 1.5), 2, 2);
        }
    }

    /// <summary>The timer: a track of lamps round the garden, lighting one by one; each time round a bug comes out.</summary>
    private static void DrawTimer(DrawingContext dc, double lap, int wave)
    {
        double L = Field.Left - 16, T = Field.Top - 16, W = Field.Cols * Field.Cell + 32, H = Field.Rows * Field.Cell + 32;
        double perim = 2 * (W + H);
        int lamps = 96;
        uint on = (wave % 3) switch { 1 => 0xFFFFE04A, 2 => 0xFF6AE0FF, _ => 0xFFFF7AE0 };
        for (int i = 0; i < lamps; i++)
        {
            // From the top middle, clockwise.
            double s = (W / 2 + i * perim / lamps) % perim;
            Point p = s < W ? new(L + s, T) : s < W + H ? new(L + W, T + s - W) : s < 2 * W + H ? new(L + W - (s - W - H), T + H) : new(L, T + H - (s - 2 * W - H));
            bool lit = i < lap * lamps;
            dc.FillRectangle(Brush(lit ? on : 0xFF1E3A22), new Rect(p.X - 3, p.Y - 3, 6, 6), 1);
        }
    }

    /// <summary>The nest in the middle: a little hut with the next bug peeping out, or the vegetable once they're all out.</summary>
    private void DrawNest(DrawingContext dc)
    {
        var g = Engine;
        var (x, y) = Field.Centre(Field.NestC, Field.NestR);
        dc.FillRectangle(Brush(0xFF3A2614), new Rect(x - 24, y - 20, 48, 42), 4);
        dc.FillRectangle(Brush(0xFF5A3A1E), new Rect(x - 20, y - 16, 40, 34), 3);
        if (g.Vegetable) DrawVegetable(dc, x, y, g.Wave, 1);
        else if (g.Bugs.Any(b => !b.Out)) DrawBug(dc, x, y + 2, Dir.Up, _clock, (g.Wave - 1) % BugNames.Length, false, 0.8);
    }

    private void DrawItem(DrawingContext dc, Item i)
    {
        var (x, y) = Field.Centre(i.C, i.R);
        switch (i.Kind)
        {
            case ItemKind.Dot:
                for (int k = 0; k < 5; k++)
                {
                    double a = k * Math.PI * 2 / 5;
                    dc.DrawEllipse(Brush(0xFFF6F2E0), null, new Point(x + Math.Cos(a) * 3, y + Math.Sin(a) * 3), 2.2, 2.2);
                }
                dc.DrawEllipse(Brush(0xFFF0C030), null, new Point(x, y), 1.8, 1.8);
                break;
            case ItemKind.Letter:
            {
                uint c = TintColour(Engine.TintOf(i));
                dc.DrawEllipse(Brush(c), Pen(0xFF1A1A1A, 2), new Point(x, y), 16, 16);
                Text(dc, i.Letter.ToString(), new Point(x, y + 1), 26, White, center: true);
                break;
            }
            case ItemKind.Heart:
                DrawHeart(dc, x, y, TintColour(Engine.TintOf(i)), 1);
                break;
            case ItemKind.Skull:
                DrawSkull(dc, x, y, 1);
                break;
        }
    }

    private static void DrawHeart(DrawingContext dc, double x, double y, uint colour, double k)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(new Point(x, y + 13 * k), true);
            c.CubicBezierTo(new Point(x - 20 * k, y - 1 * k), new Point(x - 10 * k, y - 16 * k), new Point(x, y - 6 * k));
            c.CubicBezierTo(new Point(x + 10 * k, y - 16 * k), new Point(x + 20 * k, y - 1 * k), new Point(x, y + 13 * k));
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(colour), Pen(0xFF1A1A1A, 2), geo);
        dc.DrawEllipse(Brush(0x80FFFFFF), null, new Point(x - 6 * k, y - 4 * k), 3 * k, 2 * k);
    }

    private static void DrawSkull(DrawingContext dc, double x, double y, double k)
    {
        dc.DrawEllipse(Brush(0xFFF0EEE6), Pen(0xFF3A3A3A, 1.5), new Point(x, y - 3 * k), 13 * k, 12 * k);
        dc.FillRectangle(Brush(0xFFF0EEE6), new Rect(x - 7 * k, y + 4 * k, 14 * k, 8 * k), (float)(2 * k));
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 5 * k, y - 3 * k), 3.6 * k, 4 * k);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + 5 * k, y - 3 * k), 3.6 * k, 4 * k);
        Poly(dc, 0xFF1A1A1A, null, 0, new Point(x, y + 1 * k), new Point(x - 2 * k, y + 4 * k), new Point(x + 2 * k, y + 4 * k));
        for (int i = -1; i <= 1; i++) dc.DrawLine(Pen(0xFF3A3A3A, 1), new Point(x + i * 3.5 * k, y + 7 * k), new Point(x + i * 3.5 * k, y + 12 * k));
    }

    /// <summary>The garden's vegetable: carrot, radish, cucumber, aubergine, then round again.</summary>
    private static void DrawVegetable(DrawingContext dc, double x, double y, int wave, double k)
    {
        switch ((wave - 1) % 4)
        {
            case 0:
                Poly(dc, 0xFFF08A2A, 0xFF8A4A10, 1.5, new Point(x - 8 * k, y - 8 * k), new Point(x + 8 * k, y - 8 * k), new Point(x, y + 16 * k));
                for (int i = -1; i <= 1; i++) dc.DrawLine(Pen(0xFF3AA03A, 3 * k), new Point(x, y - 8 * k), new Point(x + i * 7 * k, y - 18 * k));
                break;
            case 1:
                dc.DrawEllipse(Brush(0xFFE0405A), Pen(0xFF7A1A2A, 1.5), new Point(x, y + 2 * k), 11 * k, 11 * k);
                dc.DrawLine(Pen(0xFFF0E0E0, 2 * k), new Point(x, y + 13 * k), new Point(x, y + 18 * k));
                for (int i = -1; i <= 1; i++) dc.DrawLine(Pen(0xFF3AA03A, 3 * k), new Point(x, y - 8 * k), new Point(x + i * 6 * k, y - 18 * k));
                break;
            case 2:
                using (dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(-0.6) * Matrix.CreateTranslation(x, y)))
                {
                    dc.DrawEllipse(Brush(0xFF3A8A3A), Pen(0xFF1A4A1A, 1.5), new Point(x, y), 8 * k, 18 * k);
                    for (int i = -2; i <= 2; i++) dc.DrawEllipse(Brush(0xFF8AD08A), null, new Point(x + (i % 2) * 3 * k, y + i * 6 * k), 1.2 * k, 1.2 * k);
                }
                break;
            default:
                dc.DrawEllipse(Brush(0xFF6A2A8A), Pen(0xFF2A0A3A, 1.5), new Point(x, y + 3 * k), 10 * k, 15 * k);
                Poly(dc, 0xFF3AA03A, null, 0, new Point(x - 8 * k, y - 10 * k), new Point(x + 8 * k, y - 10 * k), new Point(x, y - 4 * k));
                dc.DrawLine(Pen(0xFF3AA03A, 3 * k), new Point(x, y - 10 * k), new Point(x, y - 17 * k));
                break;
        }
    }

    // ───────────────────────────── creatures ─────────────────────────────

    private static double AngleOf(Dir d) => d switch { Dir.Right => Math.PI / 2, Dir.Down => Math.PI, Dir.Left => -Math.PI / 2, _ => 0 };

    private void DrawPlayer(DrawingContext dc)
    {
        var g = Engine;
        var p = g.Player;
        var (x, y) = p.Pos;
        if (!p.Alive)
        {
            // Over on its back, legs waving.
            double t = g.PhaseTime;
            dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x, y), 13, 15);
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3 + Math.Sin(t * 20 + i) * 0.3;
                dc.DrawLine(Pen(0xFF1A1A1A, 2), new Point(x, y), new Point(x + Math.Cos(a) * 19, y + Math.Sin(a) * 19));
            }
            if (g.Cause == "a skull") DrawSkull(dc, x, y - 26, 0.8);
            return;
        }
        DrawLadybug(dc, x, y, p.Facing, p.Walk, 1);
    }

    /// <summary>The ladybug: a red shell split down the middle with black spots, a black head with white eyes, legs going.</summary>
    private static void DrawLadybug(DrawingContext dc, double x, double y, Dir facing, double walk, double k)
    {
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(AngleOf(facing)) * Matrix.CreateTranslation(x, y));
        for (int i = -1; i <= 1; i++)
        {
            double sw = Math.Sin(walk * 18 + i * 2) * 3 * k;
            dc.DrawLine(Pen(0xFF1A1A1A, 2 * k), new Point(x - 10 * k, y + i * 7 * k), new Point(x - 18 * k, y + i * 8 * k + sw));
            dc.DrawLine(Pen(0xFF1A1A1A, 2 * k), new Point(x + 10 * k, y + i * 7 * k), new Point(x + 18 * k, y + i * 8 * k - sw));
        }
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x, y - 13 * k), 8 * k, 7 * k);
        dc.DrawEllipse(Brush(White), null, new Point(x - 4 * k, y - 16 * k), 2.2 * k, 2.2 * k);
        dc.DrawEllipse(Brush(White), null, new Point(x + 4 * k, y - 16 * k), 2.2 * k, 2.2 * k);
        dc.DrawEllipse(Brush(0xFFE8241A), Pen(0xFF6A0A08, 1.5), new Point(x, y + 2 * k), 14 * k, 15 * k);
        dc.DrawLine(Pen(0xFF1A1A1A, 2 * k), new Point(x, y - 12 * k), new Point(x, y + 17 * k));
        foreach (var (sx, sy, r) in new[] { (-7.0, -4.0, 3.5), (7.0, -4.0, 3.5), (-8.0, 6.0, 3.0), (8.0, 6.0, 3.0), (-4.0, 12.0, 2.4), (4.0, 12.0, 2.4) })
            dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + sx * k, y + sy * k), r * k, r * k);
        dc.DrawEllipse(Brush(0x70FFFFFF), null, new Point(x - 6 * k, y - 6 * k), 3 * k, 2 * k);
    }

    /// <summary>One of the five kinds of bug, by garden: each its own colours and shape.</summary>
    private static void DrawBug(DrawingContext dc, double x, double y, Dir facing, double walk, int kind, bool frozen, double k)
    {
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(AngleOf(facing)) * Matrix.CreateTranslation(x, y));
        uint body = frozen ? 0xFF8AB0D0 : kind switch { 0 => 0xFF2A9A5A, 1 => 0xFF8A5A2A, 2 => 0xFFF0C020, 3 => 0xFF8AD04A, _ => 0xFFF07A2A };
        uint dark = frozen ? 0xFF4A6A8A : kind switch { 0 => 0xFF0E4A2A, 1 => 0xFF4A2A10, 2 => 0xFF2A2210, 3 => 0xFF3A6A1A, _ => 0xFF5A2A0A };
        double sw = frozen ? 0 : Math.Sin(walk * 18) * 3 * k;
        for (int i = -1; i <= 1; i++)
        {
            dc.DrawLine(Pen(dark, 2 * k), new Point(x - 8 * k, y + i * 7 * k), new Point(x - 16 * k, y + i * 9 * k + sw * (i % 2 == 0 ? 1 : -1)));
            dc.DrawLine(Pen(dark, 2 * k), new Point(x + 8 * k, y + i * 7 * k), new Point(x + 16 * k, y + i * 9 * k - sw * (i % 2 == 0 ? 1 : -1)));
        }
        switch (kind)
        {
            case 1: // A weevil: a long snout.
                dc.DrawLine(Pen(dark, 4 * k), new Point(x, y - 12 * k), new Point(x, y - 24 * k));
                break;
            case 2:
            case 4: // Wasps and hornets: wings.
                double flap = frozen ? 0.4 : 0.4 + Math.Sin(walk * 60) * 0.25;
                dc.DrawEllipse(Brush(0x80E0F0FF), Pen(0x60FFFFFF, 1), new Point(x - 10 * k, y - 2 * k), 9 * k, 5 * k * (1 + flap));
                dc.DrawEllipse(Brush(0x80E0F0FF), Pen(0x60FFFFFF, 1), new Point(x + 10 * k, y - 2 * k), 9 * k, 5 * k * (1 + flap));
                break;
            case 3: // A mantis: forelegs held up.
                dc.DrawLine(Pen(body, 3 * k), new Point(x - 4 * k, y - 10 * k), new Point(x - 10 * k, y - 22 * k));
                dc.DrawLine(Pen(body, 3 * k), new Point(x + 4 * k, y - 10 * k), new Point(x + 10 * k, y - 22 * k));
                break;
        }
        dc.DrawEllipse(Brush(dark), null, new Point(x, y - 12 * k), 6.5 * k, 6 * k);
        dc.DrawEllipse(Brush(0xFFFF4A3A), null, new Point(x - 3 * k, y - 15 * k), 1.8 * k, 1.8 * k);
        dc.DrawEllipse(Brush(0xFFFF4A3A), null, new Point(x + 3 * k, y - 15 * k), 1.8 * k, 1.8 * k);
        dc.DrawLine(Pen(dark, 1.5 * k), new Point(x - 3 * k, y - 17 * k), new Point(x - 8 * k, y - 24 * k));
        dc.DrawLine(Pen(dark, 1.5 * k), new Point(x + 3 * k, y - 17 * k), new Point(x + 8 * k, y - 24 * k));
        dc.DrawEllipse(Brush(body), Pen(dark, 1.5), new Point(x, y + 3 * k), kind == 3 ? 7 * k : 11 * k, 14 * k);
        if (kind is 2 or 4)
            for (int i = 0; i < 3; i++) dc.FillRectangle(Brush(dark), new Rect(x - 9 * k, y - 2 * k + i * 6 * k, 18 * k, 2.5 * k));
        else dc.DrawLine(Pen(dark, 1.5 * k), new Point(x, y - 9 * k), new Point(x, y + 16 * k));
        dc.DrawEllipse(Brush(0x50FFFFFF), null, new Point(x - 4 * k, y - 3 * k), 2.5 * k, 2 * k);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        if (!InGame) return;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 22), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 28), 30, e.Colour, center: true);
                break;
            case EffectKind.Puff:
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3, d = 6 + 22 * t;
                    dc.DrawEllipse(Brush(Alpha(0xFFE0E0E0, 1 - t)), null, new Point(e.X + Math.Cos(a) * d, e.Y + Math.Sin(a) * d), 6 * (1 - t) + 2, 6 * (1 - t) + 2);
                }
                break;
        }
    }

    // ───────────────────────────── the score bar, the sides, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF081A0C), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF2E722E), new Rect(0, Field.Hud - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 20), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(86, 20), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 20), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 20), 28, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"GARDEN {g.Wave}", new Point(512, 20), 28, 0xFFB8F0B0, center: true);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawLadybug(dc, 300 + i * 26, 30, Dir.Up, 0, 0.55);
        if (g.Multiplier > 1) Text(dc, $"x{g.Multiplier}", new Point(700, 20), 30, Blue, center: true);
    }

    /// <summary>Beside the garden: SPECIAL down the left (red letters) and EXTRA down the right (yellow).</summary>
    private void DrawSides(DrawingContext dc)
    {
        var g = Engine;
        void Word(string word, bool[] lit, uint colour, double x)
        {
            for (int i = 0; i < word.Length; i++)
            {
                double y = Field.Top + 40 + i * 62;
                dc.DrawEllipse(Brush(lit[i] ? colour : 0xFF1A2A1C), Pen(lit[i] ? 0xFFFFFFFF : 0xFF3A5A3C, 2), new Point(x, y), 22, 22);
                Text(dc, word[i].ToString(), new Point(x, y + 1), 32, lit[i] ? White : 0xFF4A6A4C, center: true, shadow: false);
            }
        }
        Word(GameEngine.Special, g.SpecialLit, 0xFFE8382A, Field.Left / 2 - 6);
        Word(GameEngine.Extra, g.ExtraLit, 0xFFF0C020, Field.Width - Field.Left / 2 + 6);
        // The bugs still to come, under EXTRA.
        int waiting = g.Bugs.Count(b => !b.Out);
        for (int i = 0; i < waiting; i++) DrawBug(dc, Field.Width - Field.Left / 2 + 6 - 30 + i * 20, Field.Top + 380, Dir.Up, 0, (g.Wave - 1) % BugNames.Length, false, 0.6);
        if (g.Frozen > 0) Text(dc, $"FROZEN {Math.Ceiling(g.Frozen):0}", new Point(Field.Width - Field.Left / 2 + 6, Field.Top + 430), 22, Blue, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 250;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xD00C2410), new Rect(cx - w / 2, cy - 46, w, h), 8);
        dc.DrawRectangle(null, Pen(0xFFE8B048, 2), new Rect(cx - w / 2, cy - 46, w, h), 8);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0C2410), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        for (int i = 0; i < 60; i++)
        {
            uint q = Hash(i, 1, 2);
            double x = q % 1024, y = Field.Hud + (q >> 10) % 700;
            for (int k = 0; k < 5; k++) dc.DrawEllipse(Brush(0x40F6F2E0), null, new Point(x + Math.Cos(k * 1.26) * 3, y + Math.Sin(k * 1.26) * 3), 2, 2);
        }
        string title = "AVA LADY BUG";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 26 + 26 + i * 52, 124 + bob), 104, i % 2 == 0 ? 0xFFE8382A : 0xFFF6F2E0, center: true);
        }
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 190), 28, 0xFFB8F0B0, center: true);
        // The ladybug pushing through a gate, a beetle behind it.
        double t = _clock % 4 / 4;
        DrawLadybug(dc, cx - 160 + t * 320, 262, Dir.Right, _clock, 1.6);
        DrawBug(dc, cx - 300 + t * 320, 262, Dir.Right, _clock, 0, false, 1.4);
        double ga = Math.Min(1, Math.Max(0, (t - 0.45) * 6)) * Math.PI / 2;
        dc.DrawLine(Pen(0xFF5A3A12, 10), new Point(cx + 90 - Math.Sin(ga) * 40, 262 - Math.Cos(ga) * 40), new Point(cx + 90 + Math.Sin(ga) * 40, 262 + Math.Cos(ga) * 40));
        dc.DrawLine(Pen(0xFFE8B048, 6), new Point(cx + 90 - Math.Sin(ga) * 40, 262 - Math.Cos(ga) * 40), new Point(cx + 90 + Math.Sin(ga) * 40, 262 + Math.Cos(ga) * 40));
        dc.DrawEllipse(Brush(0xFF8A5A22), null, new Point(cx + 90, 262), 7, 7);
        Text(dc, "PUSH THROUGH THE GATES TO SWING THEM - BUGS CAN'T PASS A GATE", new Point(cx, 322), 23, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("FLOWER", GameEngine.DotPoints.ToString(), x => { for (int k = 0; k < 5; k++) dc.DrawEllipse(Brush(0xFFF6F2E0), null, new Point(x + Math.Cos(k * 1.26) * 6, 392 + Math.Sin(k * 1.26) * 6), 4.5, 4.5); dc.DrawEllipse(Brush(0xFFF0C030), null, new Point(x, 392), 3.5, 3.5); }),
            ("LETTER", GameEngine.LetterPoints.ToString(), x => { uint c = TintColour((Tint)((int)(_clock / GameEngine.TintSeconds) % 3)); dc.DrawEllipse(Brush(c), Pen(0xFF1A1A1A, 2), new Point(x, 392), 22, 22); Text(dc, "S", new Point(x, 393), 34, White, center: true); }),
            ("HEART", GameEngine.HeartPoints.ToString(), x => DrawHeart(dc, x, 392, TintColour((Tint)(((int)(_clock / GameEngine.TintSeconds) + 1) % 3)), 1.4)),
            ("VEGETABLE", $"{1000}+", x => DrawVegetable(dc, x, 392, 1, 1.4)),
            ("SKULL", "DON'T", x => DrawSkull(dc, x, 392, 1.4)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double lx = cx - 340 + i * 170;
            legend[i].Draw(lx);
            Text(dc, legend[i].Name, new Point(lx, 438), 21, 0xFFB8F0B0, center: true);
            Text(dc, legend[i].Points, new Point(lx, 462), 22, Yellow, center: true);
        }
        Text(dc, "RED LETTERS SPELL SPECIAL (10000) - YELLOW SPELL EXTRA (A LADYBUG) - BLUE HEARTS: x2 x3 x5", new Point(cx, 500), 20, 0xFFB8F0B0, center: true);
        Text(dc, "THE VEGETABLE APPEARS ONCE ALL THE BUGS ARE OUT: EAT IT TO FREEZE THEM", new Point(cx, 526), 20, 0xFFB8F0B0, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 590), 46, Yellow, center: true);
        Text(dc, "ARROWS: RUN (IT TURNS AT THE NEXT CHANCE)    P: PAUSE    M: SOUND    GAMEPAD: STICK", new Point(cx, 650), 21, 0xFFB8F0B0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0C2410), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE BEST GARDENERS", new Point(cx, 140), 64, 0xFFE8382A, center: true);
        Text(dc, "RANK NAME    SCORE  GARDEN", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0C2410), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
