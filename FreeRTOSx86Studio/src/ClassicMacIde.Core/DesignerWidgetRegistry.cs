using System.Globalization;

namespace ClassicMacIde.Core;

/// <summary>Looks up and validates widget definitions available to the designer.</summary>
public sealed class DesignerWidgetRegistry
{
    private readonly Dictionary<string, DesignerWidgetDefinition> _definitions =
        new(StringComparer.Ordinal);

    /// <summary>Creates a registry populated with the supplied widget definitions.</summary>
    public DesignerWidgetRegistry(IEnumerable<DesignerWidgetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var definition in definitions)
        {
            Register(definition);
        }
    }

    /// <summary>Gets registered widget definitions in registration order.</summary>
    public IReadOnlyList<DesignerWidgetDefinition> Definitions =>
        Array.AsReadOnly(_definitions.Values.ToArray());

    /// <summary>Adds a widget definition, rejecting duplicate type IDs.</summary>
    public void Register(DesignerWidgetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.Id, definition))
        {
            throw new ArgumentException(
                $"Widget type ID '{definition.Id}' is already registered.",
                nameof(definition));
        }
    }

    /// <summary>Gets a registered widget definition or throws for an unknown type.</summary>
    public DesignerWidgetDefinition GetRequired(string widgetTypeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetTypeId);
        return _definitions.TryGetValue(widgetTypeId, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Widget type ID '{widgetTypeId}' is not registered.");
    }

    /// <summary>Creates a widget using the definition's geometry and property defaults.</summary>
    public DesignerControl CreateControl(
        string id,
        string widgetTypeId,
        double x,
        double y,
        string? text = null)
    {
        var definition = GetRequired(widgetTypeId);
        var properties = definition.Properties.ToDictionary(
            property => property.Id,
            property => property.DefaultValue,
            StringComparer.Ordinal);
        return new DesignerControl(
            id,
            widgetTypeId,
            text ?? definition.DefaultText,
            x,
            y,
            definition.DefaultWidth,
            definition.DefaultHeight,
            properties);
    }

    /// <summary>Validates type, minimum size, and values against the widget schema.</summary>
    public void ValidateControl(DesignerControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var definition = GetRequired(control.WidgetTypeId);
        var events = definition.Events.ToDictionary(
            eventDefinition => eventDefinition.Id,
            StringComparer.Ordinal);
        foreach (var (eventId, handlerName) in control.Events)
        {
            if (!events.ContainsKey(eventId))
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' does not support event '{eventId}'.",
                    nameof(control));
            }
            if (!ClassicMacCIdentifier.IsValid(handlerName))
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' event '{eventId}' has an invalid handler name.",
                    nameof(control));
            }
        }

        if (control.Width < definition.MinimumWidth ||
            control.Height < definition.MinimumHeight)
        {
            throw new ArgumentException(
                $"Widget '{control.Id}' is smaller than the minimum " +
                $"{definition.MinimumWidth} x {definition.MinimumHeight} pixels.",
                nameof(control));
        }

        var properties = definition.Properties.ToDictionary(
            property => property.Id,
            StringComparer.Ordinal);
        foreach (var (propertyId, value) in control.Properties)
        {
            if (!properties.TryGetValue(propertyId, out var property))
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' has unknown property '{propertyId}'.",
                    nameof(control));
            }

            var isValid = property.EditorKind switch
            {
                DesignerWidgetPropertyEditorKind.Boolean =>
                    bool.TryParse(value, out _),
                DesignerWidgetPropertyEditorKind.Integer =>
                    int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var integerValue) &&
                    (property.MinimumValue is null ||
                     integerValue >= property.MinimumValue.Value) &&
                    (property.MaximumValue is null ||
                     integerValue <= property.MaximumValue.Value),
                DesignerWidgetPropertyEditorKind.Color =>
                    ClassicMacRgbColor.TryParse(value, out _),
                _ => true,
            };
            if (!isValid)
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' property '{propertyId}' has an invalid value.",
                    nameof(control));
            }

            var textValues = property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText
                ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                : [value];
            if (textValues.Any(text =>
                    text.Length > byte.MaxValue ||
                    text.Any(character => character < 0x20 || character > 0x7e)))
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' property '{propertyId}' must contain at most 255 printable ASCII characters per item.",
                    nameof(control));
            }
        }

        if (definition.Id is "popup-menu" or "list-box")
        {
            var items = control.Properties.GetValueOrDefault("items")?
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];
            var selectedIndex = int.Parse(
                control.Properties.GetValueOrDefault("selectedIndex") ??
                    definition.Properties.Single(property => property.Id == "selectedIndex").DefaultValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
            var minimumIndex = definition.Id == "list-box" ? -1 : 0;
            if (selectedIndex < minimumIndex || selectedIndex >= items.Length)
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' selected index must be between {minimumIndex} and {items.Length - 1}.",
                    nameof(control));
            }
        }
        else if (definition.Id == "scrollbar")
        {
            var minimum = ParseProperty(control, "minimum", 0);
            var maximum = ParseProperty(control, "maximum", 100);
            var value = ParseProperty(control, "value", minimum);
            if (minimum > maximum || value < minimum || value > maximum)
            {
                throw new ArgumentException(
                    $"Widget '{control.Id}' requires minimum <= value <= maximum.",
                    nameof(control));
            }
        }
    }

    private static int ParseProperty(
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
