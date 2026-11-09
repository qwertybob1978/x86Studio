using System.Globalization;
using System.Text;

namespace ClassicMacIde.Core;

public sealed record FreeRTOSx86GeneratedGui(
    string TaskSource,
    string TaskHeader,
    string UserEventsSource,
    string UserEventsHeader,
    IReadOnlyList<string> WidgetHeaderPaths,
    IReadOnlyList<string> WidgetSourcePaths,
    string LvglWidgetConfiguration);

public static class FreeRTOSx86GuiCodeGenerator
{
    public static FreeRTOSx86GeneratedGui Generate(
        MacTargetProfile profile,
        IReadOnlyList<DesignerControl> controls,
        DesignerWidgetRegistry widgetRegistry)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(widgetRegistry);
        if (profile.GraphicsBackendId is not ("hercules" or "cga" or "vga"))
        {
            throw new NotSupportedException(
                $"DOS graphics backend '{profile.GraphicsBackendId}' is not supported.");
        }

        var controlIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controls)
        {
            ArgumentNullException.ThrowIfNull(control);
            widgetRegistry.ValidateControl(control);
            if (!ClassicMacCIdentifier.IsValid(control.Id))
            {
                throw new ArgumentException(
                    $"Control ID '{control.Id}' must be a C identifier.",
                    nameof(controls));
            }
            if (!controlIds.Add(control.Id))
            {
                throw new ArgumentException(
                    $"Control ID '{control.Id}' is duplicated.",
                    nameof(controls));
            }
            if (!control.FitsWithin(profile.ScreenWidth, profile.ScreenHeight))
            {
                throw new ArgumentException(
                    $"Control '{control.Id}' lies outside the target display.",
                    nameof(controls));
            }
            ValidateText(control.Text, nameof(controls));
            ValidateSupportedWidget(control);
        }

        var widgetTypeIds = controls
            .Select(control => control.WidgetTypeId)
            .ToArray();
        var widgetHeaders = FreeRTOSx86WidgetCatalog.GetRequiredHeaderPaths(widgetTypeIds);
        var widgetSources = FreeRTOSx86WidgetCatalog.GetRequiredSourcePaths(widgetTypeIds);
        var source = new StringBuilder();
        source.AppendLine("#include \"FreeRTOS.h\"");
        source.AppendLine("#include \"task.h\"");
        source.AppendLine("#include \"dos_agent.h\"");
        source.AppendLine("#include \"gui.h\"");
        source.AppendLine("#include \"core/lv_disp.h\"");
        source.AppendLine("#include \"core/lv_obj.h\"");
        foreach (var header in widgetHeaders)
        {
            source.Append("#include \"").Append(header).AppendLine("\"");
        }
        source.AppendLine("#include \"user_events.h\"");
        source.AppendLine();

        var handlers = controls
            .Select(control => control.Events.GetValueOrDefault("OnClick"))
            .Where(handler => !string.IsNullOrWhiteSpace(handler))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var clickControls = controls
            .Select((control, index) => (control, index))
            .Where(item => !string.IsNullOrWhiteSpace(
                item.control.Events.GetValueOrDefault("OnClick")))
            .ToArray();
        foreach (var item in controls.Select((control, index) => (control, index))
                     .Where(item => item.control.WidgetTypeId == "graphic-line"))
        {
            source.AppendLine($"static lv_point_t xLinePoints{item.index}[2];");
        }
        foreach (var item in controls.Select((control, index) => (control, index))
                     .Where(item => item.control.WidgetTypeId == "lvgl-button-matrix"))
        {
            source.AppendLine($"static const char * xButtonMap{item.index}[] =");
            source.AppendLine("{");
            foreach (var option in ReadOptions(item.control, "items", "One\nTwo\nThree"))
            {
                source.AppendLine($"    \"{EscapeCString(option)}\",");
            }
            source.AppendLine("    \"\"");
            source.AppendLine("};");
        }
        foreach (var item in controls.Select((control, index) => (control, index))
                     .Where(item => item.control.WidgetTypeId == "lvgl-message-box"))
        {
            source.AppendLine($"static const char * xMessageButtons{item.index}[] = {{ \"OK\", \"\" }};");
        }
        foreach (var item in controls.Select((control, index) => (control, index))
                     .Where(item => item.control.WidgetTypeId == "lvgl-canvas"))
        {
            var bufferSize = checked(
                ToPixel(item.control.Width) * ToPixel(item.control.Height));
            source.AppendLine($"static lv_color_t xCanvasBuffer{item.index}[{bufferSize}];");
        }
        if (controls.Any(control => control.WidgetTypeId == "graphic-line"))
        {
            source.AppendLine();
        }

        foreach (var (control, index) in clickControls)
        {
            var handler = control.Events["OnClick"];
            source.AppendLine($"static void prvEvent{index}(lv_event_t * event)");
            source.AppendLine("{");
            source.AppendLine("    (void)event;");
            source.Append("    dos_agent_log(1U, \"LVGL event: ")
                .Append(EscapeCString(control.Id))
                .AppendLine("\");");
            source.AppendLine($"    {handler}();");
            source.AppendLine("}");
            source.AppendLine();
        }

        source.AppendLine("static void prvDrawForm(void)");
        source.AppendLine("{");
        source.AppendLine("    lv_obj_t * obj;");
        source.AppendLine("    lv_obj_t * label;");
        source.AppendLine("    lv_obj_t * child;");
        source.AppendLine();
        source.AppendLine("    lv_obj_set_style_bg_color(lv_scr_act(), lv_color_black(), LV_PART_MAIN);");
        source.AppendLine("    lv_obj_set_style_bg_opa(lv_scr_act(), LV_OPA_COVER, LV_PART_MAIN);");
        foreach (var (control, index) in controls.Select((control, index) => (control, index)))
        {
            AppendDrawCall(
                source,
                control,
                index,
                clickControls.Any(item => item.index == index));
        }
        source.AppendLine("}");
        source.AppendLine();

        foreach (var handler in handlers)
        {
            source.AppendLine($"extern void {handler}(void);");
        }
        if (handlers.Length > 0)
        {
            source.AppendLine();
        }

        source.AppendLine("void vFreeRTOSx86GuiTask(void * pvParameters)");
        source.AppendLine("{");
        source.AppendLine("    (void)pvParameters;");
        source.AppendLine();
        source.AppendLine("    if (gui_initialize() != GUI_OK)");
        source.AppendLine("    {");
        source.AppendLine("        for (;;)");
        source.AppendLine("        {");
        source.AppendLine("            vTaskDelay((TickType_t)1U);");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    gui_clear();");
        source.AppendLine("    prvDrawForm();");
        source.AppendLine("    user_app_init();");
        source.AppendLine("    gui_mark_form_drawn();");
        source.AppendLine("    dos_agent_log(1U, \"LVGL GUI initialized\");");
        source.AppendLine("    for (;;)");
        source.AppendLine("    {");
        source.AppendLine("        gui_process();");
        source.AppendLine("        user_app_poll();");
        source.AppendLine("        vTaskDelay((TickType_t)1U);");
        source.AppendLine("    }");
        source.AppendLine("}");

        return new FreeRTOSx86GeneratedGui(
            source.ToString(),
            GenerateTaskHeader(),
            GenerateUserEventsSource(handlers),
            GenerateUserEventsHeader(handlers),
            widgetHeaders,
            widgetSources,
            FreeRTOSx86WidgetCatalog.GenerateConfigurationHeader(
                widgetTypeIds,
                profile.ColorDepthBits == 1 ? 1 : 8));
    }

    private static void AppendDrawCall(
        StringBuilder source,
        DesignerControl control,
        int index,
        bool hasClickHandler)
    {
        var x = ToPixel(control.X);
        var y = ToPixel(control.Y);
        var width = ToPixel(control.Width);
        var height = ToPixel(control.Height);
        var escapedText = EscapeCString(control.Text);
        var foregroundHex = ReadColorHex(control, "color", 0xFFFFFFU);
        switch (control.WidgetTypeId)
        {
            case "static-text":
                source.AppendLine("    obj = lv_label_create(lv_scr_act());");
                source.AppendLine($"    lv_label_set_text(obj, \"{escapedText}\");");
                source.AppendLine($"    lv_obj_set_style_text_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                break;
            case "button":
                source.AppendLine("    obj = lv_btn_create(lv_scr_act());");
                source.AppendLine("    lv_obj_set_style_radius(obj, 0, LV_PART_MAIN);");
                source.AppendLine("    lv_obj_set_style_border_width(obj, 1, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_style_border_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                source.AppendLine("    lv_obj_set_style_bg_opa(obj, LV_OPA_TRANSP, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                source.AppendLine("    label = lv_label_create(obj);");
                source.AppendLine($"    lv_label_set_text(label, \"{escapedText}\");");
                source.AppendLine($"    lv_obj_set_style_text_color(label, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                source.AppendLine("    lv_obj_center(label);");
                break;
            case "checkbox":
                var isChecked = bool.TryParse(
                    control.Properties.GetValueOrDefault("checked"),
                    out var checkValue) && checkValue;
                source.AppendLine("    obj = lv_checkbox_create(lv_scr_act());");
                source.AppendLine($"    lv_checkbox_set_text(obj, \"{escapedText}\");");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                source.AppendLine($"    lv_obj_set_style_text_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                source.AppendLine("    lv_obj_set_style_border_width(obj, 1, LV_PART_INDICATOR);");
                source.AppendLine($"    lv_obj_set_style_border_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_INDICATOR);");
                source.AppendLine("    lv_obj_set_style_bg_opa(obj, LV_OPA_TRANSP, LV_PART_INDICATOR);");
                source.AppendLine($"    lv_obj_set_style_bg_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_INDICATOR | LV_STATE_CHECKED);");
                source.AppendLine("    lv_obj_set_style_bg_opa(obj, LV_OPA_COVER, LV_PART_INDICATOR | LV_STATE_CHECKED);");
                source.AppendLine("    lv_obj_set_style_line_color(obj, lv_color_white(), LV_PART_INDICATOR);");
                if (isChecked)
                {
                    source.AppendLine("    lv_obj_add_state(obj, LV_STATE_CHECKED);");
                }
                break;
            case "graphic-line":
                var startX = x + (width - 1) * ReadPercentage(control, "startX") / 100;
                var startY = y + (height - 1) * ReadPercentage(control, "startY") / 100;
                var endX = x + (width - 1) * ReadPercentage(control, "endX") / 100;
                var endY = y + (height - 1) * ReadPercentage(control, "endY") / 100;
                source.AppendLine($"    xLinePoints{index}[0].x = {startX - x};");
                source.AppendLine($"    xLinePoints{index}[0].y = {startY - y};");
                source.AppendLine($"    xLinePoints{index}[1].x = {endX - x};");
                source.AppendLine($"    xLinePoints{index}[1].y = {endY - y};");
                source.AppendLine("    obj = lv_line_create(lv_scr_act());");
                source.AppendLine($"    lv_line_set_points(obj, xLinePoints{index}, 2U);");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                source.AppendLine("    lv_obj_set_style_line_width(obj, 2, LV_PART_MAIN);");
                source.AppendLine("    lv_obj_set_style_line_opa(obj, LV_OPA_COVER, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_style_line_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                break;
            case "graphic-rectangle-outline":
            case "graphic-rectangle-filled":
            case "graphic-rectangle-dithered":
                source.AppendLine("    obj = lv_obj_create(lv_scr_act());");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                source.AppendLine("    lv_obj_set_style_radius(obj, 0, LV_PART_MAIN);");
                source.AppendLine(
                    $"    lv_obj_set_style_border_width(obj, " +
                    $"{(control.WidgetTypeId.EndsWith("filled", StringComparison.Ordinal) ? 0 : 1)}, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_style_border_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                source.AppendLine(
                    $"    lv_obj_set_style_bg_opa(obj, " +
                    $"{(control.WidgetTypeId.EndsWith("filled", StringComparison.Ordinal) ? "LV_OPA_COVER" : control.WidgetTypeId.EndsWith("dithered", StringComparison.Ordinal) ? "LV_OPA_50" : "LV_OPA_TRANSP")}, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_style_bg_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                break;
            case "graphic-circle-outline":
            case "graphic-circle-filled":
            case "graphic-circle-dithered":
                source.AppendLine("    obj = lv_obj_create(lv_scr_act());");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                source.AppendLine("    lv_obj_set_style_radius(obj, LV_RADIUS_CIRCLE, LV_PART_MAIN);");
                source.AppendLine(
                    $"    lv_obj_set_style_border_width(obj, " +
                    $"{(control.WidgetTypeId.EndsWith("filled", StringComparison.Ordinal) ? 0 : 1)}, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_style_border_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                source.AppendLine(
                    $"    lv_obj_set_style_bg_opa(obj, " +
                    $"{(control.WidgetTypeId.EndsWith("filled", StringComparison.Ordinal) ? "LV_OPA_COVER" : control.WidgetTypeId.EndsWith("dithered", StringComparison.Ordinal) ? "LV_OPA_50" : "LV_OPA_TRANSP")}, LV_PART_MAIN);");
                source.AppendLine($"    lv_obj_set_style_bg_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_MAIN);");
                break;
            case "graphic-arc-outline":
            case "graphic-arc-filled":
            case "graphic-arc-dithered":
                var startAngle = ReadPercentage(control, "startAngle");
                var sweepAngle = ReadPercentage(control, "sweepAngle");
                source.AppendLine("    obj = lv_arc_create(lv_scr_act());");
                source.AppendLine($"    lv_arc_set_angles(obj, {startAngle}, {startAngle + sweepAngle});");
                source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
                source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
                source.AppendLine($"    lv_obj_set_style_arc_color(obj, lv_color_hex(0x{foregroundHex:X6}), LV_PART_INDICATOR);");
                source.AppendLine("    lv_obj_set_style_arc_width(obj, 2, LV_PART_INDICATOR);");
                break;
            default:
                AppendLvglWidgetDrawCall(source, control, x, y, width, height, index);
                break;
        }

        if (hasClickHandler)
        {
            source.AppendLine(
                $"    lv_obj_add_event_cb(obj, prvEvent{index}, LV_EVENT_CLICKED, NULL);");
        }
    }

    private static void ValidateSupportedWidget(DesignerControl control)
    {
        try
        {
            _ = FreeRTOSx86WidgetCatalog.GetRequired(control.WidgetTypeId);
        }
        catch (KeyNotFoundException)
        {
            throw new NotSupportedException(
                $"Widget '{control.WidgetTypeId}' is not supported by the DOS backend.");
        }

        if (control.Events.Keys.Any(eventId => eventId != "OnClick"))
        {
            throw new NotSupportedException(
                $"Widget '{control.Id}' uses an unsupported event.");
        }
    }

    private static void AppendLvglWidgetDrawCall(
        StringBuilder source,
        DesignerControl control,
        int x,
        int y,
        int width,
        int height,
        int index)
    {
        var text = EscapeCString(control.Text);
        var items = EscapeCString(string.Join(
            "\n",
            ReadOptions(control, "items", "Item 1\nItem 2")));
        var value = ReadPropertyInteger(control, "value", 50);
        var checkedValue = bool.TryParse(
            control.Properties.GetValueOrDefault("checked"),
            out var isChecked) && isChecked;
        var color = ClassicMacRgbColor.Parse(
            control.Properties.GetValueOrDefault("color") ?? "#FFFFFF");
        var background = ClassicMacRgbColor.Parse(
            control.Properties.GetValueOrDefault("backgroundColor") ?? "#000000");
        var colorHex = (color.Red << 16) | (color.Green << 8) | color.Blue;
        var backgroundHex =
            (background.Red << 16) | (background.Green << 8) | background.Blue;
        switch (control.WidgetTypeId)
        {
            case "lvgl-arc":
                source.AppendLine("    obj = lv_arc_create(lv_scr_act());");
                source.AppendLine($"    lv_arc_set_value(obj, {value});");
                break;
            case "lvgl-bar":
                source.AppendLine("    obj = lv_bar_create(lv_scr_act());");
                source.AppendLine($"    lv_bar_set_value(obj, {value}, LV_ANIM_OFF);");
                break;
            case "lvgl-button-matrix":
                source.AppendLine("    obj = lv_btnmatrix_create(lv_scr_act());");
                source.AppendLine($"    lv_btnmatrix_set_map(obj, xButtonMap{index});");
                break;
            case "lvgl-canvas":
                source.AppendLine("    obj = lv_canvas_create(lv_scr_act());");
                source.AppendLine(
                    $"    lv_canvas_set_buffer(obj, xCanvasBuffer{index}, {width}, {height}, LV_IMG_CF_TRUE_COLOR);");
                break;
            case "lvgl-dropdown":
                source.AppendLine("    obj = lv_dropdown_create(lv_scr_act());");
                source.AppendLine($"    lv_dropdown_set_options(obj, \"{items}\");");
                break;
            case "lvgl-image":
                source.AppendLine("    obj = lv_img_create(lv_scr_act());");
                break;
            case "lvgl-roller":
                source.AppendLine("    obj = lv_roller_create(lv_scr_act());");
                source.AppendLine($"    lv_roller_set_options(obj, \"{items}\", LV_ROLLER_MODE_NORMAL);");
                break;
            case "lvgl-slider":
                source.AppendLine("    obj = lv_slider_create(lv_scr_act());");
                source.AppendLine($"    lv_slider_set_value(obj, {value}, LV_ANIM_OFF);");
                break;
            case "lvgl-switch":
                source.AppendLine("    obj = lv_switch_create(lv_scr_act());");
                if (checkedValue)
                {
                    source.AppendLine("    lv_obj_add_state(obj, LV_STATE_CHECKED);");
                }
                break;
            case "lvgl-table":
                source.AppendLine("    obj = lv_table_create(lv_scr_act());");
                source.AppendLine($"    lv_table_set_cell_value(obj, 0, 0, \"{text}\");");
                break;
            case "lvgl-textarea":
                source.AppendLine("    obj = lv_textarea_create(lv_scr_act());");
                source.AppendLine($"    lv_textarea_set_text(obj, \"{text}\");");
                break;
            case "lvgl-animimg":
                source.AppendLine("    obj = lv_animimg_create(lv_scr_act());");
                break;
            case "lvgl-calendar":
                source.AppendLine("    obj = lv_calendar_create(lv_scr_act());");
                break;
            case "lvgl-chart":
                source.AppendLine("    obj = lv_chart_create(lv_scr_act());");
                break;
            case "lvgl-color-wheel":
                source.AppendLine("    obj = lv_colorwheel_create(lv_scr_act(), true);");
                break;
            case "lvgl-image-button":
                source.AppendLine("    obj = lv_imgbtn_create(lv_scr_act());");
                break;
            case "lvgl-keyboard":
                source.AppendLine("    obj = lv_keyboard_create(lv_scr_act());");
                break;
            case "lvgl-led":
                source.AppendLine("    obj = lv_led_create(lv_scr_act());");
                source.AppendLine($"    lv_led_set_brightness(obj, {Math.Clamp(value * 255 / 100, 0, 255)});");
                break;
            case "lvgl-list":
                source.AppendLine("    obj = lv_list_create(lv_scr_act());");
                foreach (var option in ReadOptions(control, "items", "Item 1\nItem 2"))
                {
                    source.AppendLine($"    lv_list_add_btn(obj, NULL, \"{EscapeCString(option)}\");");
                }
                break;
            case "lvgl-menu":
                source.AppendLine("    obj = lv_menu_create(lv_scr_act());");
                source.AppendLine("    child = lv_menu_page_create(obj, \"Menu\");");
                source.AppendLine($"    label = lv_label_create(child);");
                source.AppendLine($"    lv_label_set_text(label, \"{text}\");");
                break;
            case "lvgl-meter":
                source.AppendLine("    obj = lv_meter_create(lv_scr_act());");
                break;
            case "lvgl-message-box":
                source.AppendLine(
                    $"    obj = lv_msgbox_create(lv_scr_act(), \"Message\", \"{text}\", xMessageButtons{index}, true);");
                break;
            case "lvgl-span":
                source.AppendLine("    obj = lv_spangroup_create(lv_scr_act());");
                source.AppendLine($"    lv_span_set_text(lv_spangroup_add_span(obj), \"{text}\");");
                break;
            case "lvgl-spinbox":
                source.AppendLine("    obj = lv_spinbox_create(lv_scr_act());");
                source.AppendLine($"    lv_spinbox_set_value(obj, {value});");
                break;
            case "lvgl-spinner":
                source.AppendLine("    obj = lv_spinner_create(lv_scr_act(), 1000U, 60U);");
                break;
            case "lvgl-tabview":
                source.AppendLine("    obj = lv_tabview_create(lv_scr_act(), LV_DIR_TOP, 24);");
                foreach (var option in ReadOptions(control, "items", "Tab 1\nTab 2"))
                {
                    source.AppendLine(
                        $"    child = lv_tabview_add_tab(obj, \"{EscapeCString(option)}\");");
                }
                break;
            case "lvgl-tileview":
                source.AppendLine("    obj = lv_tileview_create(lv_scr_act());");
                source.AppendLine("    child = lv_tileview_add_tile(obj, 0, 0, LV_DIR_ALL);");
                break;
            case "lvgl-window":
                source.AppendLine("    obj = lv_win_create(lv_scr_act(), 24);");
                source.AppendLine($"    label = lv_label_create(obj);");
                source.AppendLine($"    lv_label_set_text(label, \"{text}\");");
                break;
            default:
                throw new NotSupportedException(
                    $"Widget '{control.WidgetTypeId}' has no Hercules LVGL generator.");
        }

        source.AppendLine($"    lv_obj_set_pos(obj, {x}, {y});");
        source.AppendLine($"    lv_obj_set_size(obj, {width}, {height});");
        source.AppendLine(
            $"    lv_obj_set_style_text_color(obj, lv_color_hex(0x{colorHex:X6}), LV_PART_MAIN);");
        source.AppendLine(
            $"    lv_obj_set_style_bg_color(obj, lv_color_hex(0x{backgroundHex:X6}), LV_PART_MAIN);");
    }

    private static IReadOnlyList<string> ReadOptions(
        DesignerControl control,
        string propertyName,
        string fallback)
    {
        var rawValue = control.Properties.GetValueOrDefault(propertyName) ?? fallback;
        return rawValue
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int ReadPropertyInteger(
        DesignerControl control,
        string propertyName,
        int fallback)
    {
        var value = control.Properties.GetValueOrDefault(propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private static uint ReadColorHex(
        DesignerControl control,
        string propertyName,
        uint fallback)
    {
        var value = control.Properties.GetValueOrDefault(propertyName);
        if (!ClassicMacRgbColor.TryParse(value, out var color))
        {
            return fallback;
        }

        return (uint)((color.Red << 16) | (color.Green << 8) | color.Blue);
    }

    private static int ReadPercentage(DesignerControl control, string propertyName)
    {
        var value = control.Properties.GetValueOrDefault(propertyName) ?? "0";
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException(
                $"Control '{control.Id}' has an invalid '{propertyName}' value.");
        }
        return result;
    }

    private static int ToPixel(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static string EscapeCString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static void ValidateText(string value, string parameterName)
    {
        if (value.Length > 80 ||
            value.Any(character => character < 0x20 || character > 0x7e))
        {
            throw new ArgumentException(
                "DOS GUI text must contain at most 80 printable ASCII characters.",
                parameterName);
        }
    }

    private static string GenerateTaskHeader() =>
        """
        #ifndef FREERTOSX86_GUI_TASK_H
        #define FREERTOSX86_GUI_TASK_H

        void vFreeRTOSx86GuiTask(void * pvParameters);

        #endif
        """ + Environment.NewLine;

    private static string GenerateUserEventsSource(IReadOnlyList<string> handlers)
    {
        var source = new StringBuilder();
        source.AppendLine("#include \"user_events.h\"");
        source.AppendLine();
        source.AppendLine("#ifndef FREERTOSX86_USER_APP");
        source.AppendLine("void user_app_init(void)");
        source.AppendLine("{");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("void user_app_poll(void)");
        source.AppendLine("{");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("void user_app_shutdown(void)");
        source.AppendLine("{");
        source.AppendLine("}");
        source.AppendLine("#endif");
        foreach (var handler in handlers)
        {
            source.AppendLine();
            source.AppendLine($"void {handler}(void)");
            source.AppendLine("{");
            source.AppendLine("    /* Add application-specific action here. */");
            source.AppendLine("}");
        }
        return source.ToString();
    }

    private static string GenerateUserEventsHeader(IReadOnlyList<string> handlers)
    {
        var source = new StringBuilder();
        source.AppendLine("#ifndef FREERTOSX86_USER_EVENTS_H");
        source.AppendLine("#define FREERTOSX86_USER_EVENTS_H");
        source.AppendLine("void user_app_init(void);");
        source.AppendLine("void user_app_poll(void);");
        source.AppendLine("void user_app_shutdown(void);");
        foreach (var handler in handlers)
        {
            source.AppendLine($"void {handler}(void);");
        }
        source.AppendLine("#endif");
        return source.ToString();
    }
}
