using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public sealed class FreeRTOSx86GuiCodeGeneratorTests
{
    private readonly DesignerWidgetRegistry _widgets =
        FreeRTOSx86WidgetCatalog.CreateRegistry();

    [Fact]
    public void TargetProfilesDescribeHerculesAndDeferredVgaTargets()
    {
        var hercules = FreeRTOSx86TargetProfiles.All[0];
        var vga = FreeRTOSx86TargetProfiles.All[1];

        Assert.Equal("dos-8088-hercules", hercules.Id);
        Assert.Equal(720, hercules.ScreenWidth);
        Assert.Equal(348, hercules.ScreenHeight);
        Assert.True(hercules.BuildSupported);
        Assert.Equal("hercules", hercules.GraphicsBackendId);
        Assert.Equal("dos-80386-vga", vga.Id);
        Assert.Equal("vga", vga.GraphicsBackendId);
        Assert.False(vga.BuildSupported);
        Assert.All(
            FreeRTOSx86TargetProfiles.All.Where(profile =>
                profile.GraphicsBackendId == "cga"),
            profile => Assert.False(profile.BuildSupported));
    }

    public static IEnumerable<object[]> WidgetTargetCases =>
        from descriptor in FreeRTOSx86WidgetCatalog.Descriptors
        from profile in FreeRTOSx86TargetProfiles.All
        select new object[]
        {
            descriptor.Definition.Id,
            profile.Id,
            ExpectedWidgetCreateCall(descriptor.Definition.Id),
        };

    [Theory]
    [MemberData(nameof(WidgetTargetCases))]
    public void GeneratorEmitsEveryCatalogWidgetForEveryDosTarget(
        string widgetTypeId,
        string profileId,
        string expectedCreateCall)
    {
        var descriptor = FreeRTOSx86WidgetCatalog.GetRequired(widgetTypeId);
        var profile = FreeRTOSx86TargetProfiles.All.Single(item => item.Id == profileId);
        var control = _widgets.CreateControl(
            "widget",
            widgetTypeId,
            0,
            0,
            descriptor.Definition.DefaultText);

        var generated = FreeRTOSx86GuiCodeGenerator.Generate(
            profile,
            [control],
            _widgets);

        Assert.Contains(expectedCreateCall, generated.TaskSource);
        Assert.Contains("#include \"core/lv_disp.h\"", generated.TaskSource);
        Assert.Equal(
            FreeRTOSx86WidgetCatalog.GetRequiredHeaderPaths([widgetTypeId]),
            generated.WidgetHeaderPaths);
        Assert.Equal(
            FreeRTOSx86WidgetCatalog.GetRequiredSourcePaths([widgetTypeId]),
            generated.WidgetSourcePaths);
        Assert.All(
            descriptor.ConfigurationMacros,
            macro => Assert.Contains($"#define {macro} 1", generated.LvglWidgetConfiguration));
        Assert.Contains(
            $"#define LV_COLOR_DEPTH {(profile.ColorDepthBits == 1 ? 1 : 8)}",
            generated.LvglWidgetConfiguration);
    }

    [Fact]
    public void GeneratorEmitsTaskWidgetsAndUserClickHandler()
    {
        var button = _widgets.CreateControl("okButton", "button", 100, 80, "OK")
            .WithEvent("OnClick", "HandleOkay");
        var rectangle = _widgets.CreateControl(
            "outline",
            "graphic-rectangle-outline",
            20,
            30);
        var generated = FreeRTOSx86GuiCodeGenerator.Generate(
            FreeRTOSx86TargetProfiles.All[0],
            [button, rectangle],
            _widgets);

        Assert.Contains("void vFreeRTOSx86GuiTask(void * pvParameters)", generated.TaskSource);
        Assert.Contains("gui_mark_form_drawn();", generated.TaskSource);
        Assert.Contains("user_app_init();", generated.TaskSource);
        Assert.Contains("user_app_poll();", generated.TaskSource);
        Assert.Contains("dos_agent_log(1U, \"LVGL GUI initialized\");", generated.TaskSource);
        Assert.EndsWith(Environment.NewLine, generated.TaskHeader);
        Assert.Contains("#include \"widgets/lv_btn.h\"", generated.TaskSource);
        Assert.DoesNotContain("#include \"widgets/lv_slider.h\"", generated.TaskSource);
        Assert.Contains("lv_btn_create(lv_scr_act())", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_bg_color(lv_scr_act(), lv_color_black(), LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_border_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_size(obj, 80, 24)", generated.TaskSource);
        Assert.Contains("lv_label_set_text(label, \"OK\")", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_text_color(label, lv_color_hex(0xFFFFFF), LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_size(obj, 64, 40)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_bg_opa(obj, LV_OPA_TRANSP, LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_add_event_cb(obj, prvEvent0, LV_EVENT_CLICKED, NULL)", generated.TaskSource);
        Assert.Contains("HandleOkay();", generated.TaskSource);
        Assert.Contains("dos_agent_log(1U, \"LVGL event: okButton\");", generated.TaskSource);
        Assert.Contains("void HandleOkay(void);", generated.UserEventsHeader);
        Assert.Contains("void HandleOkay(void)", generated.UserEventsSource);
        Assert.Contains("void user_app_init(void);", generated.UserEventsHeader);
        Assert.Contains("void user_app_poll(void);", generated.UserEventsHeader);
        Assert.Contains("void user_app_shutdown(void);", generated.UserEventsHeader);
        Assert.Contains("void user_app_shutdown(void)", generated.UserEventsSource);
        Assert.Contains("#ifndef FREERTOSX86_USER_APP", generated.UserEventsSource);
        Assert.Equal(
            ["widgets/lv_btn.c", "widgets/lv_label.c"],
            generated.WidgetSourcePaths);
        Assert.Contains("#define LV_USE_BTN 1", generated.LvglWidgetConfiguration);
        Assert.Contains("#define LV_USE_SLIDER 0", generated.LvglWidgetConfiguration);
    }

    [Fact]
    public void CatalogExposesAllBundledLvglWidgetsAndTargetColorProperties()
    {
        var ids = _widgets.Definitions.Select(definition => definition.Id).ToHashSet();
        var lvglWidgets = new[]
        {
            "lvgl-arc", "lvgl-bar", "lvgl-button-matrix", "lvgl-canvas",
            "lvgl-dropdown", "lvgl-image", "lvgl-roller", "lvgl-slider",
            "lvgl-switch", "lvgl-table", "lvgl-textarea", "lvgl-animimg",
            "lvgl-calendar", "lvgl-chart", "lvgl-color-wheel", "lvgl-image-button",
            "lvgl-keyboard", "lvgl-led", "lvgl-list", "lvgl-menu", "lvgl-meter",
            "lvgl-message-box", "lvgl-span", "lvgl-spinbox", "lvgl-spinner",
            "lvgl-tabview", "lvgl-tileview", "lvgl-window",
        };

        Assert.All(lvglWidgets, id => Assert.Contains(id, ids));
        Assert.All(_widgets.Definitions, definition =>
            Assert.Contains(definition.Properties, property => property.Id == "color"));
        var calendarSources = FreeRTOSx86WidgetCatalog.GetRequiredSourcePaths(["lvgl-calendar"]);
        Assert.Contains("extra/widgets/calendar/lv_calendar_header_arrow.c", calendarSources);
        var menu = FreeRTOSx86WidgetCatalog.GetRequired("lvgl-menu");
        Assert.Contains("extra/layouts/flex/lv_flex.c", menu.SourcePaths);
        Assert.Contains("widgets/lv_img.c", menu.SourcePaths);
        Assert.Contains("LV_USE_FLEX", menu.ConfigurationMacros);
        var list = FreeRTOSx86WidgetCatalog.GetRequired("lvgl-list");
        Assert.Contains("extra/layouts/flex/lv_flex.c", list.SourcePaths);
        Assert.Contains("LV_USE_FLEX", list.ConfigurationMacros);
    }

    [Fact]
    public void GeneratorSelectsOnlyTheUsedWidgetSources()
    {
        var chart = _widgets.CreateControl("chart", "lvgl-chart", 20, 20);
        var generated = FreeRTOSx86GuiCodeGenerator.Generate(
            FreeRTOSx86TargetProfiles.All[0],
            [chart],
            _widgets);

        Assert.Contains("obj = lv_chart_create(lv_scr_act());", generated.TaskSource);
        Assert.Equal(
            ["extra/widgets/chart/lv_chart.c"],
            generated.WidgetSourcePaths);
        Assert.Contains("#define LV_USE_CHART 1", generated.LvglWidgetConfiguration);
        Assert.Contains("#define LV_USE_BTN 0", generated.LvglWidgetConfiguration);
    }

    [Theory]
    [InlineData("dos-8088-cga-320-palette0", 8)]
    [InlineData("dos-8088-cga-320-palette1", 8)]
    [InlineData("dos-8088-cga-640", 1)]
    [InlineData("dos-80386-vga", 8)]
    public void GeneratorCreatesWidgetsAndColorDepthForEachDosDisplayProfile(
        string profileId,
        int expectedLvglColorDepth)
    {
        var profile = FreeRTOSx86TargetProfiles.All.Single(item => item.Id == profileId);
        var button = _widgets.CreateControl("run", "button", 10, 10, "Run");

        var generated = FreeRTOSx86GuiCodeGenerator.Generate(
            profile,
            [button],
            _widgets);

        Assert.Contains("lv_btn_create(lv_scr_act())", generated.TaskSource);
        Assert.Contains(
            $"#define LV_COLOR_DEPTH {expectedLvglColorDepth}",
            generated.LvglWidgetConfiguration);
    }

    [Fact]
    public void GeneratorEscapesCStringsAndSupportsGraphicsPrimitives()
    {
        var label = _widgets.CreateControl("label", "static-text", 5, 6, "Say \"hi\"");
        var line = _widgets.CreateControl("line", "graphic-line", 10, 10);
        var circle = _widgets.CreateControl("circle", "graphic-circle-filled", 50, 60);
        var checkbox = _widgets.CreateControl(
            "checkedBox",
            "checkbox",
            12,
            14,
            "Ready").WithProperty("checked", "true");
        var generated = FreeRTOSx86GuiCodeGenerator.Generate(
            FreeRTOSx86TargetProfiles.All[0],
            [label, line, circle, checkbox],
            _widgets);

        Assert.Contains("lv_label_set_text(obj, \"Say \\\"hi\\\"\")", generated.TaskSource);
        Assert.Contains("xLinePoints1[1].x = 79;", generated.TaskSource);
        Assert.Contains("xLinePoints1[1].y = 31;", generated.TaskSource);
        Assert.Contains("lv_line_set_points(obj, xLinePoints1, 2U)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_line_width(obj, 2, LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_line_color(obj, lv_color_hex(0xFFFFFF), LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_radius(obj, LV_RADIUS_CIRCLE, LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_bg_opa(obj, LV_OPA_COVER, LV_PART_MAIN)", generated.TaskSource);
        Assert.Contains("lv_obj_set_style_bg_opa(obj, LV_OPA_COVER, LV_PART_INDICATOR | LV_STATE_CHECKED)", generated.TaskSource);
    }

    [Fact]
    public void GeneratorRejectsControlsOutsideTheTarget()
    {
        var control = _widgets.CreateControl(
            "outside",
            "button",
            700,
            330,
            "Too far");

        Assert.Throws<ArgumentException>(() =>
            FreeRTOSx86GuiCodeGenerator.Generate(
                FreeRTOSx86TargetProfiles.All[0],
                [control],
                _widgets));
    }

    [Fact]
    public void ProjectSerializerRoundTripsHerculesControls()
    {
        var profile = FreeRTOSx86TargetProfiles.All[0];
        var control = _widgets.CreateControl("caption", "static-text", 12, 14, "READY");
        var project = new ClassicMacProject(
            "TestGui",
            profile.Id,
            "Test GUI",
            [control],
            formProperties: new MacFormProperties
            {
                ShowTitleBar = false,
                Resizable = false,
                HasCloseBox = false,
                HasZoomBox = false,
                Movable = false,
                Width = profile.ScreenWidth,
                Height = profile.ScreenHeight,
            });

        var restored = ClassicMacProjectSerializer.Deserialize(
            ClassicMacProjectSerializer.Serialize(project),
            _widgets);

        Assert.Equal(profile.Id, restored.TargetProfileId);
        Assert.Equal(720, restored.FormProperties.GetClientWidth(profile));
        var restoredControl = Assert.Single(restored.Controls);
        Assert.Equal(control.Id, restoredControl.Id);
        Assert.Equal(control.Text, restoredControl.Text);
        Assert.Equal(control.X, restoredControl.X);
        Assert.Equal(control.Y, restoredControl.Y);
    }

    private static string ExpectedWidgetCreateCall(string widgetTypeId) =>
        widgetTypeId switch
        {
            "static-text" => "lv_label_create(lv_scr_act())",
            "button" => "lv_btn_create(lv_scr_act())",
            "checkbox" => "lv_checkbox_create(lv_scr_act())",
            "graphic-line" => "lv_line_create(lv_scr_act())",
            "graphic-rectangle-outline" or
                "graphic-rectangle-filled" or
                "graphic-rectangle-dithered" or
                "graphic-circle-outline" or
                "graphic-circle-filled" or
                "graphic-circle-dithered" => "lv_obj_create(lv_scr_act())",
            "graphic-arc-outline" or
                "graphic-arc-filled" or
                "graphic-arc-dithered" or
                "lvgl-arc" => "lv_arc_create(lv_scr_act())",
            "lvgl-bar" => "lv_bar_create(lv_scr_act())",
            "lvgl-button-matrix" => "lv_btnmatrix_create(lv_scr_act())",
            "lvgl-canvas" => "lv_canvas_create(lv_scr_act())",
            "lvgl-dropdown" => "lv_dropdown_create(lv_scr_act())",
            "lvgl-image" => "lv_img_create(lv_scr_act())",
            "lvgl-roller" => "lv_roller_create(lv_scr_act())",
            "lvgl-slider" => "lv_slider_create(lv_scr_act())",
            "lvgl-switch" => "lv_switch_create(lv_scr_act())",
            "lvgl-table" => "lv_table_create(lv_scr_act())",
            "lvgl-textarea" => "lv_textarea_create(lv_scr_act())",
            "lvgl-animimg" => "lv_animimg_create(lv_scr_act())",
            "lvgl-calendar" => "lv_calendar_create(lv_scr_act())",
            "lvgl-chart" => "lv_chart_create(lv_scr_act())",
            "lvgl-color-wheel" => "lv_colorwheel_create(lv_scr_act(), true)",
            "lvgl-image-button" => "lv_imgbtn_create(lv_scr_act())",
            "lvgl-keyboard" => "lv_keyboard_create(lv_scr_act())",
            "lvgl-led" => "lv_led_create(lv_scr_act())",
            "lvgl-list" => "lv_list_create(lv_scr_act())",
            "lvgl-menu" => "lv_menu_create(lv_scr_act())",
            "lvgl-meter" => "lv_meter_create(lv_scr_act())",
            "lvgl-message-box" => "lv_msgbox_create(",
            "lvgl-span" => "lv_spangroup_create(lv_scr_act())",
            "lvgl-spinbox" => "lv_spinbox_create(lv_scr_act())",
            "lvgl-spinner" => "lv_spinner_create(lv_scr_act(), 1000U, 60U)",
            "lvgl-tabview" => "lv_tabview_create(lv_scr_act(), LV_DIR_TOP, 24)",
            "lvgl-tileview" => "lv_tileview_create(lv_scr_act())",
            "lvgl-window" => "lv_win_create(lv_scr_act(), 24)",
            _ => throw new ArgumentOutOfRangeException(nameof(widgetTypeId), widgetTypeId, null),
        };
}
