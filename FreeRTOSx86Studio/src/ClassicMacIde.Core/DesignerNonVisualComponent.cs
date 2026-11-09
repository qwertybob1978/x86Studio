using System.Collections.ObjectModel;

namespace ClassicMacIde.Core;

/// <summary>Defines a project-owned component instance without form geometry.</summary>
public sealed record DesignerNonVisualComponent
{
    /// <summary>Creates a nonvisual component with immutable properties and event bindings.</summary>
    public DesignerNonVisualComponent(
        string id,
        string componentTypeId,
        IReadOnlyDictionary<string, string>? properties = null,
        IReadOnlyDictionary<string, string>? events = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentTypeId);
        if (!ClassicMacCIdentifier.IsValid(id))
        {
            throw new ArgumentException(
                "Component IDs must be valid C identifiers of at most 31 characters.",
                nameof(id));
        }

        Id = id;
        ComponentTypeId = componentTypeId;
        Properties = new ReadOnlyDictionary<string, string>(
            properties is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : properties.ToDictionary(
                    property => property.Key,
                    property => property.Value,
                    StringComparer.Ordinal));
        Events = new ReadOnlyDictionary<string, string>(
            events is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : events.ToDictionary(
                    eventBinding => eventBinding.Key,
                    eventBinding => eventBinding.Value,
                    StringComparer.Ordinal));
    }

    /// <summary>Gets the stable instance ID used by generated C APIs.</summary>
    public string Id { get; }

    /// <summary>Gets the registered component type ID.</summary>
    public string ComponentTypeId { get; }

    /// <summary>Gets immutable design-time properties.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>Gets immutable event-to-handler bindings.</summary>
    public IReadOnlyDictionary<string, string> Events { get; }

    /// <summary>Returns a copy with one property changed.</summary>
    public DesignerNonVisualComponent WithProperty(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        var properties = Properties.ToDictionary(
            property => property.Key,
            property => property.Value,
            StringComparer.Ordinal);
        properties[name] = value;
        return new DesignerNonVisualComponent(Id, ComponentTypeId, properties, Events);
    }

    /// <summary>Returns a copy with an event handler assigned or cleared.</summary>
    public DesignerNonVisualComponent WithEvent(string eventId, string? handlerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var events = Events.ToDictionary(
            eventBinding => eventBinding.Key,
            eventBinding => eventBinding.Value,
            StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(handlerName))
        {
            events.Remove(eventId);
        }
        else
        {
            if (!ClassicMacCIdentifier.IsValid(handlerName))
            {
                throw new ArgumentException(
                    "Event handler names must be valid C identifiers of at most 31 characters.",
                    nameof(handlerName));
            }

            events[eventId] = handlerName;
        }

        return new DesignerNonVisualComponent(Id, ComponentTypeId, Properties, events);
    }
}
