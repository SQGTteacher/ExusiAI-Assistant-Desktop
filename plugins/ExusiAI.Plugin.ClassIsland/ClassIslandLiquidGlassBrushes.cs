using System.Windows;
using System.Windows.Media;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>WPF translation of the supplied SQGT Liquid Glass Crystal brush layers.</summary>
internal static class ClassIslandLiquidGlassBrushes
{
    private static readonly Rect Area = new(0, 0, 100, 100);
    public static Brush DarkSurface { get; } = CreateSurface(dark: true);
    public static Brush LightSurface { get; } = CreateSurface(dark: false);
    public static Brush DarkEdge { get; } = CreateEdge(dark: true);
    public static Brush LightEdge { get; } = CreateEdge(dark: false);

    private static Brush CreateSurface(bool dark)
    {
        var drawing = new DrawingGroup();
        // All five layers and their stops are copied from Styles.axaml's Default/Crystal resources.
        drawing.Children.Add(Fill(dark
            ? Vertical((0, "#50091729"), (.16, "#3B0A1625"), (.4, "#2A08121E"), (.68, "#30091424"), (1, "#480C1C30"))
            : Vertical((0, "#30F5FAFF"), (.16, "#20F6FBFF"), (.4, "#14F9FCFF"), (.68, "#18F7FBFF"), (1, "#2CEEF7FF"))));
        drawing.Children.Add(Fill(Vertical((0, "#B8FFFFFF"), (.018, "#86FFFFFF"), (.044, "#3AFFFFFF"), (.105, "#00FFFFFF"), (.84, "#00F2FAFF"), (.925, "#0EF2FAFF"), (.97, "#54F2FAFF"), (1, "#B0FFFFFF"))));
        drawing.Children.Add(Fill(Radial(new Point(.21, -.05), .43, .30, (0, "#9EFFFFFF"), (.28, "#65FFFFFF"), (.64, "#19FFFFFF"), (1, "#00FFFFFF"))));
        drawing.Children.Add(Fill(Radial(new Point(.83, 1.05), .36, .27, (0, "#94F1FAFF"), (.26, "#57F1FAFF"), (.64, "#15E7F5FF"), (1, "#00E7F5FF"))));
        drawing.Children.Add(Fill(new LinearGradientBrush(Stops((0, "#66FFFFFF"), (.018, "#2EFFFFFF"), (.08, "#00FFFFFF"), (.91, "#00E9F7FF"), (.982, "#22E9F7FF"), (1, "#52F1FAFF")), new Point(0, 0), new Point(1, 0))));
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    private static Brush CreateEdge(bool dark)
    {
        var lightStops = new (double Offset, Color Color)[]
        {
            (0, Parse("#F5FFFFFF")), (.045, Parse("#E8FFFFFF")), (.145, Parse("#65758CA3")),
            (.25, Parse("#282E4155")), (.36, Parse("#5B92B2CF")), (.465, Parse("#DFF4FCFF")),
            (.515, Parse("#F8FFFFFF")), (.605, Parse("#A9C7DEEF")), (.735, Parse("#35384C60")),
            (.855, Parse("#9ED4EAF7")), (.95, Parse("#EAFFFFFF")), (1, Parse("#F5FFFFFF"))
        };
        var darkStops = new (double Offset, Color Color)[]
        {
            (0, Parse("#EFFFFFFF")), (.045, Parse("#DDF8FFFF")), (.145, Parse("#687F9EB5")),
            (.25, Parse("#2616293E")), (.36, Parse("#597CA5C7")), (.465, Parse("#D0E4F6FF")),
            (.515, Parse("#EEFFFFFF")), (.605, Parse("#8AA8CBE8")), (.735, Parse("#2511263F")),
            (.855, Parse("#8EC6E7FC")), (.95, Parse("#DDEEFFFE")), (1, Parse("#EFFFFFFF"))
        };
        var stops = dark ? darkStops : lightStops;
        var drawing = new DrawingGroup();
        // Avalonia's conic brush has no WPF equivalent. Sample it at half-degree
        // intervals; neighboring sectors overlap slightly to avoid hairline seams.
        const int count = 720;
        for (var index = 0; index < count; index++)
        {
            var from = index * 2 * Math.PI / count - Math.PI / 2;
            var to = (index + 1.03) * 2 * Math.PI / count - Math.PI / 2;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(50, 50), true, true);
                context.LineTo(new Point(50 + 100 * Math.Cos(from), 50 + 100 * Math.Sin(from)), true, false);
                context.LineTo(new Point(50 + 100 * Math.Cos(to), 50 + 100 * Math.Sin(to)), true, false);
            }
            geometry.Freeze();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Sample(stops, (index + .5) / count)), null, geometry));
        }
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill, Viewbox = Area, ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    private static Color Sample((double Offset, Color Color)[] stops, double position)
    {
        for (var i = 1; i < stops.Length; i++)
        {
            if (position > stops[i].Offset) continue;
            var fraction = (position - stops[i - 1].Offset) / (stops[i].Offset - stops[i - 1].Offset);
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * fraction);
            var left = stops[i - 1].Color; var right = stops[i].Color;
            return Color.FromArgb(Mix(left.A, right.A), Mix(left.R, right.R), Mix(left.G, right.G), Mix(left.B, right.B));
        }
        return stops[^1].Color;
    }

    private static GeometryDrawing Fill(Brush brush) => new(brush, null, new RectangleGeometry(Area));
    private static LinearGradientBrush Vertical(params (double Offset, string Color)[] colors) =>
        new(Stops(colors), new Point(0, 0), new Point(0, 1));
    private static RadialGradientBrush Radial(Point center, double radiusX, double radiusY, params (double Offset, string Color)[] colors) =>
        new(Stops(colors)) { Center = center, GradientOrigin = center, RadiusX = radiusX, RadiusY = radiusY };
    private static GradientStopCollection Stops(params (double Offset, string Color)[] colors) =>
        new(colors.Select(x => new GradientStop(Parse(x.Color), x.Offset)));
    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
