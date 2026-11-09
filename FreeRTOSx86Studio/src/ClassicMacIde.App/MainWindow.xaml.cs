using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClassicMacIde.Core;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace ClassicMacIde.App;

public partial class MainWindow : Window
{
    private static readonly FontFamily ClassicMacFont = new("Chicago");
    private readonly DesignerWidgetRegistry _widgetRegistry =
        ClassicMacWidgetCatalog.CreateRegistry();
    private readonly DesignerNonVisualComponentRegistry _componentRegistry =
        ClassicMacNonVisualComponentCatalog.CreateRegistry();
    private readonly IReadOnlyDictionary<string, Func<DesignerControl, FrameworkElement>>
        _previewRenderers;
    private readonly List<(DesignerControl Model, Border View)> _controls = [];
    private readonly List<DesignerNonVisualComponent> _nonVisualComponents = [];
    private readonly List<string> _selectedControlIds = [];
    private readonly Dictionary<string, List<Thumb>> _resizeHandles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Canvas> _gripCanvases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<ResizeGripDirection, Thumb>> _resizeGrips =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Thumb Start, Thumb End)> _lineEndpointGrips =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, System.Windows.Shapes.Path> _graphicLinePaths =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, (double X, double Y)> _dragOrigins =
        new(StringComparer.Ordinal);
    private string? _selectedControlId;
    private string? _selectedNonVisualComponentId;
    private string? _resizingControlId;
    private string? _selectedWidgetTypeId;
    private Point _dragStart;
    private double _dragStartWidth;
    private double _dragStartHeight;
    private bool _updatingProperties;
    private bool _loadingProject;
    private bool _loadingCodeFile;
    private bool _synchronizingComponentPaletteSelection;
    private bool _projectDirty;
    private bool _codeFileDirty;
    private string? _projectFilePath;
    private string? _openCodeFilePath;
    private int? _emulatorProcessId;
    private string? _emulatorProcessName;
    private MacEmulator _selectedEmulator = MacEmulator.MiniVmac;
    private string? _basiliskSerialA;
    private string? _basiliskSerialB;
    private SerialTerminalWindow? _serialTerminalWindow;
    private DateTime _buildRunStartedAt;
    private string _formTypeName = "MainForm";
    private MacFormProperties _formProperties = new();
    private SerialAgentConnectionWindow? _serialAgentConnectionWindow;

    public MainWindow()
    {
        InitializeComponent();
        _previewRenderers =
            new Dictionary<string, Func<DesignerControl, FrameworkElement>>(
                StringComparer.Ordinal)
            {
                ["static-text"] = CreateStaticTextPreview,
                ["editable-text"] = CreateEditableTextPreview,
                ["button"] = CreateButtonPreview,
                ["checkbox"] = CreateCheckboxPreview,
                ["radio-button"] = CreateRadioButtonPreview,
                ["popup-menu"] = CreatePopupMenuPreview,
                ["list-box"] = CreateListBoxPreview,
                ["progress-bar"] = CreateProgressBarPreview,
                ["scrollbar"] = CreateScrollbarPreview,
                ["graphic-line"] = CreateGraphicLinePreview,
                ["graphic-rectangle"] = CreateGraphicRectanglePreview,
                ["graphic-circle"] = CreateGraphicCirclePreview,
                ["graphic-arc"] = CreateGraphicArcPreview,
            };
        TargetProfileSelector.ItemsSource = MacTargetProfiles.All;
        TargetProfileSelector.SelectedIndex = 0;
        WidgetPalette.ItemsSource = _widgetRegistry.Definitions
            .Where(definition => definition.Category == DesignerWidgetCategory.Controls)
            .ToArray();
        GraphicsPalette.ItemsSource = _widgetRegistry.Definitions
            .Where(definition => definition.Category == DesignerWidgetCategory.Graphics)
            .ToArray();
        NonVisualComponentPalette.ItemsSource = _componentRegistry.Definitions;
        RibbonComponentPalette.ItemsSource = _componentRegistry.Definitions;
        GraphicsPalette.SelectedIndex = 0;
        WidgetPalette.SelectedItem = _widgetRegistry.GetRequired("button");
        var initialComponent = _componentRegistry.GetRequired("file-access");
        NonVisualComponentPalette.SelectedItem = initialComponent;
        RibbonComponentPalette.SelectedItem = initialComponent;
        CodeEditor.TextChanged += CodeEditor_TextChanged;
        CodeEditor.PreviewKeyDown += CodeEditor_PreviewKeyDown;
        UpdateGridAppearance();
        UpdateWindowTitle();
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".cmideproj",
            FileName = "ClassicMacStudioProject.cmideproj",
            Filter = "ClassicMacStudio Project (*.cmideproj)|*.cmideproj",
            OverwritePrompt = true,
            Title = "Create a ClassicMacStudio project",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var project = new ClassicMacProject(
            Path.GetFileNameWithoutExtension(dialog.FileName),
            "mac-se-68k",
            "Untitled Window",
            []);
        try
        {
            ApplyProject(dialog.FileName, project);
            _projectDirty = true;
            UpdateWindowTitle();
            SaveCurrentProject();
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not create project files: {error.Message}";
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "ClassicMacStudio Project (*.cmideproj)|*.cmideproj",
            Title = "Open a ClassicMacStudio project",
        };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscardChanges())
        {
            return;
        }

        try
        {
            ApplyProject(
                dialog.FileName,
                ClassicMacProjectSerializer.Load(dialog.FileName, _widgetRegistry));
        }
        catch (Exception error) when (
            error is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                NotSupportedException or
                ArgumentException or
                KeyNotFoundException or
                System.Text.Json.JsonException)
        {
            StatusText.Text = $"Could not open project: {error.Message}";
        }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (_projectFilePath is null)
        {
            SaveProjectAs_Click(sender, e);
            return;
        }

        SaveCurrentProject();
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".cmideproj",
            FileName = _projectFilePath is null
                ? "ClassicMacStudioProject.cmideproj"
                : Path.GetFileName(_projectFilePath),
            Filter = "ClassicMacStudio Project (*.cmideproj)|*.cmideproj",
            OverwritePrompt = true,
            Title = "Save ClassicMacStudio project",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _projectFilePath = dialog.FileName;
        _projectDirty = true;
        UpdateWindowTitle();
        SaveCurrentProject();
    }

    private bool SaveCurrentProject()
    {
        if (_projectFilePath is null ||
            TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            StatusText.Text = "Create or open a project before saving.";
            return false;
        }

        try
        {
            var project = new ClassicMacProject(
                Path.GetFileNameWithoutExtension(_projectFilePath),
                profile.Id,
                FormTitle.Text,
                _controls.Select(control => control.Model),
                _formTypeName,
                _nonVisualComponents,
                _formProperties);
            EnsureUserSourceFiles(
                Path.Combine(Path.GetDirectoryName(_projectFilePath)!, "src"));
            ClassicMacProjectSerializer.Save(_projectFilePath, project);
            _projectDirty = false;
            SaveProjectMenuItem.IsEnabled = true;
            RefreshProjectTree();
            UpdateWindowTitle();
            StatusText.Text = $"Saved project: {Path.GetFileName(_projectFilePath)}";
            return true;
        }
        catch (Exception error) when (
            error is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                InvalidOperationException)
        {
            StatusText.Text = $"Could not save project: {error.Message}";
            return false;
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_projectDirty && !_codeFileDirty)
        {
            return true;
        }

        var choice = MessageBox.Show(
            this,
            "Save your project and code-file changes before continuing?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.No)
        {
            return true;
        }
        if (choice != MessageBoxResult.Yes)
        {
            return false;
        }
        if (_projectDirty && _projectFilePath is null)
        {
            SaveProjectAs_Click(this, new RoutedEventArgs());
            if (_projectDirty)
            {
                return false;
            }
        }

        return SaveDirtyChanges();
    }

    private bool SaveDirtyChanges()
    {
        if (_codeFileDirty && !SaveCurrentCodeFile())
        {
            return false;
        }

        return !_projectDirty || SaveCurrentProject();
    }

    private void ApplyProject(string path, ClassicMacProject project)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureUserSourceFiles(
            Path.Combine(Path.GetDirectoryName(fullPath)!, "src"));
        _loadingProject = true;
        try
        {
            _controls.Clear();
            _nonVisualComponents.Clear();
            FormCanvas.Children.Clear();
            _resizeHandles.Clear();
            _gripCanvases.Clear();
            _resizeGrips.Clear();
            _lineEndpointGrips.Clear();
            _graphicLinePaths.Clear();
            _selectedControlIds.Clear();
            _selectedControlId = null;
            _selectedNonVisualComponentId = null;
            _resizingControlId = null;
            _dragOrigins.Clear();
            _openCodeFilePath = null;
            _codeFileDirty = false;
            CodeEditor.Clear();
            CodeEditor.IsReadOnly = true;
            CodeEditor.SyntaxHighlighting = null;
            ActiveCodeFileText.Text = "Open a file from Project Explorer";
            SaveCodeFileMenuItem.IsEnabled = false;

            FormTitle.Text = project.FormTitle;
            FormTitleEditor.Text = project.FormTitle;
            _formTypeName = project.FormTypeName;
            FormTypeNameEditor.Text = project.FormTypeName;
            _formProperties = project.FormProperties;
            TargetProfileSelector.SelectedItem = MacTargetProfiles.All.Single(
                profile => profile.Id == project.TargetProfileId);
            foreach (var model in project.Controls)
            {
                var view = CreateWidgetView(
                    model,
                    _widgetRegistry.GetRequired(model.WidgetTypeId));
                AttachWidgetInput(model.Id, view);
                Canvas.SetLeft(view, model.X);
                Canvas.SetTop(view, model.Y);
                _controls.Add((model, view));
                FormCanvas.Children.Add(view);
            }
            _nonVisualComponents.AddRange(project.Components);
            RefreshNonVisualComponentTray();
            if (TargetProfileSelector.SelectedItem is MacTargetProfile profile)
            {
                UpdateFormPreview(profile);
            }

            _projectFilePath = fullPath;
            _projectDirty = false;
        }
        finally
        {
            _loadingProject = false;
        }

        RefreshPropertyInspector(null);
        RefreshMenuBarPreview();
        UpdateSelectionVisuals();
        UpdateBuildRunAvailability();
        ProjectNameText.Text = Path.GetFileNameWithoutExtension(path);
        SaveProjectMenuItem.IsEnabled = true;
        RefreshProjectTree();
        UpdateWindowTitle();
        StatusText.Text = $"Opened project: {Path.GetFileName(path)}";
    }

    private static void EnsureUserSourceFiles(
        string sourceDirectory,
        string? mainSource = null)
    {
        Directory.CreateDirectory(sourceDirectory);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var mainSourcePath = Path.Combine(sourceDirectory, "main.c");
        if (!File.Exists(mainSourcePath))
        {
            File.WriteAllText(
                mainSourcePath,
                mainSource ?? MacFormCodeGenerator.CreateMainSource(),
                encoding);
        }

        var userEventsPath = Path.Combine(sourceDirectory, "user_events.c");
        if (!File.Exists(userEventsPath))
        {
            File.WriteAllText(
                userEventsPath,
                """
                #include "designer_form.h"

                void MacStudioUserControlClicked(DialogPtr dialog, const char *controlId, short item)
                {
                    /* Add application behavior for the selected control here. */
                    (void)dialog;
                    (void)controlId;
                    (void)item;
                }
                """,
                encoding);
        }
    }

    private void UpdateWindowTitle()
    {
        var projectName = _projectFilePath is null
            ? "No project"
            : Path.GetFileNameWithoutExtension(_projectFilePath);
        Title = $"ClassicMacStudio {ApplicationVersion} — {projectName}" +
            (_projectDirty || _codeFileDirty ? "*" : string.Empty);
    }

    private static string ApplicationVersion =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(2)
        ?? throw new InvalidOperationException(
            "The ClassicMacStudio assembly version is unavailable.");

    private void RefreshProjectTree()
    {
        ProjectFilesTree.Items.Clear();
        if (_projectFilePath is null)
        {
            ProjectNameText.Text = "No project open";
            return;
        }

        var rootPath = Path.GetDirectoryName(_projectFilePath);
        if (rootPath is null || !Directory.Exists(rootPath))
        {
            StatusText.Text = "The project folder no longer exists.";
            return;
        }

        ProjectNameText.Text = Path.GetFileNameWithoutExtension(_projectFilePath);
        var root = CreateProjectDirectoryNode(new DirectoryInfo(rootPath));
        ProjectFilesTree.Items.Add(root);
        root.IsExpanded = true;
    }

    private static TreeViewItem CreateProjectDirectoryNode(DirectoryInfo directory)
    {
        var node = new TreeViewItem
        {
            Header = directory.Name,
            Tag = directory.FullName,
        };
        node.Items.Add(new TreeViewItem());
        node.Expanded += ProjectDirectory_Expanded;
        return node;
    }

    private static void ProjectDirectory_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender ||
            sender is not TreeViewItem { Tag: string path } node ||
            node.Items.Count != 1 ||
            node.Items[0] is not TreeViewItem { Tag: null })
        {
            return;
        }

        node.Items.Clear();
        try
        {
            foreach (var directory in new DirectoryInfo(path)
                         .EnumerateDirectories()
                         .Where(directory => !ShouldSkipProjectDirectory(directory))
                         .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Items.Add(CreateProjectDirectoryNode(directory));
            }

            foreach (var file in new DirectoryInfo(path)
                         .EnumerateFiles()
                         .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
            {
                node.Items.Add(new TreeViewItem
                {
                    Header = file.Name,
                    Tag = file.FullName,
                });
            }
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            node.Items.Add(new TreeViewItem
            {
                Header = $"Could not read folder: {error.Message}",
                IsEnabled = false,
            });
        }
    }

    private static bool ShouldSkipProjectDirectory(DirectoryInfo directory) =>
        (directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
        directory.Name is "bin" or "obj" or ".build" or ".git" or ".local-tools" or
            "node_modules" or ".vs";

    private void ProjectFilesTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectFilesTree.SelectedItem is not TreeViewItem { Tag: string path } ||
            !File.Exists(path) ||
            !ConfirmCodeFileSwitch(path))
        {
            return;
        }

        OpenCodeFile(path);
    }

    private bool ConfirmCodeFileSwitch(string path)
    {
        if (!_codeFileDirty ||
            string.Equals(_openCodeFilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var choice = MessageBox.Show(
            this,
            "Save changes to the open source file before opening another file?",
            "Unsaved code",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel)
        {
            return false;
        }
        if (choice == MessageBoxResult.Yes)
        {
            return SaveCurrentCodeFile();
        }

        _codeFileDirty = false;
        UpdateCodeFileLabel();
        UpdateWindowTitle();
        return true;
    }

    private void OpenCodeFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.Equals(
                _openCodeFilePath,
                fullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceTabs.SelectedIndex = 1;
            CodeEditor.Focus();
            return;
        }

        try
        {
            var file = new FileInfo(fullPath);
            if (file.Length > 2 * 1024 * 1024)
            {
                StatusText.Text = "The code editor only opens files up to 2 MiB.";
                return;
            }

            _loadingCodeFile = true;
            CodeEditor.Text = File.ReadAllText(path);
            CodeEditor.SyntaxHighlighting = FindHighlighting(path);
            CodeEditor.IsReadOnly = IsGeneratedSourceFile(path);
            _openCodeFilePath = file.FullName;
            _codeFileDirty = false;
            SaveCodeFileMenuItem.IsEnabled = true;
            UpdateCodeFileLabel();
            WorkspaceTabs.SelectedIndex = 1;
            _loadingCodeFile = false;
            CodeEditor.Focus();
            StatusText.Text = $"Opened {Path.GetFileName(path)}";
            UpdateWindowTitle();
        }
        catch (Exception error) when (
            error is IOException or
                UnauthorizedAccessException or
                System.Text.DecoderFallbackException)
        {
            _loadingCodeFile = false;
            StatusText.Text = $"Could not open source file: {error.Message}";
        }
    }

    private static IHighlightingDefinition? FindHighlighting(string path)
    {
        var definitionName = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".c" or ".h" or ".r" => "C++",
            ".cs" => "C#",
            ".json" or ".cmideproj" => "JavaScript",
            ".xml" => "XML",
            ".md" => "Markdown",
            ".ps1" => "PowerShell",
            ".sh" => "Bash",
            _ => null,
        };
        return definitionName is null
            ? null
            : HighlightingManager.Instance.GetDefinition(definitionName);
    }

    private static bool IsGeneratedSourceFile(string path) =>
        Path.GetFileName(path) is "designer_form.c" or "designer_form.h" or
            "designer_form.r" or "CMakeLists.txt";

    private void CodeEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_loadingCodeFile || _openCodeFilePath is null)
        {
            return;
        }

        _codeFileDirty = true;
        SaveCodeFileMenuItem.IsEnabled = true;
        UpdateCodeFileLabel();
        UpdateWindowTitle();
    }

    private void CodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveCurrentCodeFile();
            e.Handled = true;
        }
    }

    private void SaveCodeFile_Click(object sender, RoutedEventArgs e) =>
        SaveCurrentCodeFile();

    private bool SaveCurrentCodeFile()
    {
        if (_openCodeFilePath is null)
        {
            StatusText.Text = "Open a project source file before saving code.";
            return false;
        }

        try
        {
            File.WriteAllText(
                _openCodeFilePath,
                CodeEditor.Text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _codeFileDirty = false;
            UpdateCodeFileLabel();
            UpdateWindowTitle();
            StatusText.Text = $"Saved {Path.GetFileName(_openCodeFilePath)}";
            return true;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save source file: {error.Message}";
            return false;
        }
    }

    private void UpdateCodeFileLabel()
    {
        ActiveCodeFileText.Text = _openCodeFilePath is null
            ? "Open a file from Project Explorer"
            : Path.GetFileName(_openCodeFilePath) +
                (_codeFileDirty ? "  (modified)" : string.Empty);
    }

    private void RefreshFormPropertiesInspector()
    {
        if (TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        var wasUpdatingProperties = _updatingProperties;
        _updatingProperties = true;
        FormTitleBarEditor.IsChecked = _formProperties.ShowTitleBar;
        FormResizableEditor.IsChecked = _formProperties.Resizable;
        FormCloseBoxEditor.IsChecked = _formProperties.HasCloseBox;
        FormZoomBoxEditor.IsChecked = _formProperties.HasZoomBox;
        FormMovableEditor.IsChecked = _formProperties.Movable;
        FormVisibleEditor.IsChecked = _formProperties.Visible;
        FormCenterOnScreenEditor.IsChecked = _formProperties.CenterOnScreen;
        FormResizableEditor.IsEnabled = _formProperties.ShowTitleBar;
        FormCloseBoxEditor.IsEnabled = _formProperties.ShowTitleBar;
        FormZoomBoxEditor.IsEnabled = _formProperties.ShowTitleBar;
        FormMovableEditor.IsEnabled = _formProperties.ShowTitleBar;
        FormWidthEditor.Text = _formProperties.GetClientWidth(profile)
            .ToString(CultureInfo.InvariantCulture);
        FormHeightEditor.Text = _formProperties.GetClientHeight(profile)
            .ToString(CultureInfo.InvariantCulture);
        FormLeftEditor.Text = _formProperties.Left.ToString(CultureInfo.InvariantCulture);
        FormTopEditor.Text = _formProperties.Top.ToString(CultureInfo.InvariantCulture);
        FormLeftEditor.IsEnabled = !_formProperties.CenterOnScreen;
        FormTopEditor.IsEnabled = !_formProperties.CenterOnScreen;
        _updatingProperties = wasUpdatingProperties;
    }

    private void FormBooleanProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingProperties ||
            sender is not CheckBox editor ||
            editor.Tag is not string propertyId)
        {
            return;
        }

        var value = editor.IsChecked == true;
        var updated = propertyId switch
        {
            "showTitleBar" => _formProperties with
            {
                ShowTitleBar = value,
                Resizable = value && _formProperties.Resizable,
                HasCloseBox = value && _formProperties.HasCloseBox,
                HasZoomBox = value && _formProperties.HasZoomBox,
                Movable = value && _formProperties.Movable,
            },
            "resizable" => _formProperties with { Resizable = value },
            "hasCloseBox" => _formProperties with { HasCloseBox = value },
            "hasZoomBox" => _formProperties with { HasZoomBox = value },
            "movable" => _formProperties with { Movable = value },
            "visible" => _formProperties with { Visible = value },
            "centerOnScreen" => _formProperties with { CenterOnScreen = value },
            _ => _formProperties,
        };
        ApplyFormProperties(updated);
    }

    private void FormGeometryProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingProperties ||
            sender is not TextBox editor ||
            editor.Tag is not string propertyId)
        {
            return;
        }

        if (!int.TryParse(
                editor.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            StatusText.Text = "Form size and position values must be whole numbers.";
            RefreshFormPropertiesInspector();
            return;
        }

        var updated = propertyId switch
        {
            "width" => _formProperties with { Width = value },
            "height" => _formProperties with { Height = value },
            "left" => _formProperties with { Left = value },
            "top" => _formProperties with { Top = value },
            _ => _formProperties,
        };
        ApplyFormProperties(updated);
    }

    private void ApplyFormProperties(MacFormProperties updated)
    {
        if (TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        try
        {
            updated.ValidateFor(profile);
        }
        catch (ArgumentException error)
        {
            StatusText.Text = error.Message;
            RefreshFormPropertiesInspector();
            return;
        }

        if (_controls.Any(control =>
                !control.Model.FitsWithin(
                    updated.GetClientWidth(profile),
                    updated.GetClientHeight(profile))))
        {
            StatusText.Text = "All widgets must fit inside the configured form bounds.";
            RefreshFormPropertiesInspector();
            return;
        }

        if (updated == _formProperties)
        {
            return;
        }

        _formProperties = updated;
        UpdateFormPreview(profile);
        RefreshFormPropertiesInspector();
        MarkProjectDirty();
    }

    private void UpdateFormPreview(MacTargetProfile? profile = null)
    {
        profile ??= TargetProfileSelector.SelectedItem as MacTargetProfile;
        if (profile is null)
        {
            return;
        }

        var clientWidth = Math.Clamp(
            _formProperties.GetClientWidth(profile),
            100,
            profile.ScreenWidth - 50);
        var clientHeight = Math.Clamp(
            _formProperties.GetClientHeight(profile),
            60,
            profile.ScreenHeight - 80);
        var titleBarHeight = _formProperties.ShowTitleBar ? 20 : 0;
        FormFrame.Width = clientWidth + 2;
        FormFrame.Height = clientHeight + titleBarHeight + 2;
        FormCanvas.Width = clientWidth;
        FormCanvas.Height = clientHeight;
        FormTitleBarRow.Height = new GridLength(titleBarHeight);
        FormTitleBar.Visibility = _formProperties.ShowTitleBar
            ? Visibility.Visible
            : Visibility.Collapsed;
        FormCloseBox.Visibility = _formProperties.HasCloseBox
            ? Visibility.Visible
            : Visibility.Collapsed;
        FormZoomBox.Visibility = _formProperties.HasZoomBox
            ? Visibility.Visible
            : Visibility.Collapsed;
        FormGrowBox.Visibility = _formProperties.Resizable
            ? Visibility.Visible
            : Visibility.Collapsed;
        Canvas.SetLeft(
            FormFrame,
            _formProperties.CenterOnScreen
                ? (profile.ScreenWidth - FormFrame.Width) / 2
                : Math.Clamp(_formProperties.Left, 0, profile.ScreenWidth - FormFrame.Width));
        Canvas.SetTop(
            FormFrame,
            _formProperties.CenterOnScreen
                ? (profile.ScreenHeight - FormFrame.Height) / 2
                : Math.Clamp(_formProperties.Top, 0, profile.ScreenHeight - FormFrame.Height));
    }

    private void FormTitleEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FormTitleEditor.Text))
        {
            FormTitleEditor.Text = FormTitle.Text;
            StatusText.Text = "Form title cannot be empty.";
            return;
        }

        if (!string.Equals(FormTitle.Text, FormTitleEditor.Text, StringComparison.Ordinal))
        {
            FormTitle.Text = FormTitleEditor.Text;
            MarkProjectDirty();
        }
    }

    private void FormTypeNameEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!MacFormCodeGenerator.IsValidFormTypeName(FormTypeNameEditor.Text))
        {
            FormTypeNameEditor.Text = _formTypeName;
            StatusText.Text =
                "Form type name must be a C identifier with at most 31 letters, digits, or underscores.";
            return;
        }

        if (!string.Equals(_formTypeName, FormTypeNameEditor.Text, StringComparison.Ordinal))
        {
            var newFormTypeName = FormTypeNameEditor.Text;
            try
            {
                UpdateBoundEventHandlerFormTypes(_formTypeName, newFormTypeName);
                _formTypeName = newFormTypeName;
                MarkProjectDirty();
            }
            catch (Exception error) when (
                error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                FormTypeNameEditor.Text = _formTypeName;
                StatusText.Text = $"Could not update event handler signatures: {error.Message}";
            }
        }
    }

    private void MarkProjectDirty()
    {
        if (_loadingProject)
        {
            return;
        }

        _projectDirty = true;
        SaveProjectMenuItem.IsEnabled = _projectFilePath is not null;
        UpdateWindowTitle();
    }

    private void TargetProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        PreviewCanvas.Width = profile.ScreenWidth;
        PreviewCanvas.Height = profile.ScreenHeight;
        PreviewCanvas.Background = profile.ColorMode == DisplayColorMode.Monochrome
            ? Brushes.White
            : Brushes.LightGray;
        MenuBar.Width = profile.ScreenWidth;
        UpdateFormPreview(profile);
        UpdateGridAppearance();
        foreach (var control in _controls.ToArray())
        {
            ReplaceWidgetView(control.Model);
        }
        if (_selectedControlId is not null)
        {
            RefreshPropertyInspector(FindControl(_selectedControlId));
        }
        else if (_selectedNonVisualComponentId is string componentId)
        {
            RefreshNonVisualComponentInspector(
                _nonVisualComponents.FirstOrDefault(component => component.Id == componentId));
        }
        else
        {
            RefreshPropertyInspector(null);
        }

        ProfileNameText.Text = profile.DisplayName;
        ResolutionText.Text = $"{profile.ScreenWidth} x {profile.ScreenHeight} pixels";
        ModeText.Text =
            $"{profile.ColorMode} ({profile.ColorDepthBits}-bit), {profile.Architecture}";
        VerificationText.Text = profile.VerificationState switch
        {
            TargetVerificationState.GuestBootVerified =>
                "System 7.5.5 guest boot observed; application launch is not verified.",
            TargetVerificationState.ApplicationLaunchVerified =>
                "Application launch verified.",
            _ => "Candidate profile; hardware and application compatibility are unverified.",
        };
        StatusText.Text =
            $"{profile.DisplayName} | {profile.ScreenWidth} x {profile.ScreenHeight} | {profile.ColorMode} | {profile.VerificationState}";
        UpdateBuildRunAvailability();
        if (!_loadingProject && e.RemovedItems.Count > 0)
        {
            MarkProjectDirty();
        }
    }

    private void WidgetPalette_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: DesignerWidgetDefinition definition })
        {
            _selectedWidgetTypeId = definition.Id;
        }
    }

    private void AddNonVisualComponent_Click(object sender, RoutedEventArgs e)
    {
        if (NonVisualComponentPalette.SelectedItem is not DesignerNonVisualComponentDefinition definition)
        {
            StatusText.Text = "Choose a component type before adding it.";
            return;
        }

        var id = CreateNextNonVisualComponentId(definition.Id);
        var component = _componentRegistry.CreateComponent(id, definition.Id);
        _nonVisualComponents.Add(component);
        _selectedNonVisualComponentId = component.Id;
        MarkProjectDirty();
        RefreshNonVisualComponentTray();
        NonVisualComponentTray.SelectedItem = component;
        RefreshMenuBarPreview();
        StatusText.Text = $"Added {definition.DisplayName} component {component.Id}.";
    }

    private void NonVisualComponentPalette_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_synchronizingComponentPaletteSelection)
        {
            return;
        }

        _synchronizingComponentPaletteSelection = true;
        RibbonComponentPalette.SelectedItem = NonVisualComponentPalette.SelectedItem;
        _synchronizingComponentPaletteSelection = false;
    }

    private void RibbonComponentPalette_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_synchronizingComponentPaletteSelection)
        {
            return;
        }

        _synchronizingComponentPaletteSelection = true;
        NonVisualComponentPalette.SelectedItem = RibbonComponentPalette.SelectedItem;
        _synchronizingComponentPaletteSelection = false;
    }

    private void RemoveNonVisualComponent_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNonVisualComponentId is null)
        {
            StatusText.Text = "Select a component in the tray to remove it.";
            return;
        }

        var referencedByTransfer = _nonVisualComponents.Any(component =>
            component.ComponentTypeId == "protocol-transfer" &&
            component.Properties.GetValueOrDefault("serialPortId") ==
                _selectedNonVisualComponentId);
        if (referencedByTransfer)
        {
            StatusText.Text =
                "Remove or reassign the protocol component before removing its serial port.";
            return;
        }

        _nonVisualComponents.RemoveAll(
            component => component.Id == _selectedNonVisualComponentId);
        _selectedNonVisualComponentId = null;
        RefreshNonVisualComponentTray();
        RefreshPropertyInspector(null);
        RefreshMenuBarPreview();
        MarkProjectDirty();
        StatusText.Text = "Removed the selected nonvisual component.";
    }

    private void NonVisualComponentTray_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingProject ||
            NonVisualComponentTray.SelectedItem is not DesignerNonVisualComponent component)
        {
            return;
        }

        _selectedControlIds.Clear();
        _selectedControlId = null;
        _selectedNonVisualComponentId = component.Id;
        UpdateSelectionVisuals();
        RefreshNonVisualComponentInspector(component);
    }

    private string CreateNextNonVisualComponentId(string componentTypeId)
    {
        var prefix = string.Concat(componentTypeId.Split('-')
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        var usedIds = _controls.Select(control => control.Model.Id)
            .Concat(_nonVisualComponents.Select(component => component.Id))
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = $"{prefix}{index}";
            if (!usedIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No component identifier is available.");
    }

    private void RefreshNonVisualComponentTray()
    {
        var selectedId = _selectedNonVisualComponentId;
        NonVisualComponentTray.ItemsSource = _nonVisualComponents.ToArray();
        NonVisualComponentTray.SelectedItem = _nonVisualComponents.FirstOrDefault(
            component => component.Id == selectedId);
    }

    private void RefreshMenuBarPreview()
    {
        DesignerMenuTitles.Children.Clear();
        var menuTitles = _nonVisualComponents
            .Where(component => component.ComponentTypeId == "menu-item")
            .Select(component =>
                component.Properties.GetValueOrDefault("menuTitle") ?? string.Empty)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var previewText = menuTitles.Length == 0
            ? "File  Edit  View  Special"
            : string.Join("  ", menuTitles);
        DesignerMenuTitles.Children.Add(new TextBlock
        {
            Text = previewText,
            FontFamily = ClassicMacFont,
            FontSize = 9,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private void PaletteTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PaletteTabs))
        {
            return;
        }

        var activePalette = PaletteTabs.SelectedIndex == 1
            ? GraphicsPalette
            : WidgetPalette;
        if (activePalette.SelectedItem is DesignerWidgetDefinition definition)
        {
            _selectedWidgetTypeId = definition.Id;
        }
    }

    private void PlaceWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWidgetTypeId is null)
        {
            StatusText.Text = "Choose a widget type before placing it.";
            return;
        }

        var definition = _widgetRegistry.GetRequired(_selectedWidgetTypeId);
        var location = FindAvailableLocation(
            definition.DefaultWidth,
            definition.DefaultHeight);
        if (location is null)
        {
            StatusText.Text = "No room for another widget in this target window.";
            return;
        }

        AddWidgetAt(_selectedWidgetTypeId, location.Value.X, location.Value.Y);
    }

    private void FormCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DesignerToolSelector.SelectedIndex == 1 &&
            _selectedWidgetTypeId is not null)
        {
            var position = e.GetPosition(FormCanvas);
            AddWidgetAt(
                _selectedWidgetTypeId,
                Math.Max(0, position.X),
                Math.Max(0, position.Y));
            e.Handled = true;
            return;
        }

        ClearSelection();
    }

    private void DesignerToolSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var resizing = DesignerToolSelector.SelectedIndex == 2;
        if (FormCanvas is not null)
        {
            FormCanvas.Cursor = resizing ? Cursors.SizeNWSE : Cursors.Arrow;
            UpdateSelectionVisuals();
        }
    }

    private void GridSettings_Changed(object sender, RoutedEventArgs e) =>
        UpdateGridAppearance();

    private void GridSpacing_LostFocus(object sender, RoutedEventArgs e) =>
        UpdateGridAppearance();

    private void UpdateGridAppearance()
    {
        if (FormCanvas is null ||
            GridSpacingSelector is null ||
            !TryGetGridSpacing(out var spacing))
        {
            return;
        }

        var dots = new GeometryDrawing(
            Brushes.LightGray,
            null,
            new EllipseGeometry(new Point(0.5, 0.5), 0.5, 0.5));
        FormCanvas.Background = new DrawingBrush(dots)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, spacing, spacing),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, spacing, spacing),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        StatusText.Text = SnapToGridToggle.IsChecked == true
            ? $"Grid {spacing}px | snap enabled"
            : $"Grid {spacing}px | snap disabled";
    }

    private void AddWidgetAt(string widgetTypeId, double x, double y)
    {
        var definition = _widgetRegistry.GetRequired(widgetTypeId);
        if (SnapToGridToggle.IsChecked == true)
        {
            if (!TryGetGridSpacing(out var spacing))
            {
                return;
            }

            x = DesignerGeometry.Snap(x, spacing);
            y = DesignerGeometry.Snap(y, spacing);
        }
        var model = _widgetRegistry.CreateControl(
            $"{widgetTypeId}-{_controls.Count + 1}",
            widgetTypeId,
            Math.Round(x, MidpointRounding.AwayFromZero),
            Math.Round(y, MidpointRounding.AwayFromZero));
        if (!model.FitsWithin(FormCanvas.Width, FormCanvas.Height))
        {
            StatusText.Text = "The selected widget does not fit at that position.";
            return;
        }

        var view = CreateWidgetView(model, definition);
        AttachWidgetInput(model.Id, view);
        Canvas.SetLeft(view, model.X);
        Canvas.SetTop(view, model.Y);
        _controls.Add((model, view));
        FormCanvas.Children.Add(view);
        SelectControl(model.Id);
        UpdateBuildRunAvailability();
        MarkProjectDirty();
    }

    private Border CreateWidgetView(
        DesignerControl model,
        DesignerWidgetDefinition definition)
    {
        if (!_previewRenderers.TryGetValue(definition.PreviewRendererId, out var renderer))
        {
            throw new InvalidOperationException(
                $"No WPF preview renderer is registered for '{definition.PreviewRendererId}'.");
        }

        var content = renderer(model);
        var layout = new Grid();
        layout.Children.Add(content);
        var gripCanvas = new Canvas
        {
            Width = Math.Max(0, model.Width - 2),
            Height = Math.Max(0, model.Height - 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = false,
            IsHitTestVisible = true,
        };
        layout.Children.Add(gripCanvas);
        _gripCanvases[model.Id] = gripCanvas;
        var grips = new List<Thumb>();
        _resizeHandles[model.Id] = grips;
        var resizeGrips = new Dictionary<ResizeGripDirection, Thumb>();
        _resizeGrips[model.Id] = resizeGrips;
        foreach (var direction in Enum.GetValues<ResizeGripDirection>())
        {
            var grip = CreateDesignerGrip(
                new ResizeGripTag(model.Id, direction),
                direction switch
                {
                    ResizeGripDirection.NorthWest or ResizeGripDirection.SouthEast =>
                        Cursors.SizeNWSE,
                    ResizeGripDirection.NorthEast or ResizeGripDirection.SouthWest =>
                        Cursors.SizeNESW,
                    ResizeGripDirection.North or ResizeGripDirection.South =>
                        Cursors.SizeNS,
                    _ => Cursors.SizeWE,
                },
                "Drag to resize");
            grip.DragDelta += (_, args) =>
                ResizeFromGrip(model.Id, direction, args.HorizontalChange, args.VerticalChange);
            gripCanvas.Children.Add(grip);
            grips.Add(grip);
            resizeGrips.Add(direction, grip);
        }

        if (model.WidgetTypeId == "graphic-line")
        {
            var linePath = content is Grid graphicsGrid
                ? graphicsGrid.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault()
                : null;
            if (linePath is not null)
            {
                _graphicLinePaths[model.Id] = linePath;
            }

            var startGrip = CreateDesignerGrip(
                new LineEndpointGripTag(model.Id, IsStart: true),
                Cursors.Cross,
                "Drag the line start point");
            var endGrip = CreateDesignerGrip(
                new LineEndpointGripTag(model.Id, IsStart: false),
                Cursors.Cross,
                "Drag the line end point");
            startGrip.DragDelta += (_, args) =>
                MoveLineEndpoint(model.Id, true, args.HorizontalChange, args.VerticalChange);
            endGrip.DragDelta += (_, args) =>
                MoveLineEndpoint(model.Id, false, args.HorizontalChange, args.VerticalChange);
            gripCanvas.Children.Add(startGrip);
            gripCanvas.Children.Add(endGrip);
            grips.Add(startGrip);
            grips.Add(endGrip);
            _lineEndpointGrips[model.Id] = (startGrip, endGrip);
        }

        var view = new Border
        {
            Width = model.Width,
            Height = model.Height,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = layout,
        };
        PositionDesignerGrips(model);
        return view;
    }

    private static Thumb CreateDesignerGrip(object tag, Cursor cursor, string toolTip) =>
        new()
        {
            Width = 8,
            Height = 8,
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Cursor = cursor,
            Tag = tag,
            Visibility = Visibility.Collapsed,
            ToolTip = toolTip,
        };

    private static FrameworkElement CreateStaticTextPreview(DesignerControl model) =>
        new TextBlock
        {
            Text = model.Text,
            FontFamily = ClassicMacFont,
            FontSize = 9,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private static FrameworkElement CreateEditableTextPreview(DesignerControl model) =>
        new TextBox
        {
            Text = model.Text,
            FontFamily = ClassicMacFont,
            FontSize = 9,
            Padding = new Thickness(3, 0, 3, 0),
            Foreground = Brushes.Black,
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

    private static FrameworkElement CreateButtonPreview(DesignerControl model) =>
        new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = model.Text,
                    Padding = new Thickness(2, 0, 2, 0),
                    FontFamily = ClassicMacFont,
                    FontSize = 9,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };

    private static FrameworkElement CreateCheckboxPreview(DesignerControl model) =>
        CreateClassicBooleanPreview(
            model.Text,
            bool.TryParse(
                model.Properties.GetValueOrDefault("checked"),
                out var isChecked) && isChecked,
            radio: false);

    private static FrameworkElement CreateRadioButtonPreview(DesignerControl model) =>
        CreateClassicBooleanPreview(
            model.Text,
            bool.TryParse(
                model.Properties.GetValueOrDefault("selected"),
                out var isSelected) && isSelected,
            radio: true);

    private static FrameworkElement CreateClassicBooleanPreview(
        string text,
        bool isSelected,
        bool radio)
    {
        var glyph = new Grid
        {
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (radio)
        {
            glyph.Children.Add(new Ellipse
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Fill = Brushes.White,
            });
            if (isSelected)
            {
                glyph.Children.Add(new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }
        else
        {
            glyph.Children.Add(new Rectangle
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Fill = Brushes.White,
            });
            if (isSelected)
            {
                glyph.Children.Add(new System.Windows.Shapes.Path
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.5,
                    Data = Geometry.Parse("M 2,7 L 5,10 L 11,2"),
                });
            }
        }

        var layout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        layout.Children.Add(glyph);
        layout.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(4, 0, 0, 0),
            FontFamily = ClassicMacFont,
            FontSize = 9,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return layout;
    }

    private static FrameworkElement CreatePopupMenuPreview(DesignerControl model)
    {
        var choices = ReadLines(model.Properties.GetValueOrDefault("items"));
        var selectedIndex = ParseInteger(
            model.Properties.GetValueOrDefault("selectedIndex"),
            choices.Count > 0 ? 0 : -1);
        var selectedText = choices.Count > 0 && selectedIndex >= 0 &&
            selectedIndex < choices.Count
            ? choices[selectedIndex]
            : model.Text;
        var layout = new Grid
        {
            Background = Brushes.White,
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        layout.Children.Add(new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = new TextBlock
            {
                Text = selectedText,
                Margin = new Thickness(3, 0, 2, 0),
                FontFamily = ClassicMacFont,
                FontSize = 9,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        });
        var arrow = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0, 1, 1, 1),
            Background = Brushes.White,
            Child = new Polygon
            {
                Points = new PointCollection
                {
                    new(4, 6),
                    new(14, 6),
                    new(9, 11),
                },
                Fill = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(arrow, 1);
        layout.Children.Add(arrow);
        return layout;
    }

    private static FrameworkElement CreateListBoxPreview(DesignerControl model)
    {
        var items = ReadLines(model.Properties.GetValueOrDefault("items"));
        var selectedIndex = ParseInteger(
            model.Properties.GetValueOrDefault("selectedIndex"),
            -1);
        var rows = new StackPanel();
        for (var index = 0; index < items.Count; index++)
        {
            var selected = index == selectedIndex;
            rows.Children.Add(new TextBlock
            {
                Text = items[index],
                Height = 16,
                Padding = new Thickness(2, 0, 0, 0),
                FontFamily = ClassicMacFont,
                FontSize = 9,
                Foreground = selected ? Brushes.White : Brushes.Black,
                Background = selected ? Brushes.Black : Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        return new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = rows,
        };
    }

    private static FrameworkElement CreateScrollbarPreview(DesignerControl model)
    {
        var minimum = ParseInteger(model.Properties.GetValueOrDefault("minimum"), 0);
        var maximum = ParseInteger(model.Properties.GetValueOrDefault("maximum"), 100);
        var value = ParseInteger(model.Properties.GetValueOrDefault("value"), minimum);
        var layout = new Grid
        {
            Background = Brushes.White,
        };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        layout.Children.Add(CreateScrollbarArrow(up: true));
        var track = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1, 0, 1, 0),
            Background = Brushes.White,
        };
        Grid.SetRow(track, 1);
        var range = Math.Max(1, maximum - minimum);
        var offset = Math.Clamp(value - minimum, 0, range);
        var thumbHeight = Math.Max(12, Math.Min(28, model.Height * 0.3));
        var thumbTravel = Math.Max(0, model.Height - 28 - thumbHeight);
        var thumb = new Border
        {
            Height = thumbHeight,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1, thumbTravel * offset / range, 1, 0),
        };
        track.Child = thumb;
        layout.Children.Add(track);
        layout.Children.Add(CreateScrollbarArrow(up: false));
        Grid.SetRow(layout.Children[layout.Children.Count - 1], 2);
        return layout;
    }

    private static FrameworkElement CreateProgressBarPreview(DesignerControl model)
    {
        var value = Math.Clamp(
            ParseInteger(model.Properties.GetValueOrDefault("value"), 0),
            0,
            100);
        var layout = new Grid
        {
            Background = Brushes.White,
            ClipToBounds = true,
        };
        layout.Children.Add(new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
        });
        layout.Children.Add(new Rectangle
        {
            Fill = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2),
            Width = Math.Max(0, (model.Width - 4) * value / 100),
        });
        return layout;
    }

    private FrameworkElement CreateGraphicLinePreview(DesignerControl model) =>
        CreateGraphicPreview(model, "line");

    private FrameworkElement CreateGraphicRectanglePreview(DesignerControl model) =>
        CreateGraphicPreview(model, "rectangle");

    private FrameworkElement CreateGraphicCirclePreview(DesignerControl model) =>
        CreateGraphicPreview(model, "circle");

    private FrameworkElement CreateGraphicArcPreview(DesignerControl model) =>
        CreateGraphicPreview(model, "arc");

    private FrameworkElement CreateGraphicPreview(
        DesignerControl model,
        string shape)
    {
        var geometry = shape switch
        {
            "line" => CreateGraphicLineGeometry(model),
            "rectangle" => new RectangleGeometry(new Rect(
                1,
                1,
                Math.Max(1, model.Width - 2),
                Math.Max(1, model.Height - 2))),
            "circle" => new EllipseGeometry(new Rect(
                1,
                1,
                Math.Max(1, model.Width - 2),
                Math.Max(1, model.Height - 2))),
            "arc" => CreateGraphicArcGeometry(model),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var isDithered = model.WidgetTypeId.EndsWith(
            "-dithered",
            StringComparison.Ordinal);
        var isFilled = model.WidgetTypeId.EndsWith(
                "-filled",
                StringComparison.Ordinal) || isDithered;
        var isColorTarget =
            TargetProfileSelector.SelectedItem is MacTargetProfile profile &&
            profile.ColorMode == DisplayColorMode.Color;
        var color = isColorTarget
            ? CreateGraphicColorBrush(model, "color", Colors.Black)
            : Brushes.Black;
        var backgroundColor = isColorTarget
            ? CreateGraphicColorBrush(model, "backgroundColor", Colors.White)
            : Brushes.White;
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = color,
            StrokeThickness = 1,
            Fill = isDithered
                ? CreateGraphicDitherBrush(color, backgroundColor)
                : isFilled ? color : null,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(1),
        };
        return new Grid
        {
            Background = Brushes.White,
            Children = { path },
        };
    }

    private static Brush CreateGraphicColorBrush(
        DesignerControl model,
        string propertyId,
        Color fallback)
    {
        if (!ClassicMacRgbColor.TryParse(
                model.Properties.GetValueOrDefault(propertyId),
                out var color))
        {
            color = new ClassicMacRgbColor(fallback.R, fallback.G, fallback.B);
        }

        return new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
    }

    private static Geometry CreateGraphicLineGeometry(DesignerControl model)
    {
        var width = Math.Max(1, model.Width - 2);
        var height = Math.Max(1, model.Height - 2);
        var startX = ParseInteger(model.Properties.GetValueOrDefault("startX"), 0);
        var startY = ParseInteger(model.Properties.GetValueOrDefault("startY"), 0);
        var endX = ParseInteger(model.Properties.GetValueOrDefault("endX"), 100);
        var endY = ParseInteger(model.Properties.GetValueOrDefault("endY"), 100);
        return new LineGeometry(
            new Point(width * startX / 100, height * startY / 100),
            new Point(width * endX / 100, height * endY / 100));
    }

    private void PositionDesignerGrips(DesignerControl model)
    {
        if (!_gripCanvases.TryGetValue(model.Id, out var canvas))
        {
            return;
        }

        var width = Math.Max(0, model.Width - 2);
        var height = Math.Max(0, model.Height - 2);
        const double halfGrip = 4;
        var horizontalCenters = new Dictionary<ResizeGripDirection, double>
        {
            [ResizeGripDirection.NorthWest] = 0,
            [ResizeGripDirection.West] = 0,
            [ResizeGripDirection.SouthWest] = 0,
            [ResizeGripDirection.North] = width / 2,
            [ResizeGripDirection.South] = width / 2,
            [ResizeGripDirection.NorthEast] = width,
            [ResizeGripDirection.East] = width,
            [ResizeGripDirection.SouthEast] = width,
        };
        var verticalCenters = new Dictionary<ResizeGripDirection, double>
        {
            [ResizeGripDirection.NorthWest] = 0,
            [ResizeGripDirection.North] = 0,
            [ResizeGripDirection.NorthEast] = 0,
            [ResizeGripDirection.West] = height / 2,
            [ResizeGripDirection.East] = height / 2,
            [ResizeGripDirection.SouthWest] = height,
            [ResizeGripDirection.South] = height,
            [ResizeGripDirection.SouthEast] = height,
        };
        if (_resizeGrips.TryGetValue(model.Id, out var resizeGrips))
        {
            foreach (var (direction, grip) in resizeGrips)
            {
                Canvas.SetLeft(grip, horizontalCenters[direction] - halfGrip);
                Canvas.SetTop(grip, verticalCenters[direction] - halfGrip);
            }
        }

        if (_lineEndpointGrips.TryGetValue(model.Id, out var endpoints))
        {
            PositionLineEndpointGrip(model, canvas, endpoints.Start, true);
            PositionLineEndpointGrip(model, canvas, endpoints.End, false);
        }
    }

    private static void PositionLineEndpointGrip(
        DesignerControl model,
        Canvas canvas,
        Thumb grip,
        bool isStart)
    {
        var xProperty = isStart ? "startX" : "endX";
        var yProperty = isStart ? "startY" : "endY";
        var x = ParseInteger(model.Properties.GetValueOrDefault(xProperty), isStart ? 0 : 100);
        var y = ParseInteger(model.Properties.GetValueOrDefault(yProperty), isStart ? 0 : 100);
        Canvas.SetLeft(grip, canvas.Width * x / 100 - grip.Width / 2);
        Canvas.SetTop(grip, canvas.Height * y / 100 - grip.Height / 2);
    }

    private void RefreshGraphicLineView(DesignerControl model)
    {
        if (_graphicLinePaths.TryGetValue(model.Id, out var path))
        {
            path.Data = CreateGraphicLineGeometry(model);
        }

        PositionDesignerGrips(model);
    }

    private static Geometry CreateGraphicArcGeometry(DesignerControl model)
    {
        var width = Math.Max(2, model.Width - 2);
        var height = Math.Max(2, model.Height - 2);
        var center = new Point(width / 2 + 1, height / 2 + 1);
        var radiusX = Math.Max(1, width / 2);
        var radiusY = Math.Max(1, height / 2);
        var startAngle = ParseInteger(model.Properties.GetValueOrDefault("startAngle"), 0);
        var sweepAngle = ParseInteger(model.Properties.GetValueOrDefault("sweepAngle"), 180);
        var startRadians = startAngle * Math.PI / 180;
        var endRadians = (startAngle + sweepAngle) * Math.PI / 180;
        var start = new Point(
            center.X + radiusX * Math.Cos(startRadians),
            center.Y + radiusY * Math.Sin(startRadians));
        var end = new Point(
            center.X + radiusX * Math.Cos(endRadians),
            center.Y + radiusY * Math.Sin(endRadians));
        var filled = model.WidgetTypeId.EndsWith(
                "-filled",
                StringComparison.Ordinal) ||
            model.WidgetTypeId.EndsWith("-dithered", StringComparison.Ordinal);
        var figure = new PathFigure
        {
            StartPoint = filled ? center : start,
            IsClosed = filled,
            IsFilled = filled,
        };
        if (filled)
        {
            figure.Segments.Add(new LineSegment(start, isStroked: false));
        }

        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radiusX, radiusY),
            0,
            sweepAngle > 180,
            SweepDirection.Clockwise,
            isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Brush CreateGraphicDitherBrush(Brush foreground, Brush background)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(
            background,
            null,
            new RectangleGeometry(new Rect(0, 0, 2, 2))));
        drawing.Children.Add(new GeometryDrawing(
            foreground,
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));
        drawing.Children.Add(new GeometryDrawing(
            foreground,
            null,
            new RectangleGeometry(new Rect(1, 1, 1, 1))));
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 2, 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 2, 2),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
    }

    private static FrameworkElement CreateScrollbarArrow(bool up)
    {
        var button = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = new Polygon
            {
                Points = up
                    ? [new Point(8, 3), new Point(3, 10), new Point(13, 10)]
                    : [new Point(3, 4), new Point(13, 4), new Point(8, 11)],
                Fill = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        return button;
    }

    private void AttachWidgetInput(string controlId, Border view)
    {
        view.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, args) =>
        {
            var model = FindControl(controlId);
            if (model is null || DesignerToolSelector.SelectedIndex == 1)
            {
                return;
            }

            if (_resizeHandles.TryGetValue(controlId, out var handles) &&
                handles.Any(handle =>
                    handle.Visibility == Visibility.Visible && handle.IsMouseOver))
            {
                return;
            }

            var additiveSelection =
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            SelectControl(controlId, additiveSelection);
            if (!_selectedControlIds.Contains(controlId))
            {
                args.Handled = true;
                return;
            }

            _dragStart = args.GetPosition(FormCanvas);
            if (DesignerToolSelector.SelectedIndex == 2)
            {
                _selectedControlIds.Clear();
                _selectedControlIds.Add(controlId);
                _selectedControlId = controlId;
                UpdateSelectionVisuals();
                RefreshPropertyInspector(model);
                _resizingControlId = controlId;
                _dragStartWidth = model.Width;
                _dragStartHeight = model.Height;
            }
            else
            {
                _dragOrigins.Clear();
                foreach (var selectedId in _selectedControlIds)
                {
                    if (FindControl(selectedId) is DesignerControl selected)
                    {
                        _dragOrigins[selectedId] = (selected.X, selected.Y);
                    }
                }
            }

            view.CaptureMouse();
            args.Handled = true;
        }),
            handledEventsToo: true);
        view.MouseMove += (_, args) =>
        {
            if (!view.IsMouseCaptured)
            {
                return;
            }

            var position = args.GetPosition(FormCanvas);
            if (_resizingControlId == controlId)
            {
                SetControlSize(
                    controlId,
                    _dragStartWidth + position.X - _dragStart.X,
                    _dragStartHeight + position.Y - _dragStart.Y);
                return;
            }

            MoveSelection(position);
        };
        view.MouseLeftButtonUp += (_, args) =>
        {
            if (view.IsMouseCaptured &&
                (_selectedControlIds.Contains(controlId) ||
                 _resizingControlId == controlId))
            {
                _resizingControlId = null;
                _dragOrigins.Clear();
                view.ReleaseMouseCapture();
                args.Handled = true;
            }
        };
    }

    private void MoveSelection(Point position)
    {
        if (_selectedControlId is null || _dragOrigins.Count == 0)
        {
            return;
        }

        var anchor = FindControl(_selectedControlId);
        if (anchor is null)
        {
            return;
        }

        var deltaX = position.X - _dragStart.X;
        var deltaY = position.Y - _dragStart.Y;
        if (SnapToGridToggle.IsChecked == true)
        {
            if (!TryGetGridSpacing(out var spacing))
            {
                return;
            }

            var origin = _dragOrigins[_selectedControlId];
            deltaX = DesignerGeometry.Snap(origin.X + deltaX, spacing) - origin.X;
            deltaY = DesignerGeometry.Snap(origin.Y + deltaY, spacing) - origin.Y;
        }

        var selected = _dragOrigins
            .Select(origin => (Model: FindControl(origin.Key), origin.Value))
            .Where(item => item.Model is not null)
            .Select(item => (Model: item.Model!, item.Value))
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        deltaX = Math.Clamp(
            deltaX,
            -selected.Min(item => item.Value.X),
            FormCanvas.Width - selected.Max(item => item.Value.X + item.Model.Width));
        deltaY = Math.Clamp(
            deltaY,
            -selected.Min(item => item.Value.Y),
            FormCanvas.Height - selected.Max(item => item.Value.Y + item.Model.Height));
        foreach (var item in selected)
        {
            var updated = item.Model.WithGeometry(
                item.Value.X + deltaX,
                item.Value.Y + deltaY,
                item.Model.Width,
                item.Model.Height);
            UpdateControlModel(updated);
            var view = _controls.First(control => control.Model.Id == updated.Id).View;
            Canvas.SetLeft(view, updated.X);
            Canvas.SetTop(view, updated.Y);
        }

        if (deltaX != 0 || deltaY != 0)
        {
            MarkProjectDirty();
        }

        RefreshPropertyInspector(FindControl(_selectedControlId));
    }

    private void ResizeControl(string controlId, double horizontalChange, double verticalChange)
    {
        if (FindControl(controlId) is not DesignerControl model)
        {
            return;
        }

        SetControlSize(
            controlId,
            model.Width + horizontalChange,
            model.Height + verticalChange);
    }

    private void ResizeFromGrip(
        string controlId,
        ResizeGripDirection direction,
        double horizontalChange,
        double verticalChange)
    {
        if (FindControl(controlId) is not DesignerControl model)
        {
            return;
        }

        var definition = _widgetRegistry.GetRequired(model.WidgetTypeId);
        var left = model.X;
        var top = model.Y;
        var right = model.X + model.Width;
        var bottom = model.Y + model.Height;
        var spacing = 0;
        if (SnapToGridToggle.IsChecked == true && !TryGetGridSpacing(out spacing))
        {
            return;
        }

        double SnapEdge(double edge) =>
            spacing > 0 ? DesignerGeometry.Snap(edge, spacing) : edge;

        if (direction is ResizeGripDirection.NorthWest or
            ResizeGripDirection.West or
            ResizeGripDirection.SouthWest)
        {
            left = Math.Clamp(
                SnapEdge(left + horizontalChange),
                0,
                right - definition.MinimumWidth);
        }
        else if (direction is ResizeGripDirection.NorthEast or
            ResizeGripDirection.East or
            ResizeGripDirection.SouthEast)
        {
            right = Math.Clamp(
                SnapEdge(right + horizontalChange),
                left + definition.MinimumWidth,
                FormCanvas.Width);
        }

        if (direction is ResizeGripDirection.NorthWest or
            ResizeGripDirection.North or
            ResizeGripDirection.NorthEast)
        {
            top = Math.Clamp(
                SnapEdge(top + verticalChange),
                0,
                bottom - definition.MinimumHeight);
        }
        else if (direction is ResizeGripDirection.SouthWest or
            ResizeGripDirection.South or
            ResizeGripDirection.SouthEast)
        {
            bottom = Math.Clamp(
                SnapEdge(bottom + verticalChange),
                top + definition.MinimumHeight,
                FormCanvas.Height);
        }

        SetControlGeometry(model, left, top, right - left, bottom - top);
    }

    private void MoveLineEndpoint(
        string controlId,
        bool isStart,
        double horizontalChange,
        double verticalChange)
    {
        if (FindControl(controlId) is not DesignerControl { WidgetTypeId: "graphic-line" } model)
        {
            return;
        }

        var start = new Point(
            model.X + model.Width * ParseInteger(model.Properties.GetValueOrDefault("startX"), 0) / 100,
            model.Y + model.Height * ParseInteger(model.Properties.GetValueOrDefault("startY"), 0) / 100);
        var end = new Point(
            model.X + model.Width * ParseInteger(model.Properties.GetValueOrDefault("endX"), 100) / 100,
            model.Y + model.Height * ParseInteger(model.Properties.GetValueOrDefault("endY"), 100) / 100);
        var moved = isStart ? start : end;
        moved.Offset(horizontalChange, verticalChange);
        if (SnapToGridToggle.IsChecked == true)
        {
            if (!TryGetGridSpacing(out var spacing))
            {
                return;
            }

            moved.X = DesignerGeometry.Snap(moved.X, spacing);
            moved.Y = DesignerGeometry.Snap(moved.Y, spacing);
        }

        moved.X = Math.Clamp(moved.X, 0, FormCanvas.Width);
        moved.Y = Math.Clamp(moved.Y, 0, FormCanvas.Height);
        if (isStart)
        {
            start = moved;
        }
        else
        {
            end = moved;
        }

        var definition = _widgetRegistry.GetRequired(model.WidgetTypeId);
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        EnsureMinimumSpan(ref left, ref right, definition.MinimumWidth, FormCanvas.Width);
        EnsureMinimumSpan(ref top, ref bottom, definition.MinimumHeight, FormCanvas.Height);
        var width = right - left;
        var height = bottom - top;
        var updated = model.WithGeometry(left, top, width, height)
            .WithProperty("startX", ToPercent(start.X - left, width))
            .WithProperty("startY", ToPercent(start.Y - top, height))
            .WithProperty("endX", ToPercent(end.X - left, width))
            .WithProperty("endY", ToPercent(end.Y - top, height));
        SetControlGeometry(updated, left, top, width, height);
    }

    private static void EnsureMinimumSpan(
        ref double leading,
        ref double trailing,
        double minimum,
        double surfaceSize)
    {
        if (trailing - leading >= minimum)
        {
            return;
        }

        var center = (leading + trailing) / 2;
        leading = Math.Clamp(center - minimum / 2, 0, surfaceSize - minimum);
        trailing = leading + minimum;
    }

    private static string ToPercent(double offset, double span) =>
        Math.Clamp(
            (int)Math.Round(offset * 100 / span, MidpointRounding.AwayFromZero),
            0,
            100).ToString(CultureInfo.InvariantCulture);

    private void SetControlSize(string controlId, double width, double height)
    {
        if (FindControl(controlId) is not DesignerControl model)
        {
            return;
        }

        var definition = _widgetRegistry.GetRequired(model.WidgetTypeId);
        if (SnapToGridToggle.IsChecked == true)
        {
            if (!TryGetGridSpacing(out var spacing))
            {
                return;
            }

            width = DesignerGeometry.Snap(width, spacing);
            height = DesignerGeometry.Snap(height, spacing);
        }

        width = Math.Clamp(
            width,
            definition.MinimumWidth,
            Math.Max(definition.MinimumWidth, FormCanvas.Width - model.X));
        height = Math.Clamp(
            height,
            definition.MinimumHeight,
            Math.Max(definition.MinimumHeight, FormCanvas.Height - model.Y));
        SetControlGeometry(model, model.X, model.Y, width, height);
    }

    private void SetControlGeometry(
        DesignerControl model,
        double x,
        double y,
        double width,
        double height)
    {
        var updated = model.WithGeometry(x, y, width, height);
        _widgetRegistry.ValidateControl(updated);
        if (!updated.FitsWithin(FormCanvas.Width, FormCanvas.Height))
        {
            return;
        }

        UpdateControlModel(updated);
        var view = _controls.First(control => control.Model.Id == model.Id).View;
        view.Width = width;
        view.Height = height;
        Canvas.SetLeft(view, x);
        Canvas.SetTop(view, y);
        if (_gripCanvases.TryGetValue(model.Id, out var canvas))
        {
            canvas.Width = Math.Max(0, width - 2);
            canvas.Height = Math.Max(0, height - 2);
        }
        RefreshGraphicLineView(updated);
        MarkProjectDirty();
        RefreshPropertyInspector(updated);
    }

    private void SelectControl(string controlId, bool toggleSelection = false)
    {
        _selectedNonVisualComponentId = null;
        NonVisualComponentTray.SelectedItem = null;
        if (toggleSelection && _selectedControlIds.Contains(controlId))
        {
            _selectedControlIds.Remove(controlId);
            if (_selectedControlId == controlId)
            {
                _selectedControlId = _selectedControlIds.LastOrDefault();
            }
        }
        else if (toggleSelection)
        {
            _selectedControlIds.Add(controlId);
            _selectedControlId = controlId;
        }
        else
        {
            _selectedControlIds.Clear();
            _selectedControlIds.Add(controlId);
            _selectedControlId = controlId;
        }

        UpdateSelectionVisuals();
        RefreshPropertyInspector(
            _selectedControlId is null ? null : FindControl(_selectedControlId));
    }

    private void ClearSelection()
    {
        _selectedControlIds.Clear();
        _selectedControlId = null;
        _selectedNonVisualComponentId = null;
        NonVisualComponentTray.SelectedItem = null;
        UpdateSelectionVisuals();
        RefreshPropertyInspector(null);
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var (model, view) in _controls)
        {
            var selected = _selectedControlIds.Contains(model.Id);
            var showGrips = selected && _selectedControlIds.Count == 1;
            view.BorderBrush = selected ? Brushes.Black : Brushes.Transparent;
            view.BorderThickness = new Thickness(selected ? 2 : 1);
            if (_resizeHandles.TryGetValue(model.Id, out var handles))
            {
                foreach (var handle in handles)
                {
                    handle.Visibility = showGrips ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        var canAlign = _selectedControlIds.Count > 1;
        AlignLeftButton.IsEnabled = canAlign;
        AlignRightButton.IsEnabled = canAlign;
        AlignTopButton.IsEnabled = canAlign;
        AlignBottomButton.IsEnabled = canAlign;
    }

    private void AlignControls_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string alignmentText } ||
            !Enum.TryParse<DesignerControlAlignment>(
                alignmentText,
                ignoreCase: true,
                out var alignment))
        {
            return;
        }

        var selected = _selectedControlIds
            .Select(FindControl)
            .Where(control => control is not null)
            .Cast<DesignerControl>()
            .ToArray();
        if (selected.Length < 2)
        {
            return;
        }

        var aligned = DesignerGeometry.Align(selected, alignment);
        if (aligned.Any(control =>
                !control.FitsWithin(FormCanvas.Width, FormCanvas.Height)))
        {
            StatusText.Text = "Alignment would place a widget outside the form.";
            return;
        }

        foreach (var model in aligned)
        {
            UpdateControlModel(model);
            var view = _controls.First(control => control.Model.Id == model.Id).View;
            Canvas.SetLeft(view, model.X);
            Canvas.SetTop(view, model.Y);
        }

        MarkProjectDirty();
        RefreshPropertyInspector(
            _selectedControlId is null ? null : FindControl(_selectedControlId));
    }

    private bool TryGetGridSpacing(out int spacing)
    {
        if (GridSpacingSelector is not null &&
            int.TryParse(
                GridSpacingSelector.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out spacing) &&
            spacing is >= 2 and <= 64)
        {
            return true;
        }

        spacing = 0;
        if (StatusText is not null)
        {
            StatusText.Text = "Grid spacing must be an integer from 2 to 64 pixels.";
        }

        return false;
    }

    private void RefreshPropertyInspector(DesignerControl? model)
    {
        _updatingProperties = true;
        var enabled = model is not null && _selectedControlIds.Count == 1;
        InspectorTabs.SelectedIndex = 0;
        ControlTextLabel.Visibility = Visibility.Visible;
        ControlTextEditor.Visibility = Visibility.Visible;
        ControlGeometryEditors.Visibility = Visibility.Visible;
        FormPropertyEditors.Visibility = Visibility.Collapsed;
        WidgetPropertiesLabel.Text = "Widget properties";
        ControlTextEditor.IsEnabled = enabled;
        ControlXEditor.IsEnabled = enabled;
        ControlYEditor.IsEnabled = enabled;
        ControlWidthEditor.IsEnabled = enabled;
        ControlHeightEditor.IsEnabled = enabled;
        WidgetPropertyEditors.Children.Clear();
        if (model is null)
        {
            ControlTextLabel.Visibility = Visibility.Collapsed;
            ControlTextEditor.Visibility = Visibility.Collapsed;
            ControlGeometryEditors.Visibility = Visibility.Collapsed;
            RefreshEventInspector(
                "The form has no events.",
                [],
                new Dictionary<string, string>(StringComparer.Ordinal));
            ControlIdText.Text = "Form";
            ControlTextEditor.Text = string.Empty;
            ControlXEditor.Text = string.Empty;
            ControlYEditor.Text = string.Empty;
            ControlWidthEditor.Text = string.Empty;
            ControlHeightEditor.Text = string.Empty;
            WidgetPropertiesLabel.Text = "Form properties";
            FormPropertyEditors.Visibility = Visibility.Visible;
            RefreshFormPropertiesInspector();
            _updatingProperties = false;
            return;
        }

        if (_selectedControlIds.Count > 1)
        {
            RefreshEventInspector(
                $"{_selectedControlIds.Count} widgets selected",
                [],
                new Dictionary<string, string>(StringComparer.Ordinal));
            ControlIdText.Text = $"{_selectedControlIds.Count} widgets selected";
            ControlTextEditor.Text = string.Empty;
            ControlXEditor.Text = string.Empty;
            ControlYEditor.Text = string.Empty;
            ControlWidthEditor.Text = string.Empty;
            ControlHeightEditor.Text = string.Empty;
            EventOwnerText.Text = $"{_selectedControlIds.Count} widgets selected";
            _updatingProperties = false;
            return;
        }

        var definition = _widgetRegistry.GetRequired(model.WidgetTypeId);
        ControlIdText.Text = $"{definition.DisplayName} — {model.Id}";
        ControlTextEditor.Text = model.Text;
        ControlXEditor.Text = model.X.ToString("0.##", CultureInfo.InvariantCulture);
        ControlYEditor.Text = model.Y.ToString("0.##", CultureInfo.InvariantCulture);
        ControlWidthEditor.Text = model.Width.ToString("0.##", CultureInfo.InvariantCulture);
        ControlHeightEditor.Text = model.Height.ToString("0.##", CultureInfo.InvariantCulture);
        var showColorProperties =
            TargetProfileSelector.SelectedItem is MacTargetProfile profile &&
            profile.ColorMode == DisplayColorMode.Color;
        foreach (var property in definition.Properties)
        {
            if (property.EditorKind == DesignerWidgetPropertyEditorKind.Color &&
                !showColorProperties)
            {
                continue;
            }

            WidgetPropertyEditors.Children.Add(
                new TextBlock
                {
                    Text = property.DisplayName,
                    Margin = new Thickness(0, 5, 0, 2),
                    FontWeight = FontWeights.SemiBold,
                });
            if (property.EditorKind == DesignerWidgetPropertyEditorKind.Boolean)
            {
                var editor = new CheckBox
                {
                    IsChecked = bool.TryParse(
                        model.Properties.GetValueOrDefault(property.Id),
                        out var value) && value,
                    Tag = property.Id,
                };
                editor.Checked += WidgetBooleanProperty_Changed;
                editor.Unchecked += WidgetBooleanProperty_Changed;
                WidgetPropertyEditors.Children.Add(editor);
            }
            else
            {
                var editor = new TextBox
                {
                    Text = model.Properties.GetValueOrDefault(property.Id) ??
                        property.DefaultValue,
                    Tag = property.Id,
                    AcceptsReturn =
                        property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText,
                    TextWrapping =
                        property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText
                            ? TextWrapping.Wrap
                            : TextWrapping.NoWrap,
                    ToolTip = property.EditorKind == DesignerWidgetPropertyEditorKind.Color
                        ? "Enter a hexadecimal RGB color such as #3366CC."
                        : null,
                    Height =
                        property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText
                            ? 56
                            : double.NaN,
                };
                editor.LostFocus += WidgetTextProperty_LostFocus;
                WidgetPropertyEditors.Children.Add(editor);
            }
        }

        RefreshEventInspector(
            $"{definition.DisplayName} — {model.Id}",
            definition.Events,
            model.Events);
        _updatingProperties = false;
    }

    private void RefreshNonVisualComponentInspector(
        DesignerNonVisualComponent? component)
    {
        _updatingProperties = true;
        FormPropertyEditors.Visibility = Visibility.Collapsed;
        InspectorTabs.SelectedIndex = 0;
        ControlTextLabel.Visibility = Visibility.Collapsed;
        ControlTextEditor.Visibility = Visibility.Collapsed;
        ControlGeometryEditors.Visibility = Visibility.Collapsed;
        WidgetPropertiesLabel.Text = "Component properties";
        ControlTextEditor.IsEnabled = false;
        ControlXEditor.IsEnabled = false;
        ControlYEditor.IsEnabled = false;
        ControlWidthEditor.IsEnabled = false;
        ControlHeightEditor.IsEnabled = false;
        WidgetPropertyEditors.Children.Clear();
        if (component is null)
        {
            ControlIdText.Text = "None";
            ControlTextEditor.Clear();
            ControlTextEditor.Visibility = Visibility.Visible;
            ControlTextLabel.Visibility = Visibility.Visible;
            ControlGeometryEditors.Visibility = Visibility.Visible;
            RefreshEventInspector(
                "Select a widget or component to inspect its events.",
                [],
                new Dictionary<string, string>(StringComparer.Ordinal));
            _updatingProperties = false;
            return;
        }

        var definition = _componentRegistry.GetRequired(component.ComponentTypeId);
        ControlIdText.Text = $"{definition.DisplayName} — {component.Id}";
        foreach (var property in definition.Properties)
        {
            WidgetPropertyEditors.Children.Add(new TextBlock
            {
                Text = property.DisplayName,
                Margin = new Thickness(0, 5, 0, 2),
                FontWeight = FontWeights.SemiBold,
            });
            if (property.EditorKind == DesignerWidgetPropertyEditorKind.Boolean)
            {
                var editor = new CheckBox
                {
                    IsChecked = bool.TryParse(
                        component.Properties.GetValueOrDefault(property.Id),
                        out var value) && value,
                    Tag = property.Id,
                };
                editor.Checked += NonVisualBooleanProperty_Changed;
                editor.Unchecked += NonVisualBooleanProperty_Changed;
                WidgetPropertyEditors.Children.Add(editor);
            }
            else if (property.Id == "serialPortId")
            {
                var editor = new ComboBox
                {
                    ItemsSource = new[] { string.Empty }.Concat(
                        _nonVisualComponents
                            .Where(item => item.ComponentTypeId == "serial-port")
                            .Select(item => item.Id))
                        .ToArray(),
                    SelectedItem = component.Properties.GetValueOrDefault(property.Id) ??
                        property.DefaultValue,
                    Tag = property.Id,
                };
                editor.SelectionChanged += NonVisualChoiceProperty_Changed;
                WidgetPropertyEditors.Children.Add(editor);
            }
            else if (property.Options is not null)
            {
                var editor = new ComboBox
                {
                    ItemsSource = property.Options,
                    SelectedItem = component.Properties.GetValueOrDefault(property.Id) ??
                        property.DefaultValue,
                    Tag = property.Id,
                };
                editor.SelectionChanged += NonVisualChoiceProperty_Changed;
                WidgetPropertyEditors.Children.Add(editor);
            }
            else
            {
                var editor = new TextBox
                {
                    Text = component.Properties.GetValueOrDefault(property.Id) ??
                        property.DefaultValue,
                    Tag = property.Id,
                    AcceptsReturn =
                        property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText,
                    TextWrapping =
                        property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText
                            ? TextWrapping.Wrap
                            : TextWrapping.NoWrap,
                    Height =
                        property.EditorKind == DesignerWidgetPropertyEditorKind.MultiLineText
                            ? 56
                            : double.NaN,
                };
                editor.LostFocus += NonVisualTextProperty_LostFocus;
                WidgetPropertyEditors.Children.Add(editor);
            }
        }

        RefreshEventInspector(
            $"{definition.DisplayName} — {component.Id}",
            definition.Events,
            component.Events);
        _updatingProperties = false;
    }

    private void RefreshEventInspector(
        string ownerText,
        IReadOnlyList<DesignerWidgetEventDefinition> events,
        IReadOnlyDictionary<string, string> bindings)
    {
        EventEditors.Items.Clear();
        EventEditors.IsEnabled = false;
        EventOwnerText.Text = ownerText;
        EventHintText.Text = string.Empty;
        if (events.Count == 0)
        {
            EventHintText.Text = "This object type does not expose any events.";
            return;
        }

        InspectorTabs.SelectedIndex = 1;
        EventEditors.IsEnabled = _projectFilePath is not null;
        EventHintText.Text = _projectFilePath is null
            ? "Create or open a project to enable code event handlers."
            : "Enter a handler name, then use Go (or double-click the value) to create or open its code.";
        foreach (var eventDefinition in events)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(60),
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(36),
            });
            row.Children.Add(new TextBlock
            {
                Text = eventDefinition.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            var handlerEditor = new TextBox
            {
                Text = bindings.GetValueOrDefault(eventDefinition.Id) ?? string.Empty,
                Tag = eventDefinition.Id,
                MinWidth = 72,
                Margin = new Thickness(0, 0, 4, 0),
            };
            handlerEditor.LostFocus += EventHandlerEditor_LostFocus;
            handlerEditor.MouseDoubleClick += EventHandlerEditor_MouseDoubleClick;
            Grid.SetColumn(handlerEditor, 1);
            row.Children.Add(handlerEditor);
            var openButton = new Button
            {
                Content = "Go",
                Tag = eventDefinition.Id,
                ToolTip = "Create or open the event implementation",
            };
            openButton.Click += OpenEventHandler_Click;
            Grid.SetColumn(openButton, 2);
            row.Children.Add(openButton);
            EventEditors.Items.Add(row);
        }
    }

    private void EventHandlerEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingProperties ||
            sender is not TextBox { Tag: string eventId } editor)
        {
            return;
        }

        var handlerName = editor.Text.Trim();
        if (_selectedNonVisualComponentId is string componentId &&
            FindNonVisualComponent(componentId) is DesignerNonVisualComponent component)
        {
            TryBindNonVisualComponentEventHandler(component, eventId, handlerName);
        }
        else if (_selectedControlId is string controlId &&
                 FindControl(controlId) is DesignerControl control)
        {
            TryBindEventHandler(control, eventId, handlerName);
        }
    }

    private void EventHandlerEditor_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is TextBox { Tag: string eventId })
        {
            OpenEventHandler(eventId);
            e.Handled = true;
        }
    }

    private void OpenEventHandler_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string eventId })
        {
            OpenEventHandler(eventId);
        }
    }

    private void OpenEventHandler(string eventId)
    {
        var component = _selectedNonVisualComponentId is string componentId
            ? FindNonVisualComponent(componentId)
            : null;
        var control = _selectedControlId is string controlId
            ? FindControl(controlId)
            : null;
        if (component is null && control is null)
        {
            return;
        }

        var eventBindings = component?.Events ?? control!.Events;
        var handlerName = eventBindings.GetValueOrDefault(eventId);
        if (string.IsNullOrWhiteSpace(handlerName))
        {
            handlerName = component is not null
                ? CreateDefaultEventHandlerName(component.Id, eventId)
                : CreateDefaultEventHandlerName(control!.Id, eventId);
        }

        var bound = component is not null
            ? TryBindNonVisualComponentEventHandler(component, eventId, handlerName)
            : TryBindEventHandler(control!, eventId, handlerName);
        if (!bound)
        {
            return;
        }

        if (component is not null)
        {
            RefreshNonVisualComponentInspector(
                FindNonVisualComponent(component.Id));
        }
        else
        {
            RefreshPropertyInspector(FindControl(control!.Id));
        }
        var projectDirectory = Path.GetDirectoryName(_projectFilePath!);
        if (projectDirectory is null)
        {
            StatusText.Text = "The project folder is unavailable.";
            return;
        }

        var eventSourcePath = Path.Combine(projectDirectory, "src", "user_events.c");
        if (!ConfirmCodeFileSwitch(eventSourcePath))
        {
            return;
        }

        OpenCodeFile(eventSourcePath);
        if (!string.Equals(
                _openCodeFilePath,
                Path.GetFullPath(eventSourcePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NavigateToEventHandler(handlerName);
    }

    private bool TryBindEventHandler(
        DesignerControl model,
        string eventId,
        string handlerName)
    {
        try
        {
            var previousHandler = model.Events.GetValueOrDefault(eventId);
            if (string.IsNullOrWhiteSpace(handlerName) &&
                string.IsNullOrWhiteSpace(previousHandler))
            {
                return true;
            }

            var updated = model.WithEvent(
                eventId,
                string.IsNullOrWhiteSpace(handlerName) ? null : handlerName);
            _widgetRegistry.ValidateControl(updated);
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                UpdateControlModel(updated);
                MarkProjectDirty();
                StatusText.Text = $"{eventId} handler was cleared.";
                return true;
            }

            if (_projectFilePath is null)
            {
                throw new InvalidOperationException(
                    "Create or open a project before generating an event handler.");
            }

            EnsureEventHandlerImplementation(handlerName);
            if (!string.Equals(previousHandler, handlerName, StringComparison.Ordinal))
            {
                UpdateControlModel(updated);
                MarkProjectDirty();
            }
            StatusText.Text = $"{eventId} is bound to {handlerName}.";
            return true;
        }
        catch (Exception error) when (
            error is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            StatusText.Text = error.Message;
            RefreshPropertyInspector(FindControl(model.Id));
            return false;
        }
    }

    private bool TryBindNonVisualComponentEventHandler(
        DesignerNonVisualComponent model,
        string eventId,
        string handlerName)
    {
        var index = _nonVisualComponents.FindIndex(
            component => component.Id == model.Id);
        if (index < 0)
        {
            return false;
        }

        try
        {
            var previousHandler = model.Events.GetValueOrDefault(eventId);
            if (string.IsNullOrWhiteSpace(handlerName) &&
                string.IsNullOrWhiteSpace(previousHandler))
            {
                return true;
            }

            var updated = model.WithEvent(
                eventId,
                string.IsNullOrWhiteSpace(handlerName) ? null : handlerName);
            _componentRegistry.ValidateComponent(updated);
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                _nonVisualComponents[index] = updated;
                RefreshNonVisualComponentTray();
                RefreshNonVisualComponentInspector(updated);
                MarkProjectDirty();
                StatusText.Text = $"{eventId} handler was cleared.";
                return true;
            }

            if (_projectFilePath is null)
            {
                throw new InvalidOperationException(
                    "Create or open a project before generating an event handler.");
            }

            EnsureEventHandlerImplementation(handlerName);
            if (!string.Equals(previousHandler, handlerName, StringComparison.Ordinal))
            {
                _nonVisualComponents[index] = updated;
                RefreshNonVisualComponentTray();
                RefreshNonVisualComponentInspector(updated);
                MarkProjectDirty();
            }
            StatusText.Text = $"{eventId} is bound to {handlerName}.";
            return true;
        }
        catch (Exception error) when (
            error is ArgumentException or
                InvalidDataException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            StatusText.Text = error.Message;
            RefreshNonVisualComponentInspector(
                FindNonVisualComponent(model.Id));
            return false;
        }
    }

    private string CreateDefaultEventHandlerName(
        string componentId,
        string eventId)
    {
        var name = new StringBuilder();
        var capitalizeNext = true;
        foreach (var character in componentId)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                capitalizeNext = true;
                continue;
            }

            name.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
            capitalizeNext = false;
        }

        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
        {
            name.Insert(0, "Component");
        }

        var suffix = eventId.StartsWith("On", StringComparison.Ordinal)
            ? eventId[2..]
            : eventId;
        if (suffix.Length >= 31)
        {
            throw new InvalidDataException(
                $"Event '{eventId}' cannot fit in a Macintosh C handler name.");
        }

        var prefix = name.ToString();
        var candidateIndex = 1;
        while (true)
        {
            var collisionSuffix = candidateIndex == 1
                ? string.Empty
                : candidateIndex.ToString(CultureInfo.InvariantCulture);
            var prefixLength = Math.Max(1, 31 - suffix.Length - collisionSuffix.Length);
            var candidate = prefix[..Math.Min(prefix.Length, prefixLength)] +
                collisionSuffix + suffix;
            if (!GetBoundEventHandlerNames().Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }

            candidateIndex++;
        }
    }

    private IEnumerable<string> GetBoundEventHandlerNames() =>
        _controls.SelectMany(control => control.Model.Events.Values)
            .Concat(_nonVisualComponents.SelectMany(component => component.Events.Values));

    private void EnsureEventHandlerImplementation(string handlerName)
    {
        var projectDirectory = Path.GetDirectoryName(_projectFilePath!);
        if (projectDirectory is null)
        {
            throw new InvalidOperationException("The project folder is unavailable.");
        }

        var sourceDirectory = Path.Combine(projectDirectory, "src");
        EnsureUserSourceFiles(sourceDirectory, MacFormCodeGenerator.CreateMainSource());
        var sourcePath = Path.Combine(sourceDirectory, "user_events.c");
        var source = string.Equals(
                _openCodeFilePath,
                Path.GetFullPath(sourcePath),
                StringComparison.OrdinalIgnoreCase)
            ? CodeEditor.Text
            : File.ReadAllText(sourcePath);
        var updatedSource = DesignerEventCodeSynchronizer.EnsureHandlerImplementation(
            source,
            handlerName,
            _formTypeName);
        if (!string.Equals(source, updatedSource, StringComparison.Ordinal))
        {
            File.WriteAllText(
                sourcePath,
                updatedSource,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (string.Equals(
                    _openCodeFilePath,
                    Path.GetFullPath(sourcePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                _loadingCodeFile = true;
                CodeEditor.Text = updatedSource;
                _loadingCodeFile = false;
                _codeFileDirty = false;
                UpdateCodeFileLabel();
                UpdateWindowTitle();
            }
        }

        RefreshProjectTree();
    }

    private void UpdateBoundEventHandlerFormTypes(string oldFormTypeName, string newFormTypeName)
    {
        var handlerNames = _controls
            .SelectMany(control => control.Model.Events.Values)
            .Concat(_nonVisualComponents.SelectMany(component => component.Events.Values))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        if (handlerNames.Count == 0 || _projectFilePath is null)
        {
            return;
        }

        var projectDirectory = Path.GetDirectoryName(_projectFilePath);
        if (projectDirectory is null)
        {
            throw new InvalidDataException("The project folder is unavailable.");
        }

        var sourcePath = Path.Combine(projectDirectory, "src", "user_events.c");
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var source = string.Equals(
                _openCodeFilePath,
                fullSourcePath,
                StringComparison.OrdinalIgnoreCase)
            ? CodeEditor.Text
            : File.ReadAllText(fullSourcePath);
        var updated = DesignerEventCodeSynchronizer.UpdateHandlerFormTypes(
            source,
            handlerNames,
            oldFormTypeName,
            newFormTypeName);
        if (string.Equals(source, updated, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(
            fullSourcePath,
            updated,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (string.Equals(
                _openCodeFilePath,
                fullSourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            _loadingCodeFile = true;
            CodeEditor.Text = updated;
            _loadingCodeFile = false;
            _codeFileDirty = false;
            UpdateCodeFileLabel();
            UpdateWindowTitle();
        }
    }

    private void NavigateToEventHandler(string handlerName)
    {
        var declaration = CFunctionDeclarationPattern()
            .Matches(CodeEditor.Text)
            .Cast<Match>()
            .FirstOrDefault(match =>
                string.Equals(
                    match.Groups["name"].Value,
                    handlerName,
                    StringComparison.Ordinal) &&
                match.Groups["terminator"].Value == "{");
        if (declaration is null)
        {
            StatusText.Text = $"Could not locate the implementation for {handlerName}.";
            return;
        }

        var line = CodeEditor.Document.GetLineByOffset(declaration.Index);
        CodeEditor.Select(line.Offset, line.Length);
        CodeEditor.ScrollToLine(line.LineNumber);
        WorkspaceTabs.SelectedIndex = 1;
        CodeEditor.Focus();
    }

    private static Regex CFunctionDeclarationPattern() =>
        new(
            @"(?m)^[ \t]*(?<storage>static[ \t]+)?(?<returnType>[A-Za-z_]\w*)[ \t]+" +
            @"(?<name>[A-Za-z_]\w*)[ \t]*\((?<parameters>[^)]*)\)\s*(?<terminator>[;{])",
            RegexOptions.CultureInvariant);

    private void CommonProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingProperties || _selectedControlId is null)
        {
            return;
        }

        var model = FindControl(_selectedControlId);
        if (model is null)
        {
            return;
        }

        if (!double.TryParse(ControlXEditor.Text, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(ControlYEditor.Text, CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(ControlWidthEditor.Text, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(ControlHeightEditor.Text, CultureInfo.InvariantCulture, out var height))
        {
            StatusText.Text = "Position and size values must be valid numbers.";
            RefreshPropertyInspector(model);
            return;
        }

        try
        {
            if (SnapToGridToggle.IsChecked == true)
            {
                if (!TryGetGridSpacing(out var spacing))
                {
                    RefreshPropertyInspector(model);
                    return;
                }

                x = DesignerGeometry.Snap(x, spacing);
                y = DesignerGeometry.Snap(y, spacing);
                width = Math.Max(1, DesignerGeometry.Snap(width, spacing));
                height = Math.Max(1, DesignerGeometry.Snap(height, spacing));
            }

            var updated = model.WithText(ControlTextEditor.Text)
                .WithGeometry(x, y, width, height);
            _widgetRegistry.ValidateControl(updated);
            if (!updated.FitsWithin(FormCanvas.Width, FormCanvas.Height))
            {
                throw new ArgumentException("The widget must fit inside the target form.");
            }

            UpdateControlModel(updated);
            ReplaceWidgetView(updated);
            MarkProjectDirty();
        }
        catch (ArgumentException error)
        {
            StatusText.Text = error.Message;
            RefreshPropertyInspector(model);
        }
    }

    private void WidgetBooleanProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingProperties ||
            _selectedControlId is null ||
            sender is not CheckBox editor ||
            editor.Tag is not string propertyId)
        {
            return;
        }

        UpdateWidgetProperty(propertyId, editor.IsChecked == true ? "true" : "false");
    }

    private void WidgetTextProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_updatingProperties &&
            _selectedControlId is not null &&
            sender is TextBox editor &&
            editor.Tag is string propertyId)
        {
            UpdateWidgetProperty(propertyId, editor.Text);
        }
    }

    private void NonVisualBooleanProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingProperties ||
            sender is not CheckBox editor ||
            editor.Tag is not string propertyId)
        {
            return;
        }

        UpdateNonVisualComponentProperty(
            propertyId,
            editor.IsChecked == true ? "true" : "false");
    }

    private void NonVisualChoiceProperty_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingProperties ||
            sender is not ComboBox { SelectedItem: string value } editor ||
            editor.Tag is not string propertyId)
        {
            return;
        }

        UpdateNonVisualComponentProperty(propertyId, value);
    }

    private void NonVisualTextProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_updatingProperties &&
            sender is TextBox editor &&
            editor.Tag is string propertyId)
        {
            UpdateNonVisualComponentProperty(propertyId, editor.Text);
        }
    }

    private void UpdateNonVisualComponentProperty(string propertyId, string value)
    {
        if (_selectedNonVisualComponentId is null)
        {
            return;
        }

        var index = _nonVisualComponents.FindIndex(
            component => component.Id == _selectedNonVisualComponentId);
        if (index < 0)
        {
            return;
        }

        try
        {
            var updated = _nonVisualComponents[index].WithProperty(propertyId, value);
            _componentRegistry.ValidateComponent(updated);
            if (updated.ComponentTypeId == "protocol-transfer" &&
                propertyId == "serialPortId" &&
                !string.IsNullOrWhiteSpace(value) &&
                !_nonVisualComponents.Any(component =>
                    component.ComponentTypeId == "serial-port" &&
                    component.Id == value))
            {
                throw new ArgumentException(
                    $"Serial component '{value}' does not exist.");
            }
            _nonVisualComponents[index] = updated;
            RefreshNonVisualComponentTray();
            RefreshNonVisualComponentInspector(updated);
            RefreshMenuBarPreview();
            MarkProjectDirty();
        }
        catch (ArgumentException error)
        {
            StatusText.Text = error.Message;
            RefreshNonVisualComponentInspector(_nonVisualComponents[index]);
        }
    }

    private void UpdateWidgetProperty(string propertyId, string value)
    {
        if (_selectedControlId is null ||
            FindControl(_selectedControlId) is not DesignerControl model)
        {
            return;
        }

        try
        {
            var updated = model.WithProperty(propertyId, value);
            _widgetRegistry.ValidateControl(updated);
            UpdateControlModel(updated);
            ReplaceWidgetView(updated);
            MarkProjectDirty();
        }
        catch (ArgumentException error)
        {
            StatusText.Text = error.Message;
            RefreshPropertyInspector(model);
        }
    }

    private void UpdateControlModel(DesignerControl updated)
    {
        var index = _controls.FindIndex(control => control.Model.Id == updated.Id);
        if (index >= 0)
        {
            _controls[index] = (updated, _controls[index].View);
        }
    }

    private void ReplaceWidgetView(DesignerControl model)
    {
        var index = _controls.FindIndex(control => control.Model.Id == model.Id);
        if (index < 0)
        {
            return;
        }

        var oldView = _controls[index].View;
        var newView = CreateWidgetView(model, _widgetRegistry.GetRequired(model.WidgetTypeId));
        AttachWidgetInput(model.Id, newView);
        Canvas.SetLeft(newView, model.X);
        Canvas.SetTop(newView, model.Y);
        newView.BorderBrush = Brushes.Black;
        newView.BorderThickness = new Thickness(2);
        var childIndex = FormCanvas.Children.IndexOf(oldView);
        FormCanvas.Children.RemoveAt(childIndex);
        FormCanvas.Children.Insert(childIndex, newView);
        _controls[index] = (model, newView);
        UpdateSelectionVisuals();
    }

    private DesignerControl? FindControl(string controlId) =>
        _controls.FirstOrDefault(control => control.Model.Id == controlId).Model;

    private DesignerNonVisualComponent? FindNonVisualComponent(string componentId) =>
        _nonVisualComponents.FirstOrDefault(component => component.Id == componentId);

    private Point? FindAvailableLocation(double width, double height)
    {
        const double inset = 8;
        const double spacing = 8;
        for (var y = inset; y + height <= FormCanvas.Height; y += spacing)
        {
            for (var x = inset; x + width <= FormCanvas.Width; x += spacing)
            {
                var overlaps = _controls.Any(control =>
                    x < control.Model.X + control.Model.Width + spacing &&
                    x + width + spacing > control.Model.X &&
                    y < control.Model.Y + control.Model.Height + spacing &&
                    y + height + spacing > control.Model.Y);
                if (!overlaps)
                {
                    return new Point(x, y);
                }
            }
        }

        return null;
    }

    private static List<string> ReadLines(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static int ParseInteger(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private async void BuildRunButton_Click(object sender, RoutedEventArgs e) =>
        await BuildFormAsync(runTests: false);

    private async void BuildTestButton_Click(object sender, RoutedEventArgs e) =>
        await BuildFormAsync(runTests: true);

    private void MiniVmacMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _selectedEmulator = MacEmulator.MiniVmac;
        MiniVmacMenuItem.IsChecked = true;
        BasiliskIIMenuItem.IsChecked = false;
        UpdateBuildRunAvailability();
        StatusText.Text = "Mini vMac is selected; Build & Run/Test is limited to the Mac SE profile.";
    }

    private void BasiliskIIMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _selectedEmulator = MacEmulator.BasiliskII;
        MiniVmacMenuItem.IsChecked = false;
        BasiliskIIMenuItem.IsChecked = true;
        UpdateBuildRunAvailability();
        StatusText.Text =
            "Basilisk II is selected; Build & Run supports the Color Classic and PowerBook 165c profiles.";
    }

    private async Task BuildFormAsync(bool runTests)
    {
        if (TargetProfileSelector.SelectedItem is not MacTargetProfile profile ||
            !MacBuildRunCommand.IsSupportedCombination(profile, _selectedEmulator))
        {
            StatusText.Text = _selectedEmulator == MacEmulator.MiniVmac
                ? "Mini vMac is configured only for the Mac SE profile."
                : "Basilisk II is configured for the Color Classic and PowerBook 165c profiles.";
            return;
        }
        if (runTests && _selectedEmulator != MacEmulator.MiniVmac)
        {
            StatusText.Text = "Build & Test is supported only with Mini vMac and the Mac SE profile.";
            return;
        }
        if (_controls.Count == 0)
        {
            StatusText.Text = "Add at least one widget to the form before building it.";
            return;
        }
        if (_codeFileDirty && !SaveCurrentCodeFile())
        {
            return;
        }
        if (_projectDirty && _projectFilePath is not null && !SaveCurrentProject())
        {
            return;
        }

        BuildRunButton.IsEnabled = false;
        BuildTestButton.IsEnabled = false;
        StopEmulatorButton.IsEnabled = false;
        _emulatorProcessId = null;
        _emulatorProcessName = null;
        BuildOutput.Clear();
        SetBuildOutputVisible(true);
        StatusText.Text = "Starting the Retro68 build...";
        try
        {
            var workspaceRoot = FindWorkspaceRoot();
            var scriptPath = Path.Combine(
                workspaceRoot,
                "scripts",
                "windows",
                "build-and-run-mac-se.ps1");
            var temporaryProjectDirectory = Path.Combine(
                workspaceRoot,
                ".local-tools",
                "designer-form");
            var projectRoot = _projectFilePath is null
                ? null
                : Path.GetDirectoryName(_projectFilePath);
            var sourceDirectory = projectRoot is null
                ? Path.Combine(temporaryProjectDirectory, "source")
                : Path.Combine(projectRoot, "src");
            var buildDirectory = projectRoot is null
                ? Path.Combine(temporaryProjectDirectory, "build")
                : Path.Combine(projectRoot, ".build");
            var generatedForm = MacFormCodeGenerator.Generate(
                profile,
                "DesignerForm",
                FormTitle.Text,
                _formTypeName,
                _controls.Select(control => control.Model).ToArray(),
                _widgetRegistry,
                _nonVisualComponents,
                _componentRegistry,
                _formProperties);
            string? testRequestPath = null;
            string? telemetryPath = null;
            if (runTests)
            {
                var bridgeDirectory = Path.Combine(
                    temporaryProjectDirectory,
                    "bridge");
                Directory.CreateDirectory(bridgeDirectory);
                var runId = Guid.NewGuid().ToString("N");
                testRequestPath = Path.Combine(bridgeDirectory, $"request-{runId}.txt");
                telemetryPath = Path.Combine(bridgeDirectory, $"telemetry-{runId}.txt");
                var commands = new List<MacTestCommand>
                {
                    new(MacTestCommandKind.QueryForm),
                };
                commands.AddRange(_controls
                    .Where(control =>
                        _widgetRegistry.GetRequired(control.Model.WidgetTypeId).IsInteractive)
                    .Select(control =>
                    new MacTestCommand(
                        MacTestCommandKind.ClickControl,
                        control.Model.Id)));
                commands.Add(new MacTestCommand(MacTestCommandKind.Quit));
                File.WriteAllText(
                    testRequestPath,
                    MacTestBridgeProtocol.SerializeRequest(commands),
                    Encoding.ASCII);
            }

            Directory.CreateDirectory(sourceDirectory);
            var utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "designer_form.c"),
                generatedForm.CSource,
                utf8WithoutBom);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "designer_form.h"),
                generatedForm.HeaderSource,
                utf8WithoutBom);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "designer_form.r"),
                generatedForm.ResourceSource,
                utf8WithoutBom);
            File.WriteAllText(
                Path.Combine(sourceDirectory, "CMakeLists.txt"),
                generatedForm.CMakeLists,
                utf8WithoutBom);
            EnsureUserSourceFiles(sourceDirectory, generatedForm.MainSource);
            RefreshProjectTree();
            var processStart = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in MacBuildRunCommand.CreatePowerShellArguments(
                profile,
                scriptPath,
                sourceDirectory,
                buildDirectory,
                "DesignerForm",
                testRequestPath,
                telemetryPath,
                _selectedEmulator,
                basiliskSerialA: _selectedEmulator == MacEmulator.BasiliskII
                    ? _basiliskSerialA
                    : null,
                basiliskSerialB: _selectedEmulator == MacEmulator.BasiliskII
                    ? _basiliskSerialB
                    : null))
            {
                processStart.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = processStart };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        AppendBuildOutput(args.Data);
                        TrackEmulatorProcess(args.Data);
                    });
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    Dispatcher.InvokeAsync(() => AppendBuildOutput($"ERROR: {args.Data}"));
                }
            };

            _buildRunStartedAt = DateTime.Now;
            if (!process.Start())
            {
                throw new InvalidOperationException("The PowerShell build process did not start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                StatusText.Text =
                    $"Build/run failed (exit code {process.ExitCode}). See output below.";
                return;
            }

            if (runTests)
            {
                var telemetry = MacTestBridgeProtocol.ParseResponse(
                    File.ReadAllText(telemetryPath!, Encoding.ASCII));
                DisplayTestTelemetry(telemetry, FormTitle.Text);
            }
            else
            {
                StatusText.Text =
                    "Emulator session ended. Confirm the generated form was shown in the guest.";
            }
        }
        catch (Win32Exception error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "Could not start the Windows build process.";
        }
        catch (IOException error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "Could not access a build or telemetry file.";
        }
        catch (InvalidOperationException error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "Build & Run could not start.";
        }
        catch (ArgumentException error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "The current form cannot be generated for this target.";
        }
        catch (FormatException error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "The guest returned malformed or incomplete telemetry.";
        }
        finally
        {
            _emulatorProcessId = null;
            _emulatorProcessName = null;
            StopEmulatorButton.IsEnabled = false;
            UpdateBuildRunAvailability();
        }
    }

    private async void StopEmulatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_emulatorProcessId is not int processId)
        {
            return;
        }

        try
        {
            using var emulator = Process.GetProcessById(processId);
            if (_emulatorProcessName is null ||
                !string.Equals(
                    emulator.ProcessName,
                    _emulatorProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                emulator.StartTime < _buildRunStartedAt)
            {
                throw new InvalidOperationException(
                    "The tracked process is not the emulator instance launched by this run.");
            }

            StopEmulatorButton.IsEnabled = false;
            StatusText.Text = $"Stopping {_emulatorProcessName}...";
            if (!emulator.CloseMainWindow())
            {
                emulator.Kill();
            }
            else
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await emulator.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!emulator.HasExited)
                    {
                        emulator.Kill();
                    }
                }
            }

            if (!emulator.HasExited)
            {
                await emulator.WaitForExitAsync();
            }
        }
        catch (ArgumentException error)
        {
            _emulatorProcessId = null;
            _emulatorProcessName = null;
            StopEmulatorButton.IsEnabled = false;
            AppendBuildOutput(error.Message);
            StatusText.Text = "The tracked emulator process has already exited.";
        }
        catch (InvalidOperationException error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "Could not safely stop the tracked emulator process.";
        }
        catch (Win32Exception error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "Windows could not stop the emulator process.";
        }
        catch (UnauthorizedAccessException error)
        {
            AppendBuildOutput(error.Message);
            StatusText.Text = "Windows denied access to the emulator process.";
        }
    }

    private void TrackEmulatorProcess(string line)
    {
        var (prefix, processName) = line.StartsWith(
                "Mini vMac process ID: ",
                StringComparison.Ordinal)
            ? ("Mini vMac process ID: ", "Mini vMac")
            : line.StartsWith("Basilisk II process ID: ", StringComparison.Ordinal)
                ? ("Basilisk II process ID: ", "BasiliskII")
                : (string.Empty, string.Empty);
        if (prefix.Length == 0 ||
            !int.TryParse(
                line.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var processId) ||
            processId <= 0)
        {
            return;
        }

        _emulatorProcessId = processId;
        _emulatorProcessName = processName;
        StopEmulatorButton.IsEnabled = true;
    }

    private void DisplayTestTelemetry(
        MacTestTelemetry telemetry,
        string expectedFormTitle)
    {
        var errors = new List<string>(telemetry.Errors);
        if (!string.Equals(
                telemetry.FormTitle,
                expectedFormTitle,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Form title mismatch: expected '{expectedFormTitle}', received '{telemetry.FormTitle}'.");
        }

        if (telemetry.Controls.Count != _controls.Count)
        {
            errors.Add(
                $"Control count mismatch: expected {_controls.Count}, received {telemetry.Controls.Count}.");
        }

        foreach (var (model, _) in _controls)
        {
            var actual = telemetry.Controls.SingleOrDefault(
                control => control.Id == model.Id);
            if (actual is null ||
                actual.WidgetTypeId != model.WidgetTypeId ||
                actual.Text != model.Text ||
                actual.X != (int)Math.Round(model.X, MidpointRounding.AwayFromZero) ||
                actual.Y != (int)Math.Round(model.Y, MidpointRounding.AwayFromZero) ||
                actual.Width != (int)Math.Round(model.Width, MidpointRounding.AwayFromZero) ||
                actual.Height != (int)Math.Round(model.Height, MidpointRounding.AwayFromZero))
            {
                errors.Add($"Control metadata mismatch for '{model.Id}'.");
            }

            if (_widgetRegistry.GetRequired(model.WidgetTypeId).IsInteractive &&
                !telemetry.Events.Any(testEvent =>
                    testEvent.Action == "CLICK" &&
                    testEvent.ControlId == model.Id &&
                    testEvent.Outcome == "HANDLED"))
            {
                errors.Add($"Guest did not report a handled click for '{model.Id}'.");
            }
        }

        AppendBuildOutput(
            $"Guest form: {telemetry.FormTitle}; " +
            $"{telemetry.Controls.Count} control(s); " +
            $"{telemetry.Events.Count} event(s).");
        foreach (var error in errors)
        {
            AppendBuildOutput($"TEST ERROR: {error}");
        }

        StatusText.Text = errors.Count == 0
            ? "Build & Test passed: guest widget metadata and handlers were verified."
            : $"Build & Test failed with {errors.Count} error(s). See output below.";
    }

    private void AppendBuildOutput(string line)
    {
        BuildOutput.AppendText($"{line}{Environment.NewLine}");
        BuildOutput.ScrollToEnd();
        if (line.Contains("Build succeeded:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Starting the Mac SE emulator", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Starting Basilisk II", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = line;
        }
    }

    private static string FindWorkspaceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var scriptPath = Path.Combine(
                directory.FullName,
                "scripts",
                "windows",
                "build-and-run-mac-se.ps1");
            if (File.Exists(scriptPath))
            {
                return directory.FullName;
            }
        }

        throw new IOException(
            "Could not find scripts/windows/build-and-run-mac-se.ps1 in the workspace.");
    }

    private void UpdateBuildRunAvailability()
    {
        var isAvailable =
            _controls.Count > 0 &&
            TargetProfileSelector.SelectedItem is MacTargetProfile selected &&
            MacBuildRunCommand.IsSupportedCombination(selected, _selectedEmulator);
        BuildRunButton.IsEnabled = isAvailable;
        BuildTestButton.IsEnabled =
            _controls.Count > 0 &&
            _selectedEmulator == MacEmulator.MiniVmac &&
            TargetProfileSelector.SelectedItem is MacTargetProfile testTarget &&
            testTarget.Id == "mac-se-68k";
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new HelpWindow { Owner = this }.ShowDialog();
        }
        catch (InvalidOperationException error)
        {
            MessageBox.Show(
                this,
                $"Could not open IDE help: {error.Message}",
                "ClassicMacStudio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(
            this,
            $"ClassicMacStudio {ApplicationVersion}\n\nCreated by Maarten Roest\n\n" +
            "Licensed under the GNU General Public License v3.0.\n\n" +
            "GPLv3 lets users run, study, modify, and redistribute the software. " +
            "Redistribution requires preserving notices and license terms, making " +
            "the corresponding source available, and keeping recipients' freedoms. " +
            "The software is provided without warranty.\n\n" +
            "This is a brief summary, not the license text.",
            "About ClassicMacStudio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void SerialAgentConnectionWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SerialAgentConnectionWindowMenuItem.IsChecked)
        {
            OpenSerialAgentConnectionWindow();
        }
        else
        {
            _serialAgentConnectionWindow?.Hide();
        }
    }

    private void OpenSerialAgentConnectionWindow()
    {
        if (_serialAgentConnectionWindow is { IsLoaded: true } existingWindow)
        {
            if (!existingWindow.IsVisible)
            {
                existingWindow.Show();
            }
            existingWindow.Activate();
            SerialAgentConnectionWindowMenuItem.IsChecked = true;
            return;
        }

        var connectionWindow = new SerialAgentConnectionWindow
        {
            Owner = this,
        };
        connectionWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_serialAgentConnectionWindow, connectionWindow))
            {
                _serialAgentConnectionWindow = null;
                SerialAgentConnectionWindowMenuItem.IsChecked = false;
            }
        };
        _serialAgentConnectionWindow = connectionWindow;
        SerialAgentConnectionWindowMenuItem.IsChecked = true;
        connectionWindow.Show();
    }

    private void ToolboxPaneMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetToolboxPaneVisible(ToolboxPaneMenuItem.IsChecked);

    private void InspectorPaneMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetInspectorPaneVisible(InspectorPaneMenuItem.IsChecked);

    private void BuildOutputMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetBuildOutputVisible(BuildOutputMenuItem.IsChecked);

    private void SerialTerminalWindowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SerialTerminalWindowMenuItem.IsChecked)
        {
            OpenSerialTerminalWindow();
        }
        else
        {
            _serialTerminalWindow?.Hide();
        }
    }

    private void OpenSerialTerminalWindow()
    {
        if (_serialTerminalWindow is { IsLoaded: true } existingWindow)
        {
            if (!existingWindow.IsVisible)
            {
                existingWindow.Show();
            }
            existingWindow.Activate();
            SerialTerminalWindowMenuItem.IsChecked = true;
            return;
        }

        var terminalWindow = new SerialTerminalWindow(_basiliskSerialA, _basiliskSerialB)
        {
            Owner = this,
        };
        terminalWindow.BasiliskSerialPortsChanged += (serialA, serialB) =>
        {
            _basiliskSerialA = serialA;
            _basiliskSerialB = serialB;
            StatusText.Text = serialA is not null || serialB is not null
                ? $"Basilisk II serial configured: A={serialA ?? "unused"}, B={serialB ?? "unused"}."
                : "Basilisk II serial configuration cleared.";
        };
        terminalWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_serialTerminalWindow, terminalWindow))
            {
                _serialTerminalWindow = null;
                SerialTerminalWindowMenuItem.IsChecked = false;
            }
        };
        _serialTerminalWindow = terminalWindow;
        SerialTerminalWindowMenuItem.IsChecked = true;
        terminalWindow.Show();
    }

    private void RestoreDefaultLayout_Click(object sender, RoutedEventArgs e)
    {
        SetToolboxPaneVisible(true);
        SetInspectorPaneVisible(true);
        SetBuildOutputVisible(false);
    }

    private void SetToolboxPaneVisible(bool visible)
    {
        ToolboxPaneMenuItem.IsChecked = visible;
        ToolboxPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ToolboxColumn.Width = visible ? new GridLength(205) : new GridLength(0);
    }

    private void SetInspectorPaneVisible(bool visible)
    {
        InspectorPaneMenuItem.IsChecked = visible;
        InspectorPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = visible ? new GridLength(240) : new GridLength(0);
    }

    private void SetBuildOutputVisible(bool visible)
    {
        BuildOutputMenuItem.IsChecked = visible;
        BuildOutput.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }
        _serialTerminalWindow?.Close();
        _serialTerminalWindow?.Close();
    }

    private enum ResizeGripDirection
    {
        NorthWest,
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West,
    }

    private sealed record ResizeGripTag(string ControlId, ResizeGripDirection Direction);

    private sealed record LineEndpointGripTag(string ControlId, bool IsStart);
}
