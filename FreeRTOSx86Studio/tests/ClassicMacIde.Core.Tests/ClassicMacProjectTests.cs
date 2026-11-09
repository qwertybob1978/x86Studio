using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class ClassicMacProjectTests
{
    private readonly DesignerWidgetRegistry _registry =
        ClassicMacWidgetCatalog.CreateRegistry();
    private readonly DesignerNonVisualComponentRegistry _componentRegistry =
        ClassicMacNonVisualComponentCatalog.CreateRegistry();

    [Fact]
    public void SerialTerminalSampleProjectLoadsWithBoundSerialEvents()
    {
        var project = ClassicMacProjectSerializer.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Samples",
                "SerialTerminal.cmideproj"),
            _registry,
            _componentRegistry);

        Assert.Equal("mac-color-classic-68k", project.TargetProfileId);
        Assert.Equal("TerminalForm", project.FormTypeName);
        Assert.Contains(
            project.Controls,
            control => control.Id == "SendButton" &&
                control.Events.GetValueOrDefault("OnClick") == "TerminalSendClick");
        Assert.Contains(
            project.Controls,
            control => control.Id == "ClearButton" &&
                control.Events.GetValueOrDefault("OnClick") == "TerminalClearClick");
        var serialPort = Assert.Single(
            project.Components,
            component => component.Id == "SerialPort1");
        Assert.Equal("Modem", serialPort.Properties["port"]);
        Assert.Equal(
            "SerialDataAvailable",
            serialPort.Events["OnDataAvailable"]);
        Assert.Equal("SerialError", serialPort.Events["OnError"]);
    }

    [Fact]
    public void SerializeAndDeserializePreservesProjectAndWidgetProperties()
    {
        var controls = new[]
        {
            _registry.CreateControl("button-save", "button", 16, 24, "Save")
                .WithEvent("OnClick", "SaveButtonClick"),
            _registry.CreateControl("checkbox-enabled", "checkbox", 24, 64, "Enabled")
                .WithProperty("checked", "true"),
        };
        var project = new ClassicMacProject(
            "Sample",
            "mac-se-68k",
            "Sample Window",
            controls,
            "SampleForm",
            formProperties: new MacFormProperties
            {
                Resizable = true,
                HasCloseBox = true,
                HasZoomBox = true,
                Visible = false,
                CenterOnScreen = false,
                Left = 40,
                Top = 30,
                Width = 320,
                Height = 200,
            });

        var restored = ClassicMacProjectSerializer.Deserialize(
            ClassicMacProjectSerializer.Serialize(project),
            _registry);

        Assert.Equal(project.Name, restored.Name);
        Assert.Equal(project.TargetProfileId, restored.TargetProfileId);
        Assert.Equal(project.FormTitle, restored.FormTitle);
        Assert.Equal(project.FormTypeName, restored.FormTypeName);
        Assert.Equal(project.FormProperties, restored.FormProperties);
        Assert.Equal(project.Controls.Count, restored.Controls.Count);
        for (var index = 0; index < controls.Length; index++)
        {
            AssertControlsEqual(controls[index], restored.Controls[index]);
        }
    }

    [Fact]
    public void SaveAndLoadRoundTripsAProjectFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"classic-mac-{Guid.NewGuid():N}.cmideproj");
        var project = new ClassicMacProject(
            "Saved",
            "mac-se-68k",
            "Saved Window",
            [_registry.CreateControl("button-run", "button", 8, 8)]);

        try
        {
            ClassicMacProjectSerializer.Save(path, project);

            var restored = ClassicMacProjectSerializer.Load(path, _registry);

            Assert.Equal(project.Name, restored.Name);
            Assert.Equal(project.FormTitle, restored.FormTitle);
            Assert.Equal(project.FormTypeName, restored.FormTypeName);
            AssertControlsEqual(project.Controls[0], restored.Controls[0]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void DeserializeRejectsUnsupportedSchemaVersion()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            ClassicMacProjectSerializer.Deserialize(
                """{"schemaVersion":99}""",
                _registry));

        Assert.Contains("version 99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeDefaultsFormTypeForExistingSchemaOneProjects()
    {
        var restored = ClassicMacProjectSerializer.Deserialize(
            """
            {
              "schemaVersion": 1,
              "name": "Legacy",
              "targetProfileId": "mac-se-68k",
              "formTitle": "Legacy Window",
              "controls": []
            }
            """,
            _registry);

        Assert.Equal("MainForm", restored.FormTypeName);
        Assert.Equal(new MacFormProperties(), restored.FormProperties);
    }

    [Fact]
    public void DeserializeRejectsFormBoundsOutsideTheTargetScreen()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            ClassicMacProjectSerializer.Deserialize(
                """
                {
                  "schemaVersion": 1,
                  "name": "Invalid",
                  "targetProfileId": "mac-se-68k",
                  "formTitle": "Invalid Window",
                  "formProperties": { "width": 600 },
                  "controls": []
                }
                """,
                _registry));

        Assert.Contains("form properties", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeserializeDefaultsEventMappingsForLegacyControls()
    {
        var restored = ClassicMacProjectSerializer.Deserialize(
            """
            {
              "schemaVersion": 1,
              "name": "Legacy",
              "targetProfileId": "mac-se-68k",
              "formTitle": "Legacy Window",
              "controls": [
                {
                  "id": "button-run",
                  "widgetTypeId": "button",
                  "text": "Run",
                  "x": 8,
                  "y": 8,
                  "width": 80,
                  "height": 24,
                  "properties": {}
                }
              ]
            }
            """,
            _registry);

        Assert.Empty(restored.Controls[0].Events);
    }

    [Fact]
    public void SerializeAndDeserializePreservesNonVisualComponents()
    {
        var components = new[]
        {
            _componentRegistry.CreateComponent("FileAccess1", "file-access")
                .WithProperty("defaultName", "Notes.txt"),
            _componentRegistry.CreateComponent("FileMenuItem1", "menu-item")
                .WithProperty("caption", "Open")
                .WithEvent("OnClick", "OpenDocumentClick"),
            _componentRegistry.CreateComponent("SerialPort1", "serial-port"),
            _componentRegistry.CreateComponent("Transfer1", "protocol-transfer")
                .WithProperty("serialPortId", "SerialPort1")
                .WithProperty("protocol", "Zmodem")
                .WithEvent("OnComplete", "TransferComplete"),
        };
        var project = new ClassicMacProject(
            "Components",
            "mac-se-68k",
            "Component Form",
            [],
            components: components);

        var restored = ClassicMacProjectSerializer.Deserialize(
            ClassicMacProjectSerializer.Serialize(project),
            _registry,
            _componentRegistry);

        Assert.Equal(components.Length, restored.Components.Count);
        for (var index = 0; index < components.Length; index++)
        {
            AssertComponentsEqual(components[index], restored.Components[index]);
        }
    }

    [Fact]
    public void DeserializeDefaultsMissingComponentsForExistingProjects()
    {
        var restored = ClassicMacProjectSerializer.Deserialize(
            """
            {
              "schemaVersion": 1,
              "name": "Legacy",
              "targetProfileId": "mac-se-68k",
              "formTitle": "Legacy Window",
              "controls": []
            }
            """,
            _registry,
            _componentRegistry);

        Assert.Empty(restored.Components);
    }

    [Fact]
    public void DeserializeRejectsUnknownProtocolSerialComponent()
    {
        var project = new ClassicMacProject(
            "Components",
            "mac-se-68k",
            "Component Form",
            [],
            components:
            [
                _componentRegistry.CreateComponent("Transfer1", "protocol-transfer")
                    .WithProperty("serialPortId", "MissingPort"),
            ]);

        Assert.Throws<InvalidDataException>(() =>
            ClassicMacProjectSerializer.Deserialize(
                ClassicMacProjectSerializer.Serialize(project),
                _registry,
                _componentRegistry));
    }

    [Fact]
    public void DeserializeRejectsMultipleComponentsAssignedToOnePhysicalSerialPort()
    {
        var project = new ClassicMacProject(
            "Components",
            "mac-se-68k",
            "Component Form",
            [],
            components:
            [
                _componentRegistry.CreateComponent("ModemPort1", "serial-port"),
                _componentRegistry.CreateComponent("ModemPort2", "serial-port"),
            ]);

        Assert.Throws<InvalidDataException>(() =>
            ClassicMacProjectSerializer.Deserialize(
                ClassicMacProjectSerializer.Serialize(project),
                _registry,
                _componentRegistry));
    }

    [Fact]
    public void ProjectRejectsInvalidFormTypeNames()
    {
        Assert.Throws<ArgumentException>(() =>
            new ClassicMacProject("Sample", "mac-se-68k", "Window", [], "not a type"));
    }

    [Fact]
    public void ControlPreservesEventBindingsAcrossImmutableUpdates()
    {
        var control = _registry.CreateControl("button-run", "button", 8, 8)
            .WithEvent("OnClick", "RunClick");

        var updated = control.WithText("Execute")
            .WithGeometry(16, 24, 90, 28)
            .WithProperty("example", "value");

        Assert.Equal("RunClick", updated.Events["OnClick"]);
        Assert.Throws<ArgumentException>(() => control.WithEvent("OnClick", "not a name"));
        Assert.Empty(control.WithEvent("OnClick", null).Events);
    }

    [Fact]
    public void RegistryRejectsUnsupportedWidgetEvents()
    {
        var graphic = _registry.CreateControl("line-1", "graphic-line", 8, 8)
            .WithEvent("OnClick", "LineClick");

        Assert.Throws<ArgumentException>(() => _registry.ValidateControl(graphic));
    }

    [Fact]
    public void DeserializeRejectsUnknownTargetProfile()
    {
        var json = ClassicMacProjectSerializer.Serialize(
            new ClassicMacProject("Sample", "mac-se-68k", "Window", []))
            .Replace("mac-se-68k", "unknown-profile", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            ClassicMacProjectSerializer.Deserialize(json, _registry));
    }

    [Fact]
    public void DeserializeRejectsControlsOutsideTargetBounds()
    {
        var json = """
            {
              "schemaVersion": 1,
              "name": "Sample",
              "targetProfileId": "mac-se-68k",
              "formTitle": "Window",
              "controls": [
                {
                  "id": "button-run",
                  "widgetTypeId": "button",
                  "text": "Run",
                  "x": 400,
                  "y": 240,
                  "width": 80,
                  "height": 24,
                  "properties": {}
                }
              ]
            }
            """;

        Assert.Throws<InvalidDataException>(() =>
            ClassicMacProjectSerializer.Deserialize(json, _registry));
    }

    [Fact]
    public void DeserializeRejectsDuplicateControlIds()
    {
        var control = _registry.CreateControl("button-run", "button", 8, 8);
        var project = new ClassicMacProject(
            "Sample",
            "mac-se-68k",
            "Window",
            [control, control.WithGeometry(100, 8, control.Width, control.Height)]);

        Assert.Throws<InvalidDataException>(() =>
            ClassicMacProjectSerializer.Deserialize(
                ClassicMacProjectSerializer.Serialize(project),
                _registry));
    }

    private static void AssertControlsEqual(
        DesignerControl expected,
        DesignerControl actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.WidgetTypeId, actual.WidgetTypeId);
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(
            expected.Properties.OrderBy(item => item.Key),
            actual.Properties.OrderBy(item => item.Key));
        Assert.Equal(
            expected.Events.OrderBy(item => item.Key),
            actual.Events.OrderBy(item => item.Key));
    }

    private static void AssertComponentsEqual(
        DesignerNonVisualComponent expected,
        DesignerNonVisualComponent actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ComponentTypeId, actual.ComponentTypeId);
        Assert.Equal(
            expected.Properties.OrderBy(item => item.Key),
            actual.Properties.OrderBy(item => item.Key));
        Assert.Equal(
            expected.Events.OrderBy(item => item.Key),
            actual.Events.OrderBy(item => item.Key));
    }
}
