using System.Globalization;

namespace ClassicMacIde.Core;

/// <summary>Looks up, creates, and validates nonvisual component instances.</summary>
public sealed class DesignerNonVisualComponentRegistry
{
    private readonly Dictionary<string, DesignerNonVisualComponentDefinition> _definitions =
        new(StringComparer.Ordinal);

    /// <summary>Creates a registry populated with the supplied component definitions.</summary>
    public DesignerNonVisualComponentRegistry(
        IEnumerable<DesignerNonVisualComponentDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var definition in definitions)
        {
            Register(definition);
        }
    }

    /// <summary>Gets registered component definitions in registration order.</summary>
    public IReadOnlyList<DesignerNonVisualComponentDefinition> Definitions =>
        Array.AsReadOnly(_definitions.Values.ToArray());

    /// <summary>Adds a component definition, rejecting duplicate type IDs.</summary>
    public void Register(DesignerNonVisualComponentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.Id, definition))
        {
            throw new ArgumentException(
                $"Component type ID '{definition.Id}' is already registered.",
                nameof(definition));
        }
    }

    /// <summary>Gets a registered component definition or throws for an unknown type.</summary>
    public DesignerNonVisualComponentDefinition GetRequired(string componentTypeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentTypeId);
        return _definitions.TryGetValue(componentTypeId, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Component type ID '{componentTypeId}' is not registered.");
    }

    /// <summary>Creates an instance populated with its definition's property defaults.</summary>
    public DesignerNonVisualComponent CreateComponent(string id, string componentTypeId)
    {
        var definition = GetRequired(componentTypeId);
        return new DesignerNonVisualComponent(
            id,
            componentTypeId,
            definition.Properties.ToDictionary(
                property => property.Id,
                property => property.DefaultValue,
                StringComparer.Ordinal));
    }

    /// <summary>Validates component properties and event bindings against its definition.</summary>
    public void ValidateComponent(DesignerNonVisualComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var definition = GetRequired(component.ComponentTypeId);
        var properties = definition.Properties.ToDictionary(
            property => property.Id,
            StringComparer.Ordinal);
        foreach (var (propertyId, value) in component.Properties)
        {
            if (!properties.TryGetValue(propertyId, out var property))
            {
                throw new ArgumentException(
                    $"Component '{component.Id}' has unknown property '{propertyId}'.",
                    nameof(component));
            }

            var isValid = property.EditorKind switch
            {
                DesignerWidgetPropertyEditorKind.Boolean => bool.TryParse(value, out _),
                DesignerWidgetPropertyEditorKind.Integer =>
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue) &&
                    (property.MinimumValue is null || integerValue >= property.MinimumValue.Value) &&
                    (property.MaximumValue is null || integerValue <= property.MaximumValue.Value),
                _ => property.Options is null ||
                    property.Options.Contains(value, StringComparer.Ordinal),
            };
            if (!isValid)
            {
                throw new ArgumentException(
                    $"Component '{component.Id}' property '{propertyId}' has an invalid value.",
                    nameof(component));
            }

            if (value.Length > byte.MaxValue ||
                value.Any(character => character < 0x20 || character > 0x7e))
            {
                throw new ArgumentException(
                    $"Component '{component.Id}' property '{propertyId}' must contain at most 255 printable ASCII characters.",
                    nameof(component));
            }
            if (component.ComponentTypeId == "menu-item" &&
                propertyId == "shortcut" &&
                (value.Length > 1 ||
                 value.Length == 1 && !char.IsAsciiLetterOrDigit(value[0])))
            {
                throw new ArgumentException(
                    $"Menu component '{component.Id}' shortcut must be empty or one ASCII letter or digit.",
                    nameof(component));
            }
        }

        var supportedEvents = definition.Events.ToDictionary(
            eventDefinition => eventDefinition.Id,
            StringComparer.Ordinal);
        foreach (var (eventId, handlerName) in component.Events)
        {
            if (!supportedEvents.ContainsKey(eventId))
            {
                throw new ArgumentException(
                    $"Component '{component.Id}' does not support event '{eventId}'.",
                    nameof(component));
            }
            if (!ClassicMacCIdentifier.IsValid(handlerName))
            {
                throw new ArgumentException(
                    $"Component '{component.Id}' event '{eventId}' has an invalid handler name.",
                    nameof(component));
            }
        }

        if (component.ComponentTypeId == "file-access")
        {
            var fileType = component.Properties.GetValueOrDefault("fileType") ?? "TEXT";
            if (fileType.Length != 4 ||
                fileType.Any(character => !char.IsAsciiLetterOrDigit(character)))
            {
                throw new ArgumentException(
                    $"File component '{component.Id}' file type must be four ASCII letters or digits.",
                    nameof(component));
            }
        }
        else if (component.ComponentTypeId == "menu-item" &&
                 (string.IsNullOrWhiteSpace(
                      component.Properties.GetValueOrDefault("menuTitle")) ||
                  string.IsNullOrWhiteSpace(
                      component.Properties.GetValueOrDefault("caption"))))
        {
            throw new ArgumentException(
                $"Menu component '{component.Id}' requires a menu title and caption.",
                nameof(component));
        }
    }
}
