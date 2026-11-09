using System.Text;
using System.Globalization;

namespace ClassicMacIde.Core;

/// <summary>Creates the built-in System 7 widget catalog.</summary>
public static class ClassicMacWidgetCatalog
{
    /// <summary>Returns a registry containing the supported standard widget types.</summary>
    public static DesignerWidgetRegistry CreateRegistry() =>
        new(
        [
            Define(
                "static-text",
                "Static Text",
                "static-text",
                "Label",
                100,
                16,
                20,
                12,
                false,
                ItemKind.StaticText),
            Define(
                "editable-text",
                "Editable Text",
                "editable-text",
                "Text",
                120,
                20,
                40,
                18,
                true,
                ItemKind.EditText),
            Define(
                "button",
                "Push Button",
                "button",
                "Button",
                80,
                24,
                48,
                20,
                true,
                ItemKind.Button),
            Define(
                "checkbox",
                "Checkbox",
                "checkbox",
                "Check Box",
                110,
                18,
                55,
                16,
                true,
                ItemKind.CheckBox,
                new DesignerWidgetPropertyDefinition(
                    "checked",
                    "Checked",
                    DesignerWidgetPropertyEditorKind.Boolean,
                    "false")),
            Define(
                "radio-button",
                "Radio Button",
                "radio-button",
                "Radio Button",
                110,
                18,
                55,
                16,
                true,
                ItemKind.RadioButton,
                new DesignerWidgetPropertyDefinition(
                    "group",
                    "Group",
                    DesignerWidgetPropertyEditorKind.Text,
                    "default"),
                new DesignerWidgetPropertyDefinition(
                    "selected",
                    "Selected",
                    DesignerWidgetPropertyEditorKind.Boolean,
                    "false")),
            Define(
                "popup-menu",
                "Popup Menu",
                "popup-menu",
                "Option 1",
                120,
                20,
                64,
                20,
                true,
                ItemKind.PopupMenu,
                new DesignerWidgetPropertyDefinition(
                    "items",
                    "Menu items",
                    DesignerWidgetPropertyEditorKind.MultiLineText,
                    "Option 1\nOption 2"),
                new DesignerWidgetPropertyDefinition(
                    "selectedIndex",
                    "Selected index",
                    DesignerWidgetPropertyEditorKind.Integer,
                    "0")),
            Define(
                "list-box",
                "List Box",
                "list-box",
                "Items",
                130,
                76,
                64,
                40,
                true,
                ItemKind.ListBox,
                new DesignerWidgetPropertyDefinition(
                    "items",
                    "List items",
                    DesignerWidgetPropertyEditorKind.MultiLineText,
                    "Item 1\nItem 2\nItem 3"),
                new DesignerWidgetPropertyDefinition(
                    "selectedIndex",
                    "Selected index",
                    DesignerWidgetPropertyEditorKind.Integer,
                    "-1")),
            new DesignerWidgetDefinition(
                "progress-bar",
                "Progress Bar",
                "progress-bar",
                160,
                14,
                32,
                8,
                string.Empty,
                false,
                [
                    new DesignerWidgetPropertyDefinition(
                        "value",
                        "Value (%)",
                        DesignerWidgetPropertyEditorKind.Integer,
                        "0",
                        MinimumValue: 0,
                        MaximumValue: 100),
                ],
                new ProgressBarWidgetCodeGenerator(),
                supportsValueAccess: true),
            Define(
                "scrollbar",
                "Scrollbar",
                "scrollbar",
                string.Empty,
                16,
                80,
                16,
                40,
                true,
                ItemKind.Scrollbar,
                new DesignerWidgetPropertyDefinition(
                    "minimum",
                    "Minimum",
                    DesignerWidgetPropertyEditorKind.Integer,
                    "0"),
                new DesignerWidgetPropertyDefinition(
                    "maximum",
                    "Maximum",
                    DesignerWidgetPropertyEditorKind.Integer,
                    "100"),
                new DesignerWidgetPropertyDefinition(
                    "value",
                    "Value",
                    DesignerWidgetPropertyEditorKind.Integer,
                    "0")),
            .. ClassicMacGraphicsCatalog.CreateDefinitions(),
        ]);

    private static DesignerWidgetDefinition Define(
        string id,
        string displayName,
        string previewRendererId,
        string defaultText,
        double width,
        double height,
        double minimumWidth,
        double minimumHeight,
        bool interactive,
        ItemKind kind,
        params DesignerWidgetPropertyDefinition[] properties) =>
        new(
            id,
            displayName,
            previewRendererId,
            width,
            height,
            minimumWidth,
            minimumHeight,
            defaultText,
            interactive,
            properties,
            new DitlWidgetCodeGenerator(id, kind),
            supportsTextAccess: kind is
                ItemKind.StaticText or
                ItemKind.EditText or
                ItemKind.Button or
                ItemKind.CheckBox or
                ItemKind.RadioButton or
                ItemKind.PopupMenu,
            supportsValueAccess: kind is
                ItemKind.CheckBox or
                ItemKind.RadioButton or
                ItemKind.PopupMenu or
                ItemKind.ListBox or
                ItemKind.Scrollbar);

    private enum ItemKind
    {
        StaticText,
        EditText,
        Button,
        CheckBox,
        RadioButton,
        PopupMenu,
        ListBox,
        Scrollbar,
    }

    private sealed class ProgressBarWidgetCodeGenerator : MacWidgetCodeGenerator
    {
        public override string WidgetTypeId => "progress-bar";

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
            var suffix = context.ItemNumber.ToString(CultureInfo.InvariantCulture);
            source.AppendLine(
                $"static short gProgressValue{suffix} = {ParseValue(context.Control.Properties)};");
            source.AppendLine(
                $"static pascal void DrawProgressBar{suffix}(DialogPtr dialog, short item)");
            source.AppendLine("{");
            source.AppendLine("    short itemType;");
            source.AppendLine("    Handle itemHandle;");
            source.AppendLine("    Rect bounds;");
            source.AppendLine("    Rect fillBounds;");
            source.AppendLine("    GrafPtr previousPort;");
            source.AppendLine("    GetDialogItem(dialog, item, &itemType, &itemHandle, &bounds);");
            source.AppendLine("    (void)itemType;");
            source.AppendLine("    (void)itemHandle;");
            source.AppendLine("    GetPort(&previousPort);");
            source.AppendLine("    SetPort((GrafPtr)dialog);");
            source.AppendLine("    PenNormal();");
            source.AppendLine("    ForeColor(blackColor);");
            source.AppendLine("    EraseRect(&bounds);");
            source.AppendLine("    FrameRect(&bounds);");
            source.AppendLine("    fillBounds = bounds;");
            source.AppendLine("    fillBounds.left++;");
            source.AppendLine("    fillBounds.top++;");
            source.AppendLine("    fillBounds.right--;");
            source.AppendLine("    fillBounds.bottom--;");
            source.AppendLine("    if (fillBounds.right > fillBounds.left && fillBounds.bottom > fillBounds.top)");
            source.AppendLine("    {");
            source.AppendLine(
                $"        fillBounds.right = (short)(fillBounds.left + (long)(fillBounds.right - fillBounds.left) * gProgressValue{suffix} / 100);");
            source.AppendLine("        if (fillBounds.right > fillBounds.left)");
            source.AppendLine("            PaintRect(&fillBounds);");
            source.AppendLine("    }");
            source.AppendLine("    SetPort(previousPort);");
            source.AppendLine("}");
            source.AppendLine();
        }

        public override void AppendInitialization(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = context.ItemNumber.ToString(CultureInfo.InvariantCulture);
            source.AppendLine("    {");
            source.AppendLine("        short itemType;");
            source.AppendLine("        Handle itemHandle;");
            source.AppendLine("        Rect itemBounds;");
            source.AppendLine(
                $"        GetDialogItem(dialog, {context.ItemNumber}, &itemType, &itemHandle, &itemBounds);");
            source.AppendLine(
                $"        SetDialogItem(dialog, {context.ItemNumber}, itemType, (Handle)DrawProgressBar{suffix}, &itemBounds);");
            source.AppendLine(
                $"        DrawProgressBar{suffix}(dialog, {context.ItemNumber});");
            source.AppendLine("    }");
        }

        public override void AppendControlValueGetter(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = context.ItemNumber.ToString(CultureInfo.InvariantCulture);
            source.AppendLine($"    *value = gProgressValue{suffix};");
            source.AppendLine("    return noErr;");
        }

        public override void AppendControlValueSetter(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = context.ItemNumber.ToString(CultureInfo.InvariantCulture);
            source.AppendLine("    if (value < 0 || value > 100) return paramErr;");
            source.AppendLine($"    gProgressValue{suffix} = value;");
            source.AppendLine($"    DrawProgressBar{suffix}(dialog, item);");
            source.AppendLine("    return noErr;");
        }

        public override void AppendControlBoundsSetter(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = context.ItemNumber.ToString(CultureInfo.InvariantCulture);
            source.AppendLine($"            DrawProgressBar{suffix}(dialog, item);");
        }

        private static int ParseValue(IReadOnlyDictionary<string, string> properties) =>
            int.TryParse(
                properties.GetValueOrDefault("value"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0;
    }

    private sealed class DitlWidgetCodeGenerator(
        string widgetTypeId,
        ItemKind kind) : MacWidgetCodeGenerator
    {
        public override string WidgetTypeId { get; } = widgetTypeId;

        public override void AppendResourceItem(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(context);

            var item = kind switch
            {
                ItemKind.StaticText =>
                    $"StaticText {{ enabled, \"{context.EscapedText}\" }}",
                ItemKind.EditText =>
                    $"EditText {{ enabled, \"{context.EscapedText}\" }}",
                ItemKind.Button =>
                    $"Button {{ enabled, \"{context.EscapedText}\" }}",
                ItemKind.CheckBox =>
                    $"CheckBox {{ enabled, \"{context.EscapedText}\" }}",
                ItemKind.RadioButton =>
                    $"RadioButton {{ enabled, \"{context.EscapedText}\" }}",
                ItemKind.PopupMenu =>
                    $"Button {{ enabled, \"{context.EscapedText}\" }}",
                ItemKind.ListBox or ItemKind.Scrollbar =>
                    "UserItem { enabled }",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            source.AppendLine(
                $"        {{ {context.Y}, {context.X}, {context.Y + context.Height}, {context.X + context.Width} }}, {item}{(context.IsLastItem ? string.Empty : ";")}");
        }

        public override void AppendAdditionalResources(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            if (kind != ItemKind.PopupMenu)
            {
                return;
            }

            var menuId = MenuResourceId(context);
            source.AppendLine($"resource 'MENU' ({menuId}) {{");
            source.AppendLine($"    {menuId},");
            source.AppendLine("    textMenuProc,");
            source.AppendLine("    0x7FFFFFFF,");
            source.AppendLine("    enabled,");
            source.AppendLine($"    \"{EscapeRezString(context.Control.Text)}\",");
            source.AppendLine("    {");
            foreach (var item in ReadLines(
                         context.Control.Properties.GetValueOrDefault("items")))
            {
                source.AppendLine(
                    $"        \"{EscapeRezString(item)}\", noIcon, noKey, noMark, plain;");
            }
            source.AppendLine("    }");
            source.AppendLine("};");
            source.AppendLine();
        }

        public override void AppendDeclarations(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.PopupMenu:
                    source.AppendLine($"static MenuHandle gPopupMenu{suffix} = NULL;");
                    source.AppendLine($"static short gPopupSelection{suffix} = 0;");
                    source.AppendLine($"static Str255 gPopupTitle{suffix};");
                    break;
                case ItemKind.ListBox:
                    source.AppendLine($"static ListHandle gList{suffix} = NULL;");
                    break;
                case ItemKind.Scrollbar:
                    source.AppendLine($"static ControlHandle gScroll{suffix} = NULL;");
                    break;
            }
        }

        public override void AppendInitialization(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.EditText:
                    source.AppendLine("                    if (gTestSession)");
                    source.AppendLine("                    {");
                    source.AppendLine("                        Str255 testValue;");
                    source.AppendLine("                        Handle textItem;");
                    source.AppendLine("                        const char *testText = \"Widget Test\";");
                    source.AppendLine("                        short textLength = (short)strlen(testText);");
                    source.AppendLine(
                        $"                        textItem = DesignerDialogItem(gDesignerDialog, {context.ItemNumber});");
                    source.AppendLine("                        testValue[0] = (unsigned char)textLength;");
                    source.AppendLine(
                        "                        memcpy(&testValue[1], testText, (size_t)textLength);");
                    source.AppendLine(
                        "                        SetDialogItemText(textItem, testValue);");
                    source.AppendLine(
                        "                        GetDialogItemText(textItem, testValue);");
                    source.AppendLine(
                        "                        if (testValue[0] != (unsigned char)textLength || memcmp(&testValue[1], testText, (size_t)textLength) != 0)");
                    source.AppendLine(
                        "                            BridgeWriteError(\"EDIT_TEXT\", \"Edit field value did not round-trip\");");
                    source.AppendLine("                    }");
                    break;
                case ItemKind.CheckBox:
                    source.AppendLine("    {");
                    AppendControlValue(
                        source,
                        context,
                        ParseBoolean(context.Control.Properties, "checked", false) ? 1 : 0,
                        testOnly: false);
                    source.AppendLine("    }");
                    break;
                case ItemKind.RadioButton:
                    source.AppendLine("    {");
                    AppendControlValue(
                        source,
                        context,
                        ParseBoolean(context.Control.Properties, "selected", false) ? 1 : 0,
                        testOnly: false);
                    source.AppendLine("    }");
                    break;
                case ItemKind.PopupMenu:
                    source.AppendLine(
                        $"    gPopupMenu{suffix} = GetMenu({MenuResourceId(context)});");
                    source.AppendLine(
                        $"    gPopupSelection{suffix} = {ParseInteger(context.Control.Properties, "selectedIndex", 0) + 1};");
                    source.AppendLine($"    if (gPopupMenu{suffix} != NULL)");
                    source.AppendLine("    {");
                    source.AppendLine(
                        $"        GetMenuItemText(gPopupMenu{suffix}, gPopupSelection{suffix}, gPopupTitle{suffix});");
                    source.AppendLine(
                        $"        SetControlTitle((ControlHandle)DesignerDialogItem(dialog, {context.ItemNumber}), gPopupTitle{suffix});");
                    source.AppendLine("    }");
                    break;
                case ItemKind.ListBox:
                    AppendListInitialization(source, context, suffix);
                    break;
                case ItemKind.Scrollbar:
                    AppendScrollbarInitialization(source, context, suffix);
                    break;
            }
        }

        public override void AppendTestInteraction(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.CheckBox:
                    source.AppendLine("                    if (gTestSession)");
                    source.AppendLine("                    {");
                    source.AppendLine("                        short itemType;");
                    source.AppendLine("                        Handle itemHandle;");
                    source.AppendLine("                        Rect itemBounds;");
                    source.AppendLine(
                        $"                        GetDialogItem(gDesignerDialog, {context.ItemNumber}, &itemType, &itemHandle, &itemBounds);");
                    source.AppendLine("                        ControlHandle control = (ControlHandle)itemHandle;");
                    source.AppendLine(
                        "                        SetControlValue(control, GetControlValue(control) == 0 ? 1 : 0);");
                    source.AppendLine("                    }");
                    break;
                case ItemKind.RadioButton:
                    AppendControlValue(
                        source,
                        context,
                        1,
                        testOnly: true,
                        verifyValue: true);
                    break;
                case ItemKind.PopupMenu:
                    source.AppendLine("                    if (gTestSession)");
                    source.AppendLine("                    {");
                    source.AppendLine(
                        $"                        gPopupSelection{suffix} = {ParseInteger(context.Control.Properties, "selectedIndex", 0) + 1};");
                    source.AppendLine(
                        $"                        GetMenuItemText(gPopupMenu{suffix}, gPopupSelection{suffix}, gPopupTitle{suffix});");
                    source.AppendLine(
                        $"                        SetControlTitle((ControlHandle)DesignerDialogItem(gDesignerDialog, {context.ItemNumber}), gPopupTitle{suffix});");
                    source.AppendLine(
                        $"                        if (gPopupSelection{suffix} != {ParseInteger(context.Control.Properties, "selectedIndex", 0) + 1})");
                    source.AppendLine(
                        "                            BridgeWriteError(\"POPUP_SELECTION\", \"Popup selection did not match\");");
                    source.AppendLine("                    }");
                    source.AppendLine("                    else if (gPopupMenu" + suffix + " != NULL)");
                    source.AppendLine("                    {");
                    source.AppendLine("                        long selection;");
                    source.AppendLine("                        Point popupPoint;");
                    source.AppendLine("                        GetMouse(&popupPoint);");
                    source.AppendLine("                        LocalToGlobal(&popupPoint);");
                    source.AppendLine("                        selection = PopUpMenuSelect(gPopupMenu" + suffix +
                        ", popupPoint.v, popupPoint.h, gPopupSelection" + suffix + ");");
                    source.AppendLine("                        if ((short)(selection & 0xFFFF) > 0)");
                    source.AppendLine("                        {");
                    source.AppendLine(
                        "                            gPopupSelection" + suffix + " = (short)(selection & 0xFFFF);");
                    source.AppendLine(
                        "                            GetMenuItemText(gPopupMenu" + suffix +
                        ", gPopupSelection" + suffix + ", gPopupTitle" + suffix + ");");
                    source.AppendLine(
                        "                            SetControlTitle((ControlHandle)DesignerDialogItem(gDesignerDialog, " +
                        context.ItemNumber + "), gPopupTitle" + suffix + ");");
                    source.AppendLine("                        }");
                    source.AppendLine("                    }");
                    break;
                case ItemKind.ListBox:
                    var selectedIndex = ParseInteger(
                        context.Control.Properties,
                        "selectedIndex",
                        -1);
                    if (selectedIndex >= 0)
                    {
                        source.AppendLine("                    if (gTestSession && gList" + suffix + " != NULL)");
                        source.AppendLine("                    {");
                        source.AppendLine("                        Cell selectedCell;");
                        source.AppendLine("                        selectedCell.h = 0;");
                        source.AppendLine($"                        selectedCell.v = {selectedIndex};");
                        source.AppendLine(
                            $"                        LSetSelect(true, selectedCell, gList{suffix});");
                        source.AppendLine(
                            $"                        if (!LGetSelect(true, &selectedCell, gList{suffix}) || selectedCell.v != {selectedIndex})");
                        source.AppendLine(
                            "                            BridgeWriteError(\"LIST_SELECTION\", \"List selection did not match\");");
                        source.AppendLine("                    }");
                    }
                    break;
                case ItemKind.Scrollbar:
                    source.AppendLine("                    if (gTestSession && gScroll" + suffix + " != NULL)");
                    source.AppendLine(
                        $"                        SetControlValue(gScroll{suffix}, {ParseInteger(context.Control.Properties, "value", 0)});");
                    source.AppendLine(
                        $"                    if (GetControlValue(gScroll{suffix}) != {ParseInteger(context.Control.Properties, "value", 0)})");
                    source.AppendLine(
                        "                        BridgeWriteError(\"SCROLL_VALUE\", \"Scrollbar value did not match\");");
                    break;
            }
        }

        public override void AppendEventHandling(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.ListBox:
                    source.AppendLine(
                        $"            if (gList{suffix} != NULL && PtInRect(where, &(**gList{suffix}).rView))");
                    source.AppendLine("            {");
                    source.AppendLine($"                LClick(where, event.modifiers, gList{suffix});");
                    source.AppendLine($"                item = {context.ItemNumber};");
                    source.AppendLine("            }");
                    break;
                case ItemKind.Scrollbar:
                    source.AppendLine(
                        $"            if (gScroll{suffix} != NULL && PtInRect(where, &(**gScroll{suffix}).contrlRect))");
                    source.AppendLine(
                        $"                TrackControl(gScroll{suffix}, where, NULL);");
                    break;
            }
        }

        public override void AppendControlValueGetter(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.CheckBox:
                case ItemKind.RadioButton:
                    source.AppendLine("    short itemType;");
                    source.AppendLine("    Handle itemHandle;");
                    source.AppendLine("    Rect itemBounds;");
                    source.AppendLine(
                        "    GetDialogItem(dialog, item, &itemType, &itemHandle, &itemBounds);");
                    source.AppendLine("    if (itemHandle == NULL) return paramErr;");
                    source.AppendLine(
                        "    *value = GetControlValue((ControlHandle)itemHandle);");
                    source.AppendLine("    return noErr;");
                    break;
                case ItemKind.PopupMenu:
                    source.AppendLine($"    *value = gPopupSelection{suffix} - 1;");
                    source.AppendLine("    return noErr;");
                    break;
                case ItemKind.ListBox:
                    source.AppendLine("    Cell selectedCell;");
                    source.AppendLine($"    if (gList{suffix} == NULL) return paramErr;");
                    source.AppendLine("    selectedCell.h = 0;");
                    source.AppendLine("    selectedCell.v = 0;");
                    source.AppendLine(
                        $"    *value = LGetSelect(true, &selectedCell, gList{suffix}) ? selectedCell.v : -1;");
                    source.AppendLine("    return noErr;");
                    break;
                case ItemKind.Scrollbar:
                    source.AppendLine($"    if (gScroll{suffix} == NULL) return paramErr;");
                    source.AppendLine($"    *value = GetControlValue(gScroll{suffix});");
                    source.AppendLine("    return noErr;");
                    break;
            }
        }

        public override void AppendControlValueSetter(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.CheckBox:
                case ItemKind.RadioButton:
                    source.AppendLine("    short itemType;");
                    source.AppendLine("    Handle itemHandle;");
                    source.AppendLine("    Rect itemBounds;");
                    source.AppendLine("    if (value != 0 && value != 1) return paramErr;");
                    source.AppendLine(
                        "    GetDialogItem(dialog, item, &itemType, &itemHandle, &itemBounds);");
                    source.AppendLine("    if (itemHandle == NULL) return paramErr;");
                    source.AppendLine(
                        "    SetControlValue((ControlHandle)itemHandle, value);");
                    if (kind == ItemKind.RadioButton)
                    {
                        source.AppendLine("    if (value != 0) ApplyRadioGroupSelection(item);");
                    }
                    source.AppendLine("    InvalidateDesignerDialogItem(dialog, item);");
                    source.AppendLine("    return noErr;");
                    break;
                case ItemKind.PopupMenu:
                    var popupItemCount = ReadLines(
                        context.Control.Properties.GetValueOrDefault("items")).Count;
                    source.AppendLine(
                        $"    if (value < 0 || value >= {popupItemCount} || gPopupMenu{suffix} == NULL)");
                    source.AppendLine("        return paramErr;");
                    source.AppendLine($"    gPopupSelection{suffix} = value + 1;");
                    source.AppendLine(
                        $"    GetMenuItemText(gPopupMenu{suffix}, gPopupSelection{suffix}, gPopupTitle{suffix});");
                    source.AppendLine(
                        $"    SetControlTitle((ControlHandle)DesignerDialogItem(dialog, {context.ItemNumber}), gPopupTitle{suffix});");
                    source.AppendLine("    InvalidateDesignerDialogItem(dialog, item);");
                    source.AppendLine("    return noErr;");
                    break;
                case ItemKind.ListBox:
                    var listItemCount = ReadLines(
                        context.Control.Properties.GetValueOrDefault("items")).Count;
                    source.AppendLine("    Cell selectedCell;");
                    source.AppendLine("    short row;");
                    source.AppendLine(
                        $"    if (value < -1 || value >= {listItemCount} || gList{suffix} == NULL)");
                    source.AppendLine("        return paramErr;");
                    source.AppendLine("    selectedCell.h = 0;");
                    source.AppendLine("    if (value == -1)");
                    source.AppendLine("    {");
                    source.AppendLine($"        for (row = 0; row < {listItemCount}; row++)");
                    source.AppendLine("        {");
                    source.AppendLine("            selectedCell.v = row;");
                    source.AppendLine(
                        $"            LSetSelect(false, selectedCell, gList{suffix});");
                    source.AppendLine("        }");
                    source.AppendLine("    }");
                    source.AppendLine("    else");
                    source.AppendLine("    {");
                    source.AppendLine("        selectedCell.v = value;");
                    source.AppendLine(
                        $"        LSetSelect(true, selectedCell, gList{suffix});");
                    source.AppendLine("    }");
                    source.AppendLine("    InvalidateDesignerDialogItem(dialog, item);");
                    source.AppendLine("    return noErr;");
                    break;
                case ItemKind.Scrollbar:
                    var minimum = ParseInteger(
                        context.Control.Properties,
                        "minimum",
                        0);
                    var maximum = ParseInteger(
                        context.Control.Properties,
                        "maximum",
                        100);
                    source.AppendLine(
                        $"    if (value < {minimum} || value > {maximum} || gScroll{suffix} == NULL)");
                    source.AppendLine("        return paramErr;");
                    source.AppendLine($"    SetControlValue(gScroll{suffix}, value);");
                    source.AppendLine("    InvalidateDesignerDialogItem(dialog, item);");
                    source.AppendLine("    return noErr;");
                    break;
            }
        }

        public override void AppendControlBoundsSetter(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            switch (kind)
            {
                case ItemKind.ListBox:
                    source.AppendLine(
                        $"            if (gList{suffix} == NULL) {{ result = paramErr; break; }}");
                    source.AppendLine(
                        $"            LSize((short)(itemBounds.right - itemBounds.left), (short)(itemBounds.bottom - itemBounds.top), gList{suffix});");
                    source.AppendLine($"            (**gList{suffix}).rView = itemBounds;");
                    source.AppendLine(
                        $"            LUpdate(((WindowPtr)dialog)->visRgn, gList{suffix});");
                    break;
                case ItemKind.Scrollbar:
                    source.AppendLine(
                        $"            if (gScroll{suffix} == NULL) {{ result = paramErr; break; }}");
                    source.AppendLine(
                        $"            MoveControl(gScroll{suffix}, itemBounds.left, itemBounds.top);");
                    source.AppendLine(
                        $"            SizeControl(gScroll{suffix}, (short)(itemBounds.right - itemBounds.left), (short)(itemBounds.bottom - itemBounds.top));");
                    break;
            }
        }

        public override void AppendCleanup(
            StringBuilder source,
            MacWidgetCodeGenerationContext context)
        {
            var suffix = Suffix(context);
            if (kind == ItemKind.ListBox)
            {
                source.AppendLine(
                    $"    if (gList{suffix} != NULL) LDispose(gList{suffix});");
            }
        }

        private static void AppendListInitialization(
            StringBuilder source,
            MacWidgetCodeGenerationContext context,
            string suffix)
        {
            var items = ReadLines(context.Control.Properties.GetValueOrDefault("items"));
            source.AppendLine($"    Rect listView{suffix} = {{ {context.Y}, {context.X}, {context.Y + context.Height}, {context.X + context.Width} }};");
            source.AppendLine($"    Rect listData{suffix} = {{ 0, 0, 0, 1 }};");
            source.AppendLine($"    Point cellSize{suffix};");
            source.AppendLine($"    cellSize{suffix}.h = {Math.Max(1, context.Width - 2)};");
            source.AppendLine($"    cellSize{suffix}.v = 16;");
            source.AppendLine(
                $"    gList{suffix} = LNew(&listView{suffix}, &listData{suffix}, cellSize{suffix}, 0, (WindowPtr)dialog, true, false, false, true);");
            source.AppendLine($"    if (gList{suffix} != NULL)");
            source.AppendLine("    {");
            source.AppendLine($"        Cell listCell{suffix};");
            source.AppendLine($"        listCell{suffix}.h = 0;");
            source.AppendLine($"        LAddRow({items.Count}, 0, gList{suffix});");
            for (var index = 0; index < items.Count; index++)
            {
                var escapedItem = EscapeCString(items[index]);
                source.AppendLine($"        listCell{suffix}.v = {index};");
                source.AppendLine(
                    $"        LSetCell(\"{escapedItem}\", {Encoding.ASCII.GetByteCount(items[index])}, listCell{suffix}, gList{suffix});");
            }
            source.AppendLine("    }");
        }

        private static void AppendScrollbarInitialization(
            StringBuilder source,
            MacWidgetCodeGenerationContext context,
            string suffix)
        {
            source.AppendLine($"    Rect scrollBounds{suffix} = {{ {context.Y}, {context.X}, {context.Y + context.Height}, {context.X + context.Width} }};");
            source.AppendLine($"    Str255 scrollTitle{suffix};");
            source.AppendLine($"    scrollTitle{suffix}[0] = 0;");
            source.AppendLine(
                $"    gScroll{suffix} = NewControl((WindowPtr)dialog, &scrollBounds{suffix}, scrollTitle{suffix}, true, {ParseInteger(context.Control.Properties, "value", 0)}, {ParseInteger(context.Control.Properties, "minimum", 0)}, {ParseInteger(context.Control.Properties, "maximum", 100)}, scrollBarProc, 0);");
        }

        private static void AppendControlValue(
            StringBuilder source,
            MacWidgetCodeGenerationContext context,
            int value,
            bool testOnly,
            bool verifyValue = false)
        {
            var indent = testOnly ? "                    " : "    ";
            if (testOnly)
            {
                source.AppendLine($"{indent}if (gTestSession)");
                source.AppendLine($"{indent}{{");
                indent += "    ";
            }
            source.AppendLine($"{indent}short itemType;");
            source.AppendLine($"{indent}Handle itemHandle;");
            source.AppendLine($"{indent}Rect itemBounds;");
            source.AppendLine(
                $"{indent}GetDialogItem(gDesignerDialog, {context.ItemNumber}, &itemType, &itemHandle, &itemBounds);");
            source.AppendLine(
                $"{indent}SetControlValue((ControlHandle)itemHandle, {value});");
            if (verifyValue)
            {
                source.AppendLine(
                    $"{indent}if (GetControlValue((ControlHandle)itemHandle) != {value})");
                source.AppendLine(
                    $"{indent}    BridgeWriteError(\"CONTROL_VALUE\", \"Control value did not match\");");
            }
            if (testOnly)
            {
                source.AppendLine("                    }");
            }
        }

        private static int MenuResourceId(MacWidgetCodeGenerationContext context) =>
            256 + context.ItemNumber;

        private static string Suffix(MacWidgetCodeGenerationContext context) =>
            context.ItemNumber.ToString(CultureInfo.InvariantCulture);

        private static int ParseInteger(
            IReadOnlyDictionary<string, string> properties,
            string propertyId,
            int fallback) =>
            int.TryParse(
                properties.GetValueOrDefault(propertyId),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : fallback;

        private static bool ParseBoolean(
            IReadOnlyDictionary<string, string> properties,
            string propertyId,
            bool fallback) =>
            bool.TryParse(properties.GetValueOrDefault(propertyId), out var value)
                ? value
                : fallback;

        private static IReadOnlyList<string> ReadLines(string? text) =>
            (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static string EscapeRezString(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        private static string EscapeCString(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
