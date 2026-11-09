namespace ClassicMacIde.Core;

/// <summary>Creates built-in file, menu, serial, and transfer components.</summary>
public static class ClassicMacNonVisualComponentCatalog
{
    /// <summary>Returns a registry containing the built-in nonvisual components.</summary>
    public static DesignerNonVisualComponentRegistry CreateRegistry() =>
        new(
        [
            Define(
                "file-access",
                "Standard File",
                DesignerNonVisualComponentCategory.File,
                [
                    Text("openPrompt", "Open prompt", "Open:"),
                    Text("savePrompt", "Save prompt", "Save as:"),
                    Text("defaultName", "Default name", "Untitled"),
                    Text("fileType", "File type", "TEXT"),
                ],
                [Event("OnError")]),
            Define(
                "menu-item",
                "Menu Item",
                DesignerNonVisualComponentCategory.Menu,
                [
                    Text("menuTitle", "Menu title", "File"),
                    Text("caption", "Caption", "New Item"),
                    Text("shortcut", "Shortcut", string.Empty),
                    new(
                        "enabled",
                        "Enabled",
                        DesignerWidgetPropertyEditorKind.Boolean,
                        "true"),
                ],
                [Event("OnClick")]),
            Define(
                "serial-port",
                "Serial Port",
                DesignerNonVisualComponentCategory.Communications,
                [
                    Choice("port", "Port", "Modem", "Modem", "Printer"),
                    Choice("baud", "Baud rate", "9600", "300", "1200", "2400", "4800", "9600", "19200", "38400", "57600"),
                    Choice("dataBits", "Data bits", "8", "7", "8"),
                    Choice("parity", "Parity", "None", "None", "Even", "Odd"),
                    Choice("stopBits", "Stop bits", "1", "1", "2"),
                    Choice("flowControl", "Flow control", "None", "None", "Hardware", "Software"),
                ],
                [Event("OnDataAvailable"), Event("OnError")]),
            Define(
                "protocol-transfer",
                "Protocol Transfer",
                DesignerNonVisualComponentCategory.Communications,
                [
                    Text("serialPortId", "Serial component", string.Empty),
                    Text("fileName", "ZMODEM file name", "TRANSFER.BIN"),
                    Choice(
                        "protocol",
                        "Protocol",
                        "XmodemCRC",
                        "Xmodem",
                        "XmodemCRC",
                        "Xmodem1K",
                        "Zmodem"),
                    new(
                        "timeoutSeconds",
                        "Timeout (seconds)",
                        DesignerWidgetPropertyEditorKind.Integer,
                        "10",
                        MinimumValue: 1,
                        MaximumValue: 120),
                ],
                [Event("OnProgress"), Event("OnComplete"), Event("OnError")]),
        ]);

    private static DesignerNonVisualComponentDefinition Define(
        string id,
        string displayName,
        DesignerNonVisualComponentCategory category,
        IReadOnlyList<DesignerNonVisualComponentPropertyDefinition> properties,
        IReadOnlyList<DesignerWidgetEventDefinition> events) =>
        new(
            id,
            displayName,
            category,
            properties,
            events,
            new BuiltInComponentCodeGenerator(id));

    private static DesignerNonVisualComponentPropertyDefinition Text(
        string id,
        string displayName,
        string defaultValue) =>
        new(id, displayName, DesignerWidgetPropertyEditorKind.Text, defaultValue);

    private static DesignerNonVisualComponentPropertyDefinition Choice(
        string id,
        string displayName,
        string defaultValue,
        params string[] options) =>
        new(
            id,
            displayName,
            DesignerWidgetPropertyEditorKind.Text,
            defaultValue,
            options);

    private static DesignerWidgetEventDefinition Event(string id) => new(id, id);

    private sealed class BuiltInComponentCodeGenerator(string componentTypeId) :
        MacNonVisualComponentCodeGenerator
    {
        public override string ComponentTypeId { get; } = componentTypeId;
    }
}
