using System.Text;

namespace ClassicMacIde.Core;

/// <summary>Groups nonvisual components in the component palette.</summary>
public enum DesignerNonVisualComponentCategory
{
    File,
    Menu,
    Communications,
}

/// <summary>Describes a nonvisual component property and its editor constraints.</summary>
public sealed record DesignerNonVisualComponentPropertyDefinition(
    string Id,
    string DisplayName,
    DesignerWidgetPropertyEditorKind EditorKind,
    string DefaultValue,
    IReadOnlyList<string>? Options = null,
    int? MinimumValue = null,
    int? MaximumValue = null);

/// <summary>Supplies generated source for one nonvisual component type.</summary>
public interface IMacNonVisualComponentCodeGenerator
{
    /// <summary>Gets the component type ID handled by this strategy.</summary>
    string ComponentTypeId { get; }

    /// <summary>Appends this component's declarations to the generated header.</summary>
    void AppendHeaderDeclarations(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context);

    /// <summary>Appends component-specific guest code to the generated source.</summary>
    void AppendSource(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context);

    /// <summary>Appends component initialization after the form is created.</summary>
    void AppendInitialization(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context);

    /// <summary>Appends component polling or event-loop handling.</summary>
    void AppendEventLoopHandling(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context);

    /// <summary>Appends resources required by this component.</summary>
    void AppendResources(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context);

    /// <summary>Appends component cleanup before the form is disposed.</summary>
    void AppendCleanup(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context);
}

/// <summary>Provides no-op defaults for optional component generator hooks.</summary>
public abstract class MacNonVisualComponentCodeGenerator :
    IMacNonVisualComponentCodeGenerator
{
    /// <inheritdoc />
    public abstract string ComponentTypeId { get; }

    /// <inheritdoc />
    public virtual void AppendHeaderDeclarations(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendSource(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendInitialization(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendEventLoopHandling(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendResources(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context)
    {
    }

    /// <inheritdoc />
    public virtual void AppendCleanup(
        StringBuilder source,
        MacNonVisualComponentCodeGenerationContext context)
    {
    }
}

/// <summary>Provides resolved project context to a component generator.</summary>
public sealed record MacNonVisualComponentCodeGenerationContext(
    MacTargetProfile Profile,
    DesignerNonVisualComponent Component,
    int ComponentIndex,
    string FormTypeName,
    IReadOnlyList<DesignerNonVisualComponent> Components);

/// <summary>Registers the metadata and target generator for one nonvisual type.</summary>
public sealed record DesignerNonVisualComponentDefinition
{
    /// <summary>Creates a validated nonvisual component definition.</summary>
    public DesignerNonVisualComponentDefinition(
        string id,
        string displayName,
        DesignerNonVisualComponentCategory category,
        IReadOnlyList<DesignerNonVisualComponentPropertyDefinition> properties,
        IReadOnlyList<DesignerWidgetEventDefinition> events,
        IMacNonVisualComponentCodeGenerator codeGenerator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(codeGenerator);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }
        if (!string.Equals(codeGenerator.ComponentTypeId, id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The code generator component type ID must match the definition ID.",
                nameof(codeGenerator));
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
                    $"Component property ID '{property.Id}' is duplicated.",
                    nameof(properties));
            }
            if (property.Options is not null &&
                (property.EditorKind != DesignerWidgetPropertyEditorKind.Text ||
                 property.Options.Count == 0 ||
                 property.Options.Any(string.IsNullOrWhiteSpace) ||
                 property.Options.Distinct(StringComparer.Ordinal).Count() != property.Options.Count ||
                 !property.Options.Contains(property.DefaultValue, StringComparer.Ordinal)))
            {
                throw new ArgumentException(
                    $"Component property '{property.Id}' has invalid choice options.",
                    nameof(properties));
            }
            if (property.MinimumValue is int minimum &&
                property.MaximumValue is int maximum &&
                minimum > maximum)
            {
                throw new ArgumentException(
                    $"Component property '{property.Id}' has an invalid integer range.",
                    nameof(properties));
            }
            if ((property.MinimumValue is not null || property.MaximumValue is not null) &&
                property.EditorKind != DesignerWidgetPropertyEditorKind.Integer)
            {
                throw new ArgumentException(
                    $"Component property '{property.Id}' can only constrain integer values.",
                    nameof(properties));
            }
        }

        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventDefinition in events)
        {
            ArgumentNullException.ThrowIfNull(eventDefinition);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventDefinition.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(eventDefinition.DisplayName);
            if (!eventIds.Add(eventDefinition.Id))
            {
                throw new ArgumentException(
                    $"Component event ID '{eventDefinition.Id}' is duplicated.",
                    nameof(events));
            }
        }

        Id = id;
        DisplayName = displayName;
        Category = category;
        Properties = Array.AsReadOnly(properties.ToArray());
        Events = Array.AsReadOnly(events.ToArray());
        CodeGenerator = codeGenerator;
    }

    /// <summary>Gets the stable component type ID.</summary>
    public string Id { get; }

    /// <summary>Gets the palette display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the palette category.</summary>
    public DesignerNonVisualComponentCategory Category { get; }

    /// <summary>Gets the editable property schema.</summary>
    public IReadOnlyList<DesignerNonVisualComponentPropertyDefinition> Properties { get; }

    /// <summary>Gets the events exposed by this component type.</summary>
    public IReadOnlyList<DesignerWidgetEventDefinition> Events { get; }

    /// <summary>Gets the target-specific source generator.</summary>
    public IMacNonVisualComponentCodeGenerator CodeGenerator { get; }
}
