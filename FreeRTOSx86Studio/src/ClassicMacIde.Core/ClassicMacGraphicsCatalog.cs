using System.Globalization;
using System.Text;

namespace ClassicMacIde.Core;

internal static class ClassicMacGraphicsCatalog
{
    public static IReadOnlyList<DesignerWidgetDefinition> CreateDefinitions() =>
    [
        Define(
            "graphic-line",
            "Line",
            "graphic-line",
            80,
            32,
            GraphicShape.Line,
            properties: LineProperties()),
        Define("graphic-rectangle-outline", "Rectangle (Outline)", "graphic-rectangle", 64, 40, GraphicShape.Rectangle),
        Define("graphic-rectangle-filled", "Rectangle (Solid)", "graphic-rectangle", 64, 40, GraphicShape.Rectangle, GraphicFill.Solid),
        Define("graphic-rectangle-dithered", "Rectangle (Dither)", "graphic-rectangle", 64, 40, GraphicShape.Rectangle, GraphicFill.Dithered),
        Define("graphic-circle-outline", "Circle / Oval (Outline)", "graphic-circle", 40, 40, GraphicShape.Oval),
        Define("graphic-circle-filled", "Circle / Oval (Solid)", "graphic-circle", 40, 40, GraphicShape.Oval, GraphicFill.Solid),
        Define("graphic-circle-dithered", "Circle / Oval (Dither)", "graphic-circle", 40, 40, GraphicShape.Oval, GraphicFill.Dithered),
        Define("graphic-arc-outline", "Arc (Outline)", "graphic-arc", 64, 48, GraphicShape.Arc, properties: ArcProperties()),
        Define("graphic-arc-filled", "Arc (Solid)", "graphic-arc", 64, 48, GraphicShape.Arc, GraphicFill.Solid, ArcProperties()),
        Define("graphic-arc-dithered", "Arc (Dither)", "graphic-arc", 64, 48, GraphicShape.Arc, GraphicFill.Dithered, ArcProperties()),
    ];

    private static DesignerWidgetDefinition Define(
        string id,
        string displayName,
        string previewRendererId,
        double width,
        double height,
        GraphicShape shape,
        GraphicFill fill = GraphicFill.Outline,
        IReadOnlyList<DesignerWidgetPropertyDefinition>? properties = null)
    {
        var allProperties = (properties ?? [])
            .Concat(ColorProperties(fill))
            .ToArray();
        return new(
            id,
            displayName,
            previewRendererId,
            width,
            height,
            8,
            8,
            string.Empty,
            false,
            allProperties,
            new GraphicWidgetCodeGenerator(id, shape, fill),
            DesignerWidgetCategory.Graphics);
    }

    private static DesignerWidgetPropertyDefinition[] ColorProperties(
        GraphicFill fill)
    {
        var properties = new List<DesignerWidgetPropertyDefinition>
        {
            new(
                "color",
                "Color",
                DesignerWidgetPropertyEditorKind.Color,
                "#000000"),
        };
        if (fill == GraphicFill.Dithered)
        {
            properties.Add(new DesignerWidgetPropertyDefinition(
                "backgroundColor",
                "Dither background",
                DesignerWidgetPropertyEditorKind.Color,
                "#FFFFFF"));
        }

        return properties.ToArray();
    }

    private static DesignerWidgetPropertyDefinition[] ArcProperties() =>
    [
        new(
            "startAngle",
            "Start angle",
            DesignerWidgetPropertyEditorKind.Integer,
            "0",
            MinimumValue: 0,
            MaximumValue: 359),
        new(
            "sweepAngle",
            "Sweep angle",
            DesignerWidgetPropertyEditorKind.Integer,
            "180",
            MinimumValue: 1,
            MaximumValue: 359),
    ];

    private static DesignerWidgetPropertyDefinition[] LineProperties() =>
    [
        new(
            "startX",
            "Start X (%)",
            DesignerWidgetPropertyEditorKind.Integer,
            "0",
            MinimumValue: 0,
            MaximumValue: 100),
        new(
            "startY",
            "Start Y (%)",
            DesignerWidgetPropertyEditorKind.Integer,
            "0",
            MinimumValue: 0,
            MaximumValue: 100),
        new(
            "endX",
            "End X (%)",
            DesignerWidgetPropertyEditorKind.Integer,
            "100",
            MinimumValue: 0,
            MaximumValue: 100),
        new(
            "endY",
            "End Y (%)",
            DesignerWidgetPropertyEditorKind.Integer,
            "100",
            MinimumValue: 0,
            MaximumValue: 100),
    ];

    private enum GraphicShape
    {
        Line,
        Rectangle,
        Oval,
        Arc,
    }

    private enum GraphicFill
    {
        Outline,
        Solid,
        Dithered,
    }

    private sealed class GraphicWidgetCodeGenerator(
        string widgetTypeId,
        GraphicShape shape,
        GraphicFill fill) : MacWidgetCodeGenerator
    {
        public override string WidgetTypeId { get; } = widgetTypeId;

        public override void AppendResourceItem(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(context);
            source.AppendLine(
                $"        {{ {context.Y}, {context.X}, {context.Y + context.Height}, {context.X + context.Width} }}, UserItem {{ enabled }}{(context.IsLastItem ? string.Empty : ";")}");
        }

        public override void AppendDeclarations(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(context);
            var suffix = Suffix(context);
            source.AppendLine($"static pascal void DrawGraphic{suffix}(DialogPtr dialog, short item)");
            source.AppendLine("{");
            source.AppendLine("    short itemType;");
            source.AppendLine("    Handle itemHandle;");
            source.AppendLine("    Rect bounds;");
            source.AppendLine("    GrafPtr previousPort;");
            if (context.Profile.ColorMode == DisplayColorMode.Color)
            {
                var drawColor = ClassicMacRgbColor.Parse(
                    context.Control.Properties.GetValueOrDefault("color") ?? "#000000");
                source.AppendLine(
                    $"    RGBColor drawColor{suffix} = {{ 0x{drawColor.Red16:X4}, 0x{drawColor.Green16:X4}, 0x{drawColor.Blue16:X4} }};");
                if (fill == GraphicFill.Dithered)
                {
                    var backgroundColor = ClassicMacRgbColor.Parse(
                        context.Control.Properties.GetValueOrDefault("backgroundColor") ??
                            "#FFFFFF");
                    source.AppendLine(
                        $"    RGBColor backgroundColor{suffix} = {{ 0x{backgroundColor.Red16:X4}, 0x{backgroundColor.Green16:X4}, 0x{backgroundColor.Blue16:X4} }};");
                }
            }
            source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &bounds);");
            source.AppendLine("    (void)itemType;");
            source.AppendLine("    (void)itemHandle;");
            source.AppendLine("    GetPort(&previousPort);");
            source.AppendLine("    SetPort((GrafPtr)dialog);");
            source.AppendLine("    PenNormal();");
            if (context.Profile.ColorMode == DisplayColorMode.Color)
            {
                source.AppendLine($"    RGBForeColor(&drawColor{suffix});");
                if (fill == GraphicFill.Dithered)
                {
                    source.AppendLine($"    RGBBackColor(&backgroundColor{suffix});");
                }
            }
            else
            {
                source.AppendLine("    ForeColor(blackColor);");
            }
            source.AppendLine("    bounds.left++;");
            source.AppendLine("    bounds.top++;");
            source.AppendLine("    bounds.right--;");
            source.AppendLine("    bounds.bottom--;");
            source.AppendLine("    if (bounds.right > bounds.left && bounds.bottom > bounds.top)");
            source.AppendLine("    {");
            AppendDrawCall(source, context);
            source.AppendLine("    }");
            if (context.Profile.ColorMode == DisplayColorMode.Color)
            {
                source.AppendLine("    ForeColor(blackColor);");
                if (fill == GraphicFill.Dithered)
                {
                    source.AppendLine("    BackColor(whiteColor);");
                }
            }
            source.AppendLine("    SetPort(previousPort);");
            source.AppendLine("}");
            source.AppendLine();
        }

        public override void AppendInitialization(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            source.AppendLine("    {");
            source.AppendLine("        short itemType;");
            source.AppendLine("        Handle itemHandle;");
            source.AppendLine("        Rect itemBounds;");
            source.AppendLine(
                $"        GetDialogItem(dialog, {context.ItemNumber}, &itemType, &itemHandle, &itemBounds);");
            source.AppendLine("        (void)itemHandle;");
            source.AppendLine(
                $"        SetDialogItem(dialog, {context.ItemNumber}, itemType, (Handle)DrawGraphic{suffix}, &itemBounds);");
            source.AppendLine($"        DrawGraphic{suffix}(dialog, {context.ItemNumber});");
            source.AppendLine("    }");
        }

        private void AppendDrawCall(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var function = shape switch
            {
                GraphicShape.Line => null,
                GraphicShape.Rectangle => fill switch
                {
                    GraphicFill.Outline => "FrameRect",
                    GraphicFill.Solid => "PaintRect",
                    GraphicFill.Dithered => "FillRect",
                    _ => throw new ArgumentOutOfRangeException(nameof(fill)),
                },
                GraphicShape.Oval => fill switch
                {
                    GraphicFill.Outline => "FrameOval",
                    GraphicFill.Solid => "PaintOval",
                    GraphicFill.Dithered => "FillOval",
                    _ => throw new ArgumentOutOfRangeException(nameof(fill)),
                },
                GraphicShape.Arc => fill switch
                {
                    GraphicFill.Outline => "FrameArc",
                    GraphicFill.Solid => "PaintArc",
                    GraphicFill.Dithered => "FillArc",
                    _ => throw new ArgumentOutOfRangeException(nameof(fill)),
                },
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };
            if (shape == GraphicShape.Line)
            {
                var startX = ParseInteger(context.Control, "startX", 0);
                var startY = ParseInteger(context.Control, "startY", 0);
                var endX = ParseInteger(context.Control, "endX", 100);
                var endY = ParseInteger(context.Control, "endY", 100);
                source.AppendLine(
                    $"        MoveTo((short)(bounds.left + (bounds.right - bounds.left) * {startX} / 100), (short)(bounds.top + (bounds.bottom - bounds.top) * {startY} / 100));");
                source.AppendLine(
                    $"        LineTo((short)(bounds.left + (bounds.right - bounds.left) * {endX} / 100), (short)(bounds.top + (bounds.bottom - bounds.top) * {endY} / 100));");
            }
            else if (shape == GraphicShape.Arc)
            {
                var startAngle = ParseInteger(context.Control, "startAngle", 0);
                var sweepAngle = ParseInteger(context.Control, "sweepAngle", 180);
                var pattern = fill == GraphicFill.Dithered ? ", &qd.gray" : string.Empty;
                source.AppendLine(
                    $"        {function}(&bounds, {startAngle}, {sweepAngle}{pattern});");
            }
            else
            {
                var pattern = fill == GraphicFill.Dithered ? ", &qd.gray" : string.Empty;
                source.AppendLine($"        {function}(&bounds{pattern});");
            }
        }

        private static string Suffix(MacWidgetCodeGenerationContext context) =>
            context.ItemNumber.ToString(CultureInfo.InvariantCulture);

        private static int ParseInteger(
            DesignerControl control,
            string propertyId,
            int fallback) =>
            int.TryParse(
                control.Properties.GetValueOrDefault(propertyId),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : fallback;
    }
}
