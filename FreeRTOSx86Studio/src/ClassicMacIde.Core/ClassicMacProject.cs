using System.Text.Json;

namespace ClassicMacIde.Core;

/// <summary>Defines the persisted, target-aware contents of a designer project.</summary>
public sealed record ClassicMacProject
{
    /// <summary>Creates a project snapshot with stable widget IDs and properties.</summary>
    public ClassicMacProject(
        string name,
        string targetProfileId,
        string formTitle,
        IEnumerable<DesignerControl> controls,
        string formTypeName = "MainForm",
        IEnumerable<DesignerNonVisualComponent>? components = null,
        MacFormProperties? formProperties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(formTitle);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentException.ThrowIfNullOrWhiteSpace(formTypeName);
        if (!ClassicMacCIdentifier.IsValid(formTypeName))
        {
            throw new ArgumentException(
                "Form type name must be a C identifier with at most 31 letters, digits, or underscores.",
                nameof(formTypeName));
        }

        Name = name;
        TargetProfileId = targetProfileId;
        FormTitle = formTitle;
        FormTypeName = formTypeName;
        FormProperties = formProperties ?? new MacFormProperties();
        Controls = Array.AsReadOnly(controls.ToArray());
        Components = Array.AsReadOnly(
            (components ?? Array.Empty<DesignerNonVisualComponent>()).ToArray());
    }

    /// <summary>Gets the display name of the project.</summary>
    public string Name { get; }

    /// <summary>Gets the selected target profile ID.</summary>
    public string TargetProfileId { get; }

    /// <summary>Gets the form's title-bar text.</summary>
    public string FormTitle { get; }

    /// <summary>Gets the C type name used for the main form handle.</summary>
    public string FormTypeName { get; }

    /// <summary>Gets the classic Macintosh form window attributes.</summary>
    public MacFormProperties FormProperties { get; }

    /// <summary>Gets the project controls in designer order.</summary>
    public IReadOnlyList<DesignerControl> Controls { get; }

    /// <summary>Gets the project's nonvisual components in tray order.</summary>
    public IReadOnlyList<DesignerNonVisualComponent> Components { get; }
}

/// <summary>Reads and writes versioned JSON project definition files.</summary>
public static class ClassicMacProjectSerializer
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Serializes a project as a readable, versioned JSON document.</summary>
    public static string Serialize(ClassicMacProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return JsonSerializer.Serialize(
            ProjectFileDocument.FromProject(project),
            SerializerOptions);
    }

    /// <summary>
    /// Deserializes and validates a project against the available profiles and widgets.
    /// </summary>
    public static ClassicMacProject Deserialize(
        string json,
        DesignerWidgetRegistry widgetRegistry,
        DesignerNonVisualComponentRegistry? componentRegistry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(widgetRegistry);
        componentRegistry ??= ClassicMacNonVisualComponentCatalog.CreateRegistry();

        var document = JsonSerializer.Deserialize<ProjectFileDocument>(
            json,
            SerializerOptions) ??
            throw new InvalidDataException("The project document is empty.");
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Project schema version {document.SchemaVersion} is not supported.");
        }

        var profile = StudioTargetProfiles.All.FirstOrDefault(
            item => item.Id == document.TargetProfileId) ??
            throw new InvalidDataException(
                $"Target profile '{document.TargetProfileId}' is not registered.");
        if (document.Controls is null)
        {
            throw new InvalidDataException("The project is missing its controls collection.");
        }

        var controls = document.Controls.Select(control =>
        {
            if (control is null ||
                string.IsNullOrWhiteSpace(control.Id) ||
                string.IsNullOrWhiteSpace(control.WidgetTypeId) ||
                control.Text is null)
            {
                throw new InvalidDataException("The project contains an incomplete control.");
            }

            var model = new DesignerControl(
                control.Id,
                control.WidgetTypeId,
                control.Text,
                control.X,
                control.Y,
                control.Width,
                control.Height,
                control.Properties,
                control.Events);
            widgetRegistry.ValidateControl(model);
            var maximumWidth = profile.IsFullScreen
                ? profile.ScreenWidth
                : profile.ScreenWidth - 50;
            var maximumHeight = profile.IsFullScreen
                ? profile.ScreenHeight
                : profile.ScreenHeight - 80;
            if (!model.FitsWithin(maximumWidth, maximumHeight))
            {
                throw new InvalidDataException(
                    $"Control '{model.Id}' is outside the target form bounds.");
            }

            return model;
        }).ToArray();
        var components = (document.Components ?? [])
            .Select(component =>
            {
                if (component is null ||
                    string.IsNullOrWhiteSpace(component.Id) ||
                    string.IsNullOrWhiteSpace(component.ComponentTypeId))
                {
                    throw new InvalidDataException(
                        "The project contains an incomplete nonvisual component.");
                }

                var model = new DesignerNonVisualComponent(
                    component.Id,
                    component.ComponentTypeId,
                    component.Properties,
                    component.Events);
                componentRegistry.ValidateComponent(model);
                return model;
            })
            .ToArray();

        if (string.IsNullOrWhiteSpace(document.Name) ||
            string.IsNullOrWhiteSpace(document.FormTitle))
        {
            throw new InvalidDataException("The project is missing its name or form title.");
        }

        var formTypeName = document.FormTypeName ?? "MainForm";
        if (!ClassicMacCIdentifier.IsValid(formTypeName))
        {
            throw new InvalidDataException("The project has an invalid form type name.");
        }

        var formProperties = document.FormProperties ?? new MacFormProperties();
        try
        {
            formProperties.ValidateFor(profile);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(
                $"The project has invalid form properties: {error.Message}",
                error);
        }

        if (controls.Any(control =>
                !control.FitsWithin(
                    formProperties.GetClientWidth(profile),
                    formProperties.GetClientHeight(profile))))
        {
            throw new InvalidDataException(
                "A project control is outside the configured form bounds.");
        }

        if (controls.Select(control => control.Id).Distinct(StringComparer.Ordinal).Count() !=
            controls.Length)
        {
            throw new InvalidDataException("Control IDs must be unique within a project.");
        }
        var allIds = controls.Select(control => control.Id)
            .Concat(components.Select(component => component.Id))
            .ToArray();
        if (allIds.Distinct(StringComparer.Ordinal).Count() != allIds.Length)
        {
            throw new InvalidDataException(
                "Control and component IDs must be unique within a project.");
        }

        var duplicateSerialPort = components
            .Where(component => component.ComponentTypeId == "serial-port")
            .GroupBy(
                component => component.Properties.GetValueOrDefault("port") ?? "Modem",
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSerialPort is not null)
        {
            throw new InvalidDataException(
                $"Only one serial component may be assigned to the {duplicateSerialPort.Key} port.");
        }

        var serialPortIds = components
            .Where(component => component.ComponentTypeId == "serial-port")
            .Select(component => component.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var transfer in components.Where(
                     component => component.ComponentTypeId == "protocol-transfer"))
        {
            var serialPortId = transfer.Properties.GetValueOrDefault("serialPortId");
            if (!string.IsNullOrWhiteSpace(serialPortId) &&
                !serialPortIds.Contains(serialPortId))
            {
                throw new InvalidDataException(
                    $"Protocol component '{transfer.Id}' references unknown serial component '{serialPortId}'.");
            }
        }

        return new ClassicMacProject(
            document.Name,
            profile.Id,
            document.FormTitle,
            controls,
            formTypeName,
            components,
            formProperties);
    }

    /// <summary>Writes a project definition to a UTF-8 JSON file.</summary>
    public static void Save(string path, ClassicMacProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        File.WriteAllText(path, Serialize(project));
    }

    /// <summary>Loads and validates a project definition from a JSON file.</summary>
    public static ClassicMacProject Load(
        string path,
        DesignerWidgetRegistry widgetRegistry,
        DesignerNonVisualComponentRegistry? componentRegistry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(widgetRegistry);
        return Deserialize(File.ReadAllText(path), widgetRegistry, componentRegistry);
    }

    private sealed class ProjectFileDocument
    {
        public int SchemaVersion { get; set; }

        public string? Name { get; set; }

        public string? TargetProfileId { get; set; }

        public string? FormTitle { get; set; }

        public string? FormTypeName { get; set; }

        public MacFormProperties? FormProperties { get; set; }

        public List<ControlFileDocument?>? Controls { get; set; }

        public List<ComponentFileDocument?>? Components { get; set; }

        public static ProjectFileDocument FromProject(ClassicMacProject project) =>
            new()
            {
                SchemaVersion = CurrentSchemaVersion,
                Name = project.Name,
                TargetProfileId = project.TargetProfileId,
                FormTitle = project.FormTitle,
                FormTypeName = project.FormTypeName,
                FormProperties = project.FormProperties,
                Controls = project.Controls
                    .Select(ControlFileDocument.FromControl)
                    .Cast<ControlFileDocument?>()
                    .ToList(),
                Components = project.Components
                    .Select(ComponentFileDocument.FromComponent)
                    .Cast<ComponentFileDocument?>()
                    .ToList(),
            };
    }

    private sealed class ControlFileDocument
    {
        public string? Id { get; set; }

        public string? WidgetTypeId { get; set; }

        public string? Text { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public Dictionary<string, string>? Properties { get; set; }

        public Dictionary<string, string>? Events { get; set; }

        public static ControlFileDocument FromControl(DesignerControl control) =>
            new()
            {
                Id = control.Id,
                WidgetTypeId = control.WidgetTypeId,
                Text = control.Text,
                X = control.X,
                Y = control.Y,
                Width = control.Width,
                Height = control.Height,
                Properties = control.Properties.ToDictionary(
                    property => property.Key,
                    property => property.Value,
                    StringComparer.Ordinal),
                Events = control.Events.ToDictionary(
                    eventBinding => eventBinding.Key,
                    eventBinding => eventBinding.Value,
                    StringComparer.Ordinal),
            };
    }

    private sealed class ComponentFileDocument
    {
        public string? Id { get; set; }

        public string? ComponentTypeId { get; set; }

        public Dictionary<string, string>? Properties { get; set; }

        public Dictionary<string, string>? Events { get; set; }

        public static ComponentFileDocument FromComponent(
            DesignerNonVisualComponent component) =>
            new()
            {
                Id = component.Id,
                ComponentTypeId = component.ComponentTypeId,
                Properties = component.Properties.ToDictionary(
                    property => property.Key,
                    property => property.Value,
                    StringComparer.Ordinal),
                Events = component.Events.ToDictionary(
                    eventBinding => eventBinding.Key,
                    eventBinding => eventBinding.Value,
                    StringComparer.Ordinal),
            };
    }
}
