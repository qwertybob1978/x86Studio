using System.Text;
using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class DesignerWidgetRegistryTests
{
    [Fact]
    public void ClassicCatalogIncludesTheSystemSevenWidgetSet()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();

        Assert.Equal(
            [
                "static-text",
                "editable-text",
                "button",
                "checkbox",
                "radio-button",
                "popup-menu",
                "list-box",
                "progress-bar",
                "scrollbar",
            ],
            registry.Definitions
                .Where(definition => definition.Category == DesignerWidgetCategory.Controls)
                .Select(definition => definition.Id));
    }

    [Fact]
    public void ClassicCatalogIncludesQuickDrawGraphicsInTheirOwnCategory()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();

        Assert.Equal(
            [
                "graphic-line",
                "graphic-rectangle-outline",
                "graphic-rectangle-filled",
                "graphic-rectangle-dithered",
                "graphic-circle-outline",
                "graphic-circle-filled",
                "graphic-circle-dithered",
                "graphic-arc-outline",
                "graphic-arc-filled",
                "graphic-arc-dithered",
            ],
            registry.Definitions
                .Where(definition => definition.Category == DesignerWidgetCategory.Graphics)
                .Select(definition => definition.Id));
    }

    [Fact]
    public void CreatesControlUsingWidgetDefaults()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();

        var control = registry.CreateControl("check-1", "checkbox", 4, 8);

        Assert.Equal("Check Box", control.Text);
        Assert.Equal((110, 18), (control.Width, control.Height));
        Assert.Equal("false", control.Properties["checked"]);
    }

    [Fact]
    public void RejectsGeometryBelowTheWidgetMinimum()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var control = new DesignerControl(
            "button-1",
            "button",
            "OK",
            0,
            0,
            47,
            20);

        Assert.Throws<ArgumentException>(() => registry.ValidateControl(control));
    }

    [Fact]
    public void RejectsUnknownAndInvalidWidgetProperties()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var unknown = registry.CreateControl("button-1", "button", 0, 0)
            .WithProperty("unknown", "value");
        var invalid = registry.CreateControl("check-1", "checkbox", 0, 0)
            .WithProperty("checked", "maybe");

        Assert.Throws<ArgumentException>(() => registry.ValidateControl(unknown));
        Assert.Throws<ArgumentException>(() => registry.ValidateControl(invalid));
    }

    [Fact]
    public void ValidatesListAndScrollbarRanges()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var invalidList = registry.CreateControl("list-1", "list-box", 0, 0)
            .WithProperty("selectedIndex", "5");
        var invalidScroll = registry.CreateControl("scroll-1", "scrollbar", 0, 0)
            .WithProperty("maximum", "5")
            .WithProperty("value", "6");
        var invalidProgress = registry.CreateControl("progress-1", "progress-bar", 0, 0)
            .WithProperty("value", "101");

        Assert.Throws<ArgumentException>(() => registry.ValidateControl(invalidList));
        Assert.Throws<ArgumentException>(() => registry.ValidateControl(invalidScroll));
        Assert.Throws<ArgumentException>(() => registry.ValidateControl(invalidProgress));
    }

    [Fact]
    public void RejectsClassicWidgetPropertyItemsLongerThanAResourceString()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var control = registry.CreateControl("list-1", "list-box", 0, 0)
            .WithProperty("items", new string('x', 256))
            .WithProperty("selectedIndex", "0");

        Assert.Throws<ArgumentException>(() => registry.ValidateControl(control));
    }

    [Fact]
    public void RegistryCanBeExtendedAndGeneratorsReceiveLifecycleHooks()
    {
        var generator = new TestWidgetGenerator();
        var definition = new DesignerWidgetDefinition(
            "custom",
            "Custom",
            "custom",
            40,
            20,
            20,
            12,
            "Custom",
            true,
            [],
            generator);
        var registry = new DesignerWidgetRegistry([definition]);

        var generated = MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "CustomApp",
            "Custom Form",
            [registry.CreateControl("custom-1", "custom", 0, 0)],
            registry);

        Assert.Contains("CustomItem", generated.ResourceSource);
        Assert.Contains("CustomInitialize()", generated.CSource);
        Assert.Contains("CustomTest()", generated.CSource);
        Assert.Contains("CustomCleanup()", generated.CSource);
    }

    [Fact]
    public void RejectsDuplicateWidgetTypeIds()
    {
        var definition = new DesignerWidgetDefinition(
            "custom",
            "Custom",
            "custom",
            40,
            20,
            20,
            12,
            "Custom",
            true,
            [],
            new TestWidgetGenerator());
        var registry = new DesignerWidgetRegistry([definition]);

        Assert.Throws<ArgumentException>(() => registry.Register(definition));
    }

    [Fact]
    public void GeneratorRejectsMultipleInitiallySelectedRadioButtonsInOneGroup()
    {
        var registry = ClassicMacWidgetCatalog.CreateRegistry();
        var first = registry.CreateControl("radio-1", "radio-button", 0, 0)
            .WithProperty("selected", "true");
        var second = registry.CreateControl("radio-2", "radio-button", 0, 24)
            .WithProperty("selected", "true");

        Assert.Throws<ArgumentException>(() => MacFormCodeGenerator.Generate(
            MacTargetProfiles.All[0],
            "RadioApp",
            "Radio Form",
            [first, second],
            registry));
    }

    private sealed class TestWidgetGenerator : MacWidgetCodeGenerator
    {
        public override string WidgetTypeId => "custom";

        public override void AppendResourceItem(
            StringBuilder source,
            MacWidgetCodeGenerationContext context) =>
            source.AppendLine(
                $"        {{ {context.Y}, {context.X}, {context.Y + context.Height}, {context.X + context.Width} }}, CustomItem {{ enabled }}");

        public override void AppendInitialization(
            StringBuilder source,
            MacWidgetCodeGenerationContext context) =>
            source.AppendLine("    CustomInitialize();");

        public override void AppendTestInteraction(
            StringBuilder source,
            MacWidgetCodeGenerationContext context) =>
            source.AppendLine("                    CustomTest();");

        public override void AppendCleanup(
            StringBuilder source,
            MacWidgetCodeGenerationContext context) =>
            source.AppendLine("    CustomCleanup();");
    }
}
