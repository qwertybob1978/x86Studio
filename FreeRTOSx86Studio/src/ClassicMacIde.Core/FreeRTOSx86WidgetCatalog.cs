namespace ClassicMacIde.Core;

public sealed record FreeRTOSx86WidgetDescriptor(
    DesignerWidgetDefinition Definition,
    string RibbonGroup,
    IReadOnlyList<string> HeaderPaths,
    IReadOnlyList<string> SourcePaths,
    IReadOnlyList<string> ConfigurationMacros);

public static class FreeRTOSx86WidgetCatalog
{
    private static readonly string LabelHeader = "widgets/lv_label.h";
    private static readonly string LabelSource = "widgets/lv_label.c";

    private static readonly Lazy<IReadOnlyList<FreeRTOSx86WidgetDescriptor>> DescriptorCache =
        new(() => Array.AsReadOnly(CreateDescriptors().ToArray()));

    public static IReadOnlyList<FreeRTOSx86WidgetDescriptor> Descriptors =>
        DescriptorCache.Value;

    public static DesignerWidgetRegistry CreateRegistry() =>
        new(Descriptors.Select(descriptor => descriptor.Definition));

    public static FreeRTOSx86WidgetDescriptor GetRequired(string widgetTypeId) =>
        Descriptors.FirstOrDefault(descriptor =>
            descriptor.Definition.Id == widgetTypeId) ??
        throw new KeyNotFoundException(
            $"FreeRTOSx86 widget type '{widgetTypeId}' is not registered.");

    public static IReadOnlyList<string> GetRequiredSourcePaths(
        IEnumerable<string> widgetTypeIds)
    {
        ArgumentNullException.ThrowIfNull(widgetTypeIds);
        return widgetTypeIds
            .SelectMany(widgetTypeId => GetRequired(widgetTypeId).SourcePaths)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> GetRequiredHeaderPaths(
        IEnumerable<string> widgetTypeIds)
    {
        ArgumentNullException.ThrowIfNull(widgetTypeIds);
        return widgetTypeIds
            .SelectMany(widgetTypeId => GetRequired(widgetTypeId).HeaderPaths)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> GetRequiredConfigurationMacros(
        IEnumerable<string> widgetTypeIds)
    {
        ArgumentNullException.ThrowIfNull(widgetTypeIds);
        return widgetTypeIds
            .SelectMany(widgetTypeId =>
                GetRequired(widgetTypeId).ConfigurationMacros)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static string GenerateConfigurationHeader(
        IEnumerable<string> widgetTypeIds,
        int colorDepth = 1)
    {
        if (colorDepth is not (1 or 8 or 16 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(colorDepth));
        }

        var enabledMacros = new HashSet<string>(
            GetRequiredConfigurationMacros(widgetTypeIds),
            StringComparer.Ordinal);
        var allMacros = Descriptors
            .SelectMany(descriptor => descriptor.ConfigurationMacros)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var source = new System.Text.StringBuilder();
        source.AppendLine("#ifndef FREERTOSX86_LVGL_WIDGET_CONFIG_H");
        source.AppendLine("#define FREERTOSX86_LVGL_WIDGET_CONFIG_H");
        source.Append("#define LV_COLOR_DEPTH ").AppendLine(colorDepth.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        foreach (var macro in allMacros)
        {
            source.Append("#define ")
                .Append(macro)
                .Append(' ')
                .AppendLine(enabledMacros.Contains(macro) ? "1" : "0");
        }
        source.AppendLine("#endif");
        return source.ToString();
    }

    private static IEnumerable<FreeRTOSx86WidgetDescriptor> CreateDescriptors()
    {
        var standard = ClassicMacWidgetCatalog.CreateRegistry().Definitions
            .Where(definition => definition.Id is
                "static-text" or
                "button" or
                "checkbox" or
                "graphic-line" or
                "graphic-rectangle-outline" or
                "graphic-rectangle-filled" or
                "graphic-rectangle-dithered" or
                "graphic-circle-outline" or
                "graphic-circle-filled" or
                "graphic-circle-dithered" or
                "graphic-arc-outline" or
                "graphic-arc-filled" or
                "graphic-arc-dithered")
            .Select(AddTargetColorProperties);

        foreach (var definition in standard)
        {
            yield return definition.Id switch
            {
                "static-text" => Descriptor(
                    definition, "Controls", [LabelHeader], [LabelSource], ["LV_USE_LABEL"]),
                "button" => Descriptor(
                    definition,
                    "Controls",
                    ["widgets/lv_btn.h", LabelHeader],
                    ["widgets/lv_btn.c", LabelSource],
                    ["LV_USE_BTN", "LV_USE_LABEL"]),
                "checkbox" => Descriptor(
                    definition,
                    "Controls",
                    ["widgets/lv_checkbox.h", LabelHeader],
                    ["widgets/lv_checkbox.c", LabelSource],
                    ["LV_USE_CHECKBOX", "LV_USE_LABEL"]),
                "graphic-line" => Descriptor(
                    definition,
                    "Drawing",
                    ["widgets/lv_line.h"],
                    ["widgets/lv_line.c"],
                    ["LV_USE_LINE"]),
                "graphic-arc-outline" or "graphic-arc-filled" or "graphic-arc-dithered" =>
                    Descriptor(
                        definition,
                        "Drawing",
                        ["widgets/lv_arc.h"],
                        ["widgets/lv_arc.c"],
                        ["LV_USE_ARC"]),
                _ => Descriptor(
                    definition,
                    "Drawing",
                    ["core/lv_obj.h"],
                    [],
                    []),
            };
        }

        foreach (var spec in AdditionalWidgets)
        {
            yield return CreateAdditionalDescriptor(spec);
        }
    }

    private static readonly WidgetSpec[] AdditionalWidgets =
    [
        new("lvgl-arc", "Arc", "Data", "lv_arc",
            "widgets/lv_arc.h", ["widgets/lv_arc.c"], ["LV_USE_ARC"], 64, 64, "50", WidgetProperty.Value),
        new("lvgl-bar", "Bar", "Data", "lv_bar",
            "widgets/lv_bar.h", ["widgets/lv_bar.c"], ["LV_USE_BAR"], 120, 18, "50", WidgetProperty.Value),
        new("lvgl-button-matrix", "Button Matrix", "Controls", "lv_btnmatrix",
            "widgets/lv_btnmatrix.h", ["widgets/lv_btnmatrix.c", LabelSource], ["LV_USE_BTNMATRIX", "LV_USE_LABEL"], 160, 60, "One\nTwo\nThree", WidgetProperty.Items),
        new("lvgl-canvas", "Canvas", "Media", "lv_canvas",
            "widgets/lv_canvas.h", ["widgets/lv_canvas.c"], ["LV_USE_CANVAS"], 96, 64, "", WidgetProperty.None),
        new("lvgl-dropdown", "Dropdown", "Controls", "lv_dropdown",
            "widgets/lv_dropdown.h", ["widgets/lv_dropdown.c", LabelSource], ["LV_USE_DROPDOWN", "LV_USE_LABEL"], 120, 24, "Option 1", WidgetProperty.Items),
        new("lvgl-image", "Image", "Media", "lv_img",
            "widgets/lv_img.h", ["widgets/lv_img.c"], ["LV_USE_IMG"], 64, 64, "", WidgetProperty.None),
        new("lvgl-roller", "Roller", "Controls", "lv_roller",
            "widgets/lv_roller.h", ["widgets/lv_roller.c", LabelSource], ["LV_USE_ROLLER", "LV_USE_LABEL"], 100, 60, "One\nTwo\nThree", WidgetProperty.Items),
        new("lvgl-slider", "Slider", "Controls", "lv_slider",
            "widgets/lv_slider.h", ["widgets/lv_slider.c", "widgets/lv_bar.c"], ["LV_USE_SLIDER", "LV_USE_BAR"], 120, 18, "50", WidgetProperty.Value),
        new("lvgl-switch", "Switch", "Controls", "lv_switch",
            "widgets/lv_switch.h", ["widgets/lv_switch.c"], ["LV_USE_SWITCH"], 48, 24, "", WidgetProperty.Checked),
        new("lvgl-table", "Table", "Data", "lv_table",
            "widgets/lv_table.h", ["widgets/lv_table.c"], ["LV_USE_TABLE"], 160, 80, "Cell", WidgetProperty.None),
        new("lvgl-textarea", "Text Area", "Controls", "lv_textarea",
            "widgets/lv_textarea.h", ["widgets/lv_textarea.c", LabelSource], ["LV_USE_TEXTAREA", "LV_USE_LABEL"], 160, 64, "Text", WidgetProperty.Text),
        new("lvgl-animimg", "Animated Image", "Media", "lv_animimg",
            "extra/widgets/animimg/lv_animimg.h", ["extra/widgets/animimg/lv_animimg.c", "widgets/lv_img.c"], ["LV_USE_ANIMIMG", "LV_USE_IMG"], 64, 64, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-calendar", "Calendar", "Data", "lv_calendar",
            "extra/widgets/calendar/lv_calendar.h",
            ["extra/widgets/calendar/lv_calendar.c", "extra/widgets/calendar/lv_calendar_header_arrow.c", "extra/widgets/calendar/lv_calendar_header_dropdown.c", "widgets/lv_dropdown.c", LabelSource],
            ["LV_USE_CALENDAR", "LV_USE_CALENDAR_HEADER_ARROW", "LV_USE_CALENDAR_HEADER_DROPDOWN", "LV_USE_DROPDOWN", "LV_USE_LABEL"], 180, 160, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-chart", "Chart", "Data", "lv_chart",
            "extra/widgets/chart/lv_chart.h", ["extra/widgets/chart/lv_chart.c"], ["LV_USE_CHART"], 180, 120, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-color-wheel", "Color Wheel", "Data", "lv_colorwheel",
            "extra/widgets/colorwheel/lv_colorwheel.h", ["extra/widgets/colorwheel/lv_colorwheel.c"], ["LV_USE_COLORWHEEL"], 120, 120, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-image-button", "Image Button", "Controls", "lv_imgbtn",
            "extra/widgets/imgbtn/lv_imgbtn.h", ["extra/widgets/imgbtn/lv_imgbtn.c"], ["LV_USE_IMGBTN"], 24, 24, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-keyboard", "Keyboard", "Controls", "lv_keyboard",
            "extra/widgets/keyboard/lv_keyboard.h", ["extra/widgets/keyboard/lv_keyboard.c", "widgets/lv_btnmatrix.c", "widgets/lv_textarea.c", LabelSource], ["LV_USE_KEYBOARD", "LV_USE_BTNMATRIX", "LV_USE_TEXTAREA", "LV_USE_LABEL"], 220, 100, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-led", "LED", "Data", "lv_led",
            "extra/widgets/led/lv_led.h", ["extra/widgets/led/lv_led.c"], ["LV_USE_LED"], 24, 24, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-list", "List", "Containers", "lv_list",
            "extra/widgets/list/lv_list.h", ["extra/widgets/list/lv_list.c", "extra/layouts/flex/lv_flex.c", "widgets/lv_btn.c", LabelSource], ["LV_USE_LIST", "LV_USE_FLEX", "LV_USE_BTN", "LV_USE_LABEL"], 140, 100, "Item 1\nItem 2", WidgetProperty.Items, IsExtra: true),
        new("lvgl-menu", "Menu", "Containers", "lv_menu",
            "extra/widgets/menu/lv_menu.h",
            ["extra/widgets/menu/lv_menu.c", "extra/layouts/flex/lv_flex.c", "widgets/lv_btn.c", "widgets/lv_img.c", LabelSource],
            ["LV_USE_MENU", "LV_USE_FLEX", "LV_USE_BTN", "LV_USE_IMG", "LV_USE_LABEL"], 180, 120, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-meter", "Meter", "Data", "lv_meter",
            "extra/widgets/meter/lv_meter.h", ["extra/widgets/meter/lv_meter.c"], ["LV_USE_METER"], 120, 120, "50", WidgetProperty.Value, IsExtra: true),
        new("lvgl-message-box", "Message Box", "Containers", "lv_msgbox",
            "extra/widgets/msgbox/lv_msgbox.h", ["extra/widgets/msgbox/lv_msgbox.c", LabelSource, "widgets/lv_btnmatrix.c"], ["LV_USE_MSGBOX", "LV_USE_LABEL", "LV_USE_BTNMATRIX"], 180, 100, "Message", WidgetProperty.Text, IsExtra: true),
        new("lvgl-span", "Span Group", "Data", "lv_spangroup",
            "extra/widgets/span/lv_span.h", ["extra/widgets/span/lv_span.c"], ["LV_USE_SPAN"], 160, 48, "Text", WidgetProperty.Text, IsExtra: true),
        new("lvgl-spinbox", "Spinbox", "Controls", "lv_spinbox",
            "extra/widgets/spinbox/lv_spinbox.h", ["extra/widgets/spinbox/lv_spinbox.c", "widgets/lv_textarea.c", "widgets/lv_btnmatrix.c", LabelSource], ["LV_USE_SPINBOX", "LV_USE_TEXTAREA", "LV_USE_BTNMATRIX", "LV_USE_LABEL"], 100, 32, "0", WidgetProperty.Value, IsExtra: true),
        new("lvgl-spinner", "Spinner", "Data", "lv_spinner",
            "extra/widgets/spinner/lv_spinner.h", ["extra/widgets/spinner/lv_spinner.c", "widgets/lv_arc.c"], ["LV_USE_SPINNER", "LV_USE_ARC"], 48, 48, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-tabview", "Tab View", "Containers", "lv_tabview",
            "extra/widgets/tabview/lv_tabview.h", ["extra/widgets/tabview/lv_tabview.c", "widgets/lv_btnmatrix.c", LabelSource], ["LV_USE_TABVIEW", "LV_USE_BTNMATRIX", "LV_USE_LABEL"], 180, 120, "Tab 1\nTab 2", WidgetProperty.Items, IsExtra: true),
        new("lvgl-tileview", "Tile View", "Containers", "lv_tileview",
            "extra/widgets/tileview/lv_tileview.h", ["extra/widgets/tileview/lv_tileview.c"], ["LV_USE_TILEVIEW"], 160, 100, "", WidgetProperty.None, IsExtra: true),
        new("lvgl-window", "Window", "Containers", "lv_win",
            "extra/widgets/win/lv_win.h", ["extra/widgets/win/lv_win.c", "widgets/lv_btn.c", LabelSource], ["LV_USE_WIN", "LV_USE_BTN", "LV_USE_LABEL"], 180, 120, "Window", WidgetProperty.Text, IsExtra: true),
    ];

    private static FreeRTOSx86WidgetDescriptor CreateAdditionalDescriptor(
        WidgetSpec spec)
    {
        var properties = new List<DesignerWidgetPropertyDefinition>
        {
            new("color", "Foreground color", DesignerWidgetPropertyEditorKind.Color, "#FFFFFF"),
            new("backgroundColor", "Background color", DesignerWidgetPropertyEditorKind.Color, "#000000"),
        };
        if (spec.Properties.HasFlag(WidgetProperty.Value))
        {
            properties.Add(new(
                "value",
                "Value",
                DesignerWidgetPropertyEditorKind.Integer,
                spec.DefaultText == "50" ? "50" : "0",
                MinimumValue: 0,
                MaximumValue: 100));
        }
        if (spec.Properties.HasFlag(WidgetProperty.Checked))
        {
            properties.Add(new(
                "checked",
                "Checked",
                DesignerWidgetPropertyEditorKind.Boolean,
                "false"));
        }
        if (spec.Properties.HasFlag(WidgetProperty.Items))
        {
            properties.Add(new(
                "items",
                "Items",
                DesignerWidgetPropertyEditorKind.MultiLineText,
                spec.DefaultText));
        }
        var defaultText = spec.Properties.HasFlag(WidgetProperty.Items)
            ? string.Empty
            : spec.DefaultText;
        var definition = new DesignerWidgetDefinition(
            spec.Id,
            spec.Name,
            spec.LvglClass,
            spec.Width,
            spec.Height,
            8,
            8,
            defaultText,
            isInteractive: true,
            properties,
            new MetadataWidgetCodeGenerator(spec.Id),
            DesignerWidgetCategory.Controls,
            supportsTextAccess: spec.Properties.HasFlag(WidgetProperty.Text),
            supportsValueAccess: spec.Properties.HasFlag(WidgetProperty.Value));
        var sourcePaths = spec.SourcePaths
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return Descriptor(
            definition,
            spec.RibbonGroup,
            new[] { spec.HeaderPath }
                .Concat(spec.SourcePaths.Select(sourcePath =>
                    sourcePath[..^1] + "h"))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            sourcePaths,
            spec.ConfigurationMacros);
    }

    private static DesignerWidgetDefinition AddTargetColorProperties(
        DesignerWidgetDefinition definition)
    {
        var properties = definition.Properties
            .Select(property => property.Id == "color"
                ? property with { DefaultValue = "#FFFFFF" }
                : property)
            .ToList();
        if (properties.All(property => property.Id != "color"))
        {
            properties.Add(new(
                "color",
                "Foreground color",
                DesignerWidgetPropertyEditorKind.Color,
                "#FFFFFF"));
        }
        if (properties.All(property => property.Id != "backgroundColor"))
        {
            properties.Add(new(
                "backgroundColor",
                "Background color",
                DesignerWidgetPropertyEditorKind.Color,
                "#000000"));
        }

        return new DesignerWidgetDefinition(
            definition.Id,
            definition.DisplayName,
            definition.PreviewRendererId,
            definition.DefaultWidth,
            definition.DefaultHeight,
            definition.MinimumWidth,
            definition.MinimumHeight,
            definition.DefaultText,
            definition.IsInteractive,
            properties,
            definition.CodeGenerator,
            definition.Category,
            definition.SupportsTextAccess,
            definition.SupportsValueAccess,
            definition.Events);
    }

    private static FreeRTOSx86WidgetDescriptor Descriptor(
        DesignerWidgetDefinition definition,
        string ribbonGroup,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> sources,
        IReadOnlyList<string> configurationMacros) =>
        new(
            definition,
            ribbonGroup,
            Array.AsReadOnly(headers.ToArray()),
            Array.AsReadOnly(sources.ToArray()),
            Array.AsReadOnly(configurationMacros.ToArray()));

    private sealed record WidgetSpec(
        string Id,
        string Name,
        string RibbonGroup,
        string LvglClass,
        string HeaderPath,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<string> ConfigurationMacros,
        double Width,
        double Height,
        string DefaultText,
        WidgetProperty Properties,
        bool IsExtra = false);

    [Flags]
    private enum WidgetProperty
    {
        None = 0,
        Value = 1,
        Checked = 2,
        Items = 4,
        Text = 8,
    }

    private sealed class MetadataWidgetCodeGenerator(string widgetTypeId)
        : MacWidgetCodeGenerator
    {
        public override string WidgetTypeId { get; } = widgetTypeId;

        public override void AppendResourceItem(
            System.Text.StringBuilder source,
            MacWidgetCodeGenerationContext context) =>
            throw new NotSupportedException(
                "FreeRTOSx86 widgets do not generate Macintosh resources.");
    }
}
