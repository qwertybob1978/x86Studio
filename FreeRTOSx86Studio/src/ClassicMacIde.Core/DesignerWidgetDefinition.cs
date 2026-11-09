using System.Text;

namespace ClassicMacIde.Core;

/// <summary>Identifies the toolbox category that contains a designer item.</summary>
public enum DesignerWidgetCategory
{
    Controls,
    Graphics,
}

/// <summary>Identifies the editor used for a widget property.</summary>
public enum DesignerWidgetPropertyEditorKind
{
    Text,
    MultiLineText,
    Boolean,
    Integer,
    Color,
}

/// <summary>Describes a property that can be edited in the designer.</summary>
/// <param name="MinimumValue">Optional inclusive minimum for integer properties.</param>
/// <param name="MaximumValue">Optional inclusive maximum for integer properties.</param>
public sealed record DesignerWidgetPropertyDefinition(
    string Id,
    string DisplayName,
    DesignerWidgetPropertyEditorKind EditorKind,
    string DefaultValue,
    int? MinimumValue = null,
    int? MaximumValue = null);

/// <summary>Describes a widget event that can be bound to a user-code handler.</summary>
public sealed record DesignerWidgetEventDefinition(string Id, string DisplayName);

/// <summary>
/// Supplies target-specific resource and guest-code generation for one widget type.
/// </summary>
public interface IMacWidgetCodeGenerator
{
    /// <summary>Gets the widget type ID handled by this generator.</summary>
    string WidgetTypeId { get; }

    /// <summary>Appends this widget's Dialog Manager item to the Rez source.</summary>
    void AppendResourceItem(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends additional Rez resources required by this widget.</summary>
    void AppendAdditionalResources(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends guest declarations required by this widget.</summary>
    void AppendDeclarations(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends guest initialization after the dialog is created.</summary>
    void AppendInitialization(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends a simulated interaction used by Build &amp; Test.</summary>
    void AppendTestInteraction(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends event-loop handling for this widget type.</summary>
    void AppendEventHandling(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends the body of this widget's generated value getter case.</summary>
    void AppendControlValueGetter(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends the body of this widget's generated value setter case.</summary>
    void AppendControlValueSetter(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <summary>Appends widget-specific runtime work after its bounds are changed.</summary>
    void AppendControlBoundsSetter(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <summary>Appends guest cleanup before the dialog is disposed.</summary>
    void AppendCleanup(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);
}

/// <summary>
/// Provides a default no-op implementation for optional widget code-generation hooks.
/// </summary>
public abstract class MacWidgetCodeGenerator : IMacWidgetCodeGenerator
{
    /// <inheritdoc />
    public abstract string WidgetTypeId { get; }

    /// <inheritdoc />
    public abstract void AppendResourceItem(
        StringBuilder source,
        MacWidgetCodeGenerationContext context);

    /// <inheritdoc />
    public virtual void AppendAdditionalResources(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendDeclarations(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendInitialization(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendTestInteraction(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendEventHandling(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendControlValueGetter(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendControlValueSetter(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendControlBoundsSetter(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendCleanup(
        StringBuilder source,
        MacWidgetCodeGenerationContext context)
    {
    }
}

/// <summary>Provides resolved geometry and model data to a widget code generator.</summary>
public sealed record MacWidgetCodeGenerationContext(
    MacTargetProfile Profile,
    DesignerControl Control,
    int ItemNumber,
    int X,
    int Y,
    int Width,
    int Height,
    string EscapedText,
    bool IsLastItem);

/// <summary>
/// Registers the toolbox metadata and code-generation strategy for a widget type.
/// </summary>
public sealed record DesignerWidgetDefinition
{
    /// <summary>Creates a widget definition with validated defaults and properties.</summary>
    public DesignerWidgetDefinition(
        string id,
        string displayName,
        string previewRendererId,
        double defaultWidth,
        double defaultHeight,
        double minimumWidth,
        double minimumHeight,
        string defaultText,
        bool isInteractive,
        IReadOnlyList<DesignerWidgetPropertyDefinition> properties,
        IMacWidgetCodeGenerator codeGenerator,
        DesignerWidgetCategory category = DesignerWidgetCategory.Controls,
        bool supportsTextAccess = false,
        bool supportsValueAccess = false,
        IReadOnlyList<DesignerWidgetEventDefinition>? events = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewRendererId);
        ArgumentNullException.ThrowIfNull(defaultText);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(codeGenerator);
        if (events is not null)
        {
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var eventDefinition in events)
            {
                ArgumentNullException.ThrowIfNull(eventDefinition);
                ArgumentException.ThrowIfNullOrWhiteSpace(eventDefinition.Id);
                ArgumentException.ThrowIfNullOrWhiteSpace(eventDefinition.DisplayName);
                if (!eventIds.Add(eventDefinition.Id))
                {
                    throw new ArgumentException(
                        $"Widget event ID '{eventDefinition.Id}' is duplicated.",
                        nameof(events));
                }
            }
        }
        if (!double.IsFinite(defaultWidth) || !double.IsFinite(defaultHeight) ||
            !double.IsFinite(minimumWidth) || !double.IsFinite(minimumHeight) ||
            defaultWidth < minimumWidth || defaultHeight < minimumHeight ||
            minimumWidth <= 0 || minimumHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultWidth),
                "Widget default geometry must be finite and at least its positive minimum size.");
        }
        if (!string.Equals(codeGenerator.WidgetTypeId, id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The code generator widget type ID must match the definition ID.",
                nameof(codeGenerator));
        }
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        var propertyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            ArgumentNullException.ThrowIfNull(property);
            ArgumentException.ThrowIfNullOrWhiteSpace(property.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(property.DisplayName);
            ArgumentNullException.ThrowIfNull(property.DefaultValue);
            if (!propertyIds.Add(property.Id))
            {
                throw new ArgumentException(
                    $"Widget property ID '{property.Id}' is duplicated.",
                    nameof(properties));
            }
            if (property.MinimumValue is int minimum &&
                property.MaximumValue is int maximum &&
                minimum > maximum)
            {
                throw new ArgumentException(
                    $"Widget property '{property.Id}' has an invalid integer range.",
                    nameof(properties));
            }
            if ((property.MinimumValue is not null || property.MaximumValue is not null) &&
                property.EditorKind != DesignerWidgetPropertyEditorKind.Integer)
            {
                throw new ArgumentException(
                    $"Widget property '{property.Id}' can only constrain integer values.",
                    nameof(properties));
            }
        }

        Id = id;
        DisplayName = displayName;
        PreviewRendererId = previewRendererId;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
        MinimumWidth = minimumWidth;
        MinimumHeight = minimumHeight;
        DefaultText = defaultText;
        IsInteractive = isInteractive;
        Properties = Array.AsReadOnly(properties.ToArray());
        CodeGenerator = codeGenerator;
        Category = category;
        SupportsTextAccess = supportsTextAccess;
        SupportsValueAccess = supportsValueAccess;
        var supportedEvents = events ?? (isInteractive
            ? new[] { new DesignerWidgetEventDefinition("OnClick", "OnClick") }
            : Array.Empty<DesignerWidgetEventDefinition>());
        Events = Array.AsReadOnly(supportedEvents.ToArray());
    }

    /// <summary>Gets the stable, extensible widget type ID.</summary>
    public string Id { get; }

    /// <summary>Gets the name displayed in the widget toolbox.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the ID used to locate the WPF preview renderer.</summary>
    public string PreviewRendererId { get; }

    /// <summary>Gets the default width in target-screen pixels.</summary>
    public double DefaultWidth { get; }

    /// <summary>Gets the default height in target-screen pixels.</summary>
    public double DefaultHeight { get; }

    /// <summary>Gets the minimum permitted width in target-screen pixels.</summary>
    public double MinimumWidth { get; }

    /// <summary>Gets the minimum permitted height in target-screen pixels.</summary>
    public double MinimumHeight { get; }

    /// <summary>Gets the default display text.</summary>
    public string DefaultText { get; }

    /// <summary>Gets whether Build &amp; Test should synthesize a click for this widget.</summary>
    public bool IsInteractive { get; }

    /// <summary>Gets the toolbox category for this widget definition.</summary>
    public DesignerWidgetCategory Category { get; }

    /// <summary>Gets whether generated code can read or write this widget's text.</summary>
    public bool SupportsTextAccess { get; }

    /// <summary>Gets whether generated code can read or write this widget's value.</summary>
    public bool SupportsValueAccess { get; }

    /// <summary>Gets the events exposed by this widget type.</summary>
    public IReadOnlyList<DesignerWidgetEventDefinition> Events { get; }

    /// <summary>Gets the widget-specific property schema.</summary>
    public IReadOnlyList<DesignerWidgetPropertyDefinition> Properties { get; }

    /// <summary>Gets the target-specific Rez and guest-code generator.</summary>
    public IMacWidgetCodeGenerator CodeGenerator { get; }
}
