using System.Collections.ObjectModel;

namespace ClassicMacIde.Core;

/// <summary>
/// Defines a classic Macintosh widget instance and its logical form geometry.
/// </summary>
public sealed record DesignerControl
{
    /// <summary>
    /// Creates a push button using the original designer model signature.
    /// </summary>
    public DesignerControl(
        string id,
        string text,
        double x,
        double y,
        double width,
        double height)
        : this(id, "button", text, x, y, width, height)
    {
    }

    /// <summary>
    /// Creates a widget instance with its registered widget type and properties.
    /// </summary>
    public DesignerControl(
        string id,
        string widgetTypeId,
        string text,
        double x,
        double y,
        double width,
        double height,
        IReadOnlyDictionary<string, string>? properties = null,
        IReadOnlyDictionary<string, string>? events = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetTypeId);
        ArgumentNullException.ThrowIfNull(text);
        ValidateCoordinate(x, nameof(x), allowZero: true);
        ValidateCoordinate(y, nameof(y), allowZero: true);
        ValidateCoordinate(width, nameof(width), allowZero: false);
        ValidateCoordinate(height, nameof(height), allowZero: false);

        Id = id;
        WidgetTypeId = widgetTypeId;
        Text = text;
        X = x;
        Y = y;
        Width = width;
        Height = height;
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

    /// <summary>Gets the stable ID used by code generation and test telemetry.</summary>
    public string Id { get; }

    /// <summary>Gets the registry ID that selects this widget's behavior.</summary>
    public string WidgetTypeId { get; }

    /// <summary>Gets the text displayed by the widget.</summary>
    public string Text { get; }

    /// <summary>Gets the top-left X position in target-screen pixels.</summary>
    public double X { get; }

    /// <summary>Gets the top-left Y position in target-screen pixels.</summary>
    public double Y { get; }

    /// <summary>Gets the widget width in target-screen pixels.</summary>
    public double Width { get; }

    /// <summary>Gets the widget height in target-screen pixels.</summary>
    public double Height { get; }

    /// <summary>Gets the widget-specific properties as immutable key/value data.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>Gets event-to-handler bindings as immutable key/value data.</summary>
    public IReadOnlyDictionary<string, string> Events { get; }

    /// <summary>Returns a copy with the requested text.</summary>
    public DesignerControl WithText(string text) =>
        new(Id, WidgetTypeId, text, X, Y, Width, Height, Properties, Events);

    /// <summary>Returns a copy with updated target-pixel geometry.</summary>
    public DesignerControl WithGeometry(
        double x,
        double y,
        double width,
        double height) =>
        new(Id, WidgetTypeId, Text, x, y, width, height, Properties, Events);

    /// <summary>Returns a copy with one widget-specific property changed.</summary>
    public DesignerControl WithProperty(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        var properties = Properties.ToDictionary(
            property => property.Key,
            property => property.Value,
            StringComparer.Ordinal);
        properties[name] = value;
        return new DesignerControl(
            Id,
            WidgetTypeId,
            Text,
            X,
            Y,
            Width,
            Height,
            properties,
            Events);
    }

    /// <summary>Returns a copy with an event handler assigned or cleared.</summary>
    public DesignerControl WithEvent(string eventId, string? handlerName)
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

        return new DesignerControl(
            Id,
            WidgetTypeId,
            Text,
            X,
            Y,
            Width,
            Height,
            Properties,
            events);
    }

    /// <summary>Returns whether the widget fits entirely inside the target surface.</summary>
    public bool FitsWithin(double surfaceWidth, double surfaceHeight) =>
        double.IsFinite(surfaceWidth) &&
        double.IsFinite(surfaceHeight) &&
        surfaceWidth > 0 &&
        surfaceHeight > 0 &&
        X + Width <= surfaceWidth &&
        Y + Height <= surfaceHeight;

    private static void ValidateCoordinate(
        double value,
        string parameterName,
        bool allowZero)
    {
        if (!double.IsFinite(value) || (allowZero ? value < 0 : value <= 0))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
