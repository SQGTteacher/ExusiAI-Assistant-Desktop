using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ExusiAI.Plugin.MishaShowcase;

internal static class ClassIslandThemeWpfAdapter
{
    public static void ApplyIslandStyle(
        Border border,
        ClassIslandThemeSnapshot snapshot,
        ClassIslandThemeVariant variant,
        bool customBackgroundEnabled)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MainWindowStylesAssist.IsCustomBackgroundColorEnabled"] = customBackgroundEnabled ? "True" : "False"
        };

        ApplySetters(
            border,
            snapshot,
            variant,
            snapshot.ResolveSetters(new ClassIslandThemeTarget(
                "MainWindowBackgroundMaterialControl",
                ["line-background"],
                properties)),
            customBackgroundEnabled);

        ApplySetters(
            border,
            snapshot,
            variant,
            snapshot.ResolveSetters(new ClassIslandThemeTarget(
                "Border",
                ["line-background"],
                properties)),
            customBackgroundEnabled);

        ApplySetters(
            border,
            snapshot,
            variant,
            snapshot.ResolveSetters(new ClassIslandThemeTarget(
                "Border",
                ["line-background-frame"],
                properties)),
            preserveBackground: true);
    }

    private static void ApplySetters(
        Border border,
        ClassIslandThemeSnapshot snapshot,
        ClassIslandThemeVariant variant,
        IReadOnlyDictionary<string, ClassIslandResolvedThemeSetter> setters,
        bool preserveBackground)
    {
        foreach (var pair in setters)
        {
            var property = pair.Key;
            var setter = pair.Value;
            switch (property.ToLowerInvariant())
            {
                case "background" or "fallbackbrush" when !preserveBackground:
                    if (TryResolveBrush(snapshot, variant, setter, out var background))
                        border.Background = background;
                    break;
                case "borderbrush":
                    if (TryResolveBrush(snapshot, variant, setter, out var borderBrush))
                        border.BorderBrush = borderBrush;
                    break;
                case "borderthickness":
                    if (TryResolveLiteral(setter, out var thickness) &&
                        TryParseThickness(thickness, out var parsedThickness))
                        border.BorderThickness = parsedThickness;
                    break;
                case "cornerradius":
                    if (TryResolveLiteral(setter, out var radius) &&
                        TryParseCornerRadius(radius, out var parsedRadius))
                        border.CornerRadius = parsedRadius;
                    break;
                case "padding":
                    if (TryResolveLiteral(setter, out var padding) &&
                        TryParseThickness(padding, out var parsedPadding))
                        border.Padding = parsedPadding;
                    break;
                case "margin":
                    if (TryResolveLiteral(setter, out var margin) &&
                        TryParseThickness(margin, out var parsedMargin))
                        border.Margin = parsedMargin;
                    break;
                case "opacity":
                    if (TryResolveLiteral(setter, out var opacity) &&
                        double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOpacity))
                        border.Opacity = Math.Clamp(parsedOpacity, 0, 1);
                    break;
                case "foreground":
                    if (TryResolveBrush(snapshot, variant, setter, out var foreground))
                        TextElement.SetForeground(border, foreground);
                    break;
                case "fontfamily":
                    if (TryResolveLiteral(setter, out var fontFamily) && !string.IsNullOrWhiteSpace(fontFamily))
                    {
                        try { TextElement.SetFontFamily(border, new FontFamily(fontFamily)); }
                        catch (ArgumentException) { }
                    }
                    break;
                case "fontsize":
                    if (TryResolveLiteral(setter, out var fontSize) &&
                        double.TryParse(fontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFontSize))
                        TextElement.SetFontSize(border, Math.Max(1, parsedFontSize));
                    break;
                case "fontweight":
                    if (TryResolveLiteral(setter, out var fontWeight))
                        TextElement.SetFontWeight(border, ParseFontWeight(fontWeight));
                    break;
                case "boxshadow":
                    if (TryResolveLiteral(setter, out var shadow) &&
                        TryParseDropShadow(shadow, out var effect))
                        border.Effect = effect;
                    break;
                case "width":
                    ApplyLength(setter, value => border.Width = value);
                    break;
                case "height":
                    ApplyLength(setter, value => border.Height = value);
                    break;
                case "minwidth":
                    ApplyLength(setter, value => border.MinWidth = value);
                    break;
                case "minheight":
                    ApplyLength(setter, value => border.MinHeight = value);
                    break;
                case "maxwidth":
                    ApplyLength(setter, value => border.MaxWidth = value);
                    break;
                case "maxheight":
                    ApplyLength(setter, value => border.MaxHeight = value);
                    break;
                case "cliptobounds":
                    if (TryResolveLiteral(setter, out var clip) && bool.TryParse(clip, out var clipToBounds))
                        border.ClipToBounds = clipToBounds;
                    break;
            }
        }
    }

    public static bool TryResolveBrush(
        ClassIslandThemeSnapshot snapshot,
        ClassIslandThemeVariant variant,
        ClassIslandResolvedThemeSetter setter,
        out Brush brush)
    {
        ClassIslandThemeResource? resource = setter.Value.InlineResource;
        if (resource is null && !string.IsNullOrWhiteSpace(setter.Value.ResourceKey))
        {
            if (!setter.LocalResources.TryGetValue(setter.Value.ResourceKey, out resource))
                resource = snapshot.ResolveResource(setter.Value.ResourceKey, variant);
        }

        if (resource is not null && TryCreateBrush(resource, out brush))
            return true;

        if (!string.IsNullOrWhiteSpace(setter.Value.Literal) &&
            TryCreateLiteralBrush(setter.Value.Literal, out brush))
            return true;

        brush = Brushes.Transparent;
        return false;
    }

    public static bool TryCreateBrush(ClassIslandThemeResource resource, out Brush brush)
    {
        switch (resource)
        {
            case ClassIslandThemeColorResource color:
                brush = new SolidColorBrush(ToMediaColor(color.Color));
                return true;

            case ClassIslandThemeSolidBrushResource solid:
                brush = new SolidColorBrush(ToMediaColor(solid.Color))
                {
                    Opacity = Math.Clamp(solid.Opacity, 0, 1)
                };
                return true;

            case ClassIslandThemeLinearGradientResource linear:
                brush = new LinearGradientBrush(
                    ToGradientStops(linear.Stops),
                    ToPoint(linear.StartPoint),
                    ToPoint(linear.EndPoint))
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox
                };
                return true;

            case ClassIslandThemeRadialGradientResource radial:
                brush = new RadialGradientBrush(ToGradientStops(radial.Stops))
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    Center = ToPoint(radial.Center),
                    GradientOrigin = ToPoint(radial.GradientOrigin),
                    RadiusX = Math.Max(0, radial.RadiusX),
                    RadiusY = Math.Max(0, radial.RadiusY)
                };
                return true;

            case ClassIslandThemeConicGradientResource conic:
                brush = new LinearGradientBrush(
                    ToGradientStops(conic.Stops),
                    new Point(0, 0),
                    new Point(1, 1))
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    RelativeTransform = new RotateTransform(conic.Angle, 0.5, 0.5)
                };
                return true;

            case ClassIslandThemeDrawingBrushResource drawing:
                var group = new DrawingGroup();
                foreach (var layer in drawing.Layers)
                {
                    if (!TryCreateBrush(layer.Brush, out var layerBrush))
                        continue;
                    var geometry = CreateGeometry(layer);
                    if (geometry is null)
                        continue;
                    group.Children.Add(new GeometryDrawing(layerBrush, null, geometry));
                }

                if (group.Children.Count == 0)
                    break;

                brush = new DrawingBrush(group)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, 100, 100),
                    ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                    Viewport = new Rect(0, 0, 1, 1)
                };
                return true;

            case ClassIslandThemeLiteralResource literal when TryCreateLiteralBrush(literal.RawValue, out var literalBrush):
                brush = literalBrush;
                return true;
        }

        brush = Brushes.Transparent;
        return false;
    }

    private static Geometry? CreateGeometry(ClassIslandThemeDrawingLayer layer)
    {
        try
        {
            if (layer.GeometryKind.Equals("RectangleGeometry", StringComparison.OrdinalIgnoreCase))
            {
                var parts = layer.GeometryData.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                    return new RectangleGeometry(new Rect(x, y, width, height));
                return new RectangleGeometry(new Rect(0, 0, 100, 100));
            }

            if (!string.IsNullOrWhiteSpace(layer.GeometryData))
                return Geometry.Parse(layer.GeometryData);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
        }

        return null;
    }

    private static bool TryCreateLiteralBrush(string raw, out Brush brush)
    {
        var value = raw.Trim();
        if (ClassIslandThemeColor.TryParseAxaml(value, out var color))
        {
            brush = new SolidColorBrush(ToMediaColor(color));
            return true;
        }

        brush = value.ToLowerInvariant() switch
        {
            "transparent" => Brushes.Transparent,
            "black" => Brushes.Black,
            "white" => Brushes.White,
            "red" => Brushes.Red,
            "dodgerblue" => Brushes.DodgerBlue,
            _ => Brushes.Transparent
        };
        return value.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("black", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("white", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("red", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("dodgerblue", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveLiteral(ClassIslandResolvedThemeSetter setter, out string value)
    {
        if (!string.IsNullOrWhiteSpace(setter.Value.Literal))
        {
            value = setter.Value.Literal;
            return true;
        }

        if (setter.Value.InlineResource is ClassIslandThemeLiteralResource literal)
        {
            value = literal.RawValue;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(setter.Value.ResourceKey) &&
            setter.LocalResources.TryGetValue(setter.Value.ResourceKey, out var local) &&
            local is ClassIslandThemeLiteralResource localLiteral)
        {
            value = localLiteral.RawValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void ApplyLength(ClassIslandResolvedThemeSetter setter, Action<double> apply)
    {
        if (!TryResolveLiteral(setter, out var raw) ||
            !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return;
        apply(Math.Max(0, value));
    }

    private static bool TryParseThickness(string raw, out Thickness thickness)
    {
        var parts = SplitNumbers(raw);
        switch (parts.Count)
        {
            case 1:
                thickness = new Thickness(parts[0]);
                return true;
            case 2:
                thickness = new Thickness(parts[0], parts[1], parts[0], parts[1]);
                return true;
            case 4:
                thickness = new Thickness(parts[0], parts[1], parts[2], parts[3]);
                return true;
            default:
                thickness = default;
                return false;
        }
    }

    private static bool TryParseCornerRadius(string raw, out CornerRadius radius)
    {
        var parts = SplitNumbers(raw);
        switch (parts.Count)
        {
            case 1:
                radius = new CornerRadius(Math.Max(0, parts[0]));
                return true;
            case 4:
                radius = new CornerRadius(
                    Math.Max(0, parts[0]),
                    Math.Max(0, parts[1]),
                    Math.Max(0, parts[2]),
                    Math.Max(0, parts[3]));
                return true;
            default:
                radius = default;
                return false;
        }
    }

    private static List<double> SplitNumbers(string raw)
    {
        var result = new List<double>();
        foreach (var part in raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return [];
            result.Add(value);
        }
        return result;
    }

    private static bool TryParseDropShadow(string raw, out DropShadowEffect effect)
    {
        foreach (var candidate in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 5 || parts[0].Equals("inset", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetX) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetY) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var blur))
                continue;

            var colorToken = parts[^1];
            if (!ClassIslandThemeColor.TryParseAxaml(colorToken, out var color))
                continue;

            var depth = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
            var direction = depth <= 0.0001
                ? 270d
                : Math.Atan2(-offsetY, offsetX) * 180d / Math.PI;

            effect = new DropShadowEffect
            {
                Color = Color.FromRgb(color.R, color.G, color.B),
                Opacity = color.A / 255d,
                BlurRadius = Math.Max(0, blur),
                ShadowDepth = depth,
                Direction = direction
            };
            return true;
        }

        effect = new DropShadowEffect();
        return false;
    }

    private static FontWeight ParseFontWeight(string raw)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            return FontWeight.FromOpenTypeWeight(Math.Clamp(numeric, 1, 999));

        return raw.Trim().ToLowerInvariant() switch
        {
            "thin" => FontWeights.Thin,
            "extralight" or "ultralight" => FontWeights.ExtraLight,
            "light" => FontWeights.Light,
            "medium" => FontWeights.Medium,
            "semibold" or "demibold" => FontWeights.SemiBold,
            "bold" => FontWeights.Bold,
            "extrabold" or "ultrabold" => FontWeights.ExtraBold,
            "black" or "heavy" => FontWeights.Black,
            _ => FontWeights.Normal
        };
    }

    private static GradientStopCollection ToGradientStops(IEnumerable<ClassIslandThemeGradientStop> stops)
    {
        var collection = new GradientStopCollection();
        foreach (var stop in stops.OrderBy(x => x.Offset))
            collection.Add(new GradientStop(ToMediaColor(stop.Color), Math.Clamp(stop.Offset, 0, 1)));
        return collection;
    }

    private static Point ToPoint(ClassIslandThemePoint point) => new(point.X, point.Y);

    private static Color ToMediaColor(ClassIslandThemeColor color) =>
        Color.FromArgb(color.A, color.R, color.G, color.B);
}
