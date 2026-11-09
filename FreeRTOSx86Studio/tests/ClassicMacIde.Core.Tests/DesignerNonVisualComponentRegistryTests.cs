using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class DesignerNonVisualComponentRegistryTests
{
    [Fact]
    public void BuiltInCatalogProvidesFileMenuSerialAndTransferComponents()
    {
        var registry = ClassicMacNonVisualComponentCatalog.CreateRegistry();

        Assert.Contains(registry.Definitions, definition => definition.Id == "file-access");
        Assert.Contains(registry.Definitions, definition => definition.Id == "menu-item");
        Assert.Contains(registry.Definitions, definition => definition.Id == "serial-port");
        Assert.Contains(registry.Definitions, definition => definition.Id == "protocol-transfer");
        var protocolProperty = registry.GetRequired("protocol-transfer").Properties
            .Single(property => property.Id == "protocol");

        Assert.NotNull(protocolProperty.Options);
        Assert.Contains("XmodemCRC", protocolProperty.Options);
        Assert.Contains("Zmodem", protocolProperty.Options);
    }

    [Fact]
    public void CreateComponentUsesDefinitionDefaultsAndImmutableUpdates()
    {
        var registry = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var component = registry.CreateComponent("MenuItem1", "menu-item")
            .WithProperty("caption", "Open")
            .WithEvent("OnClick", "OpenClick");

        Assert.Equal("File", component.Properties["menuTitle"]);
        Assert.Equal("Open", component.Properties["caption"]);
        Assert.Equal("OpenClick", component.Events["OnClick"]);
        Assert.Equal("New Item", registry.CreateComponent("MenuItem2", "menu-item")
            .Properties["caption"]);
    }

    [Fact]
    public void ValidateComponentRejectsUnknownPropertiesAndInvalidChoices()
    {
        var registry = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var serialPort = registry.CreateComponent("SerialPort1", "serial-port");

        Assert.Throws<ArgumentException>(() =>
            registry.ValidateComponent(serialPort.WithProperty("baud", "12345")));
        Assert.Throws<ArgumentException>(() =>
            registry.ValidateComponent(serialPort.WithProperty("unknown", "value")));
    }

    [Fact]
    public void ValidateComponentRejectsUnsupportedEventsAndInvalidHandlers()
    {
        var registry = ClassicMacNonVisualComponentCatalog.CreateRegistry();
        var menuItem = registry.CreateComponent("MenuItem1", "menu-item");

        Assert.Throws<ArgumentException>(() =>
            registry.ValidateComponent(menuItem.WithEvent("OnDataAvailable", "Received")));
        Assert.Throws<ArgumentException>(() =>
            registry.ValidateComponent(menuItem.WithEvent("OnClick", "not a C name")));
    }

    [Fact]
    public void RegistryRejectsDuplicateComponentDefinitions()
    {
        var definition = CreateDefinition("custom");

        Assert.Throws<ArgumentException>(() =>
            new DesignerNonVisualComponentRegistry([definition, definition]));
    }

    private static DesignerNonVisualComponentDefinition CreateDefinition(string id) =>
        new(
            id,
            "Custom Component",
            DesignerNonVisualComponentCategory.File,
            [],
            [],
            new TestComponentGenerator(id));

    private sealed class TestComponentGenerator(string componentTypeId) :
        MacNonVisualComponentCodeGenerator
    {
        public override string ComponentTypeId { get; } = componentTypeId;
    }
}
