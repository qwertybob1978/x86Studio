using System.Diagnostics;
using System.Globalization;
using System.IO;
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

public partial class FreeRTOSx86StudioWindow : Window
{
    private sealed record WidgetColorChoice(string Label, string Hex);

    private readonly DesignerWidgetRegistry _widgetRegistry =
        FreeRTOSx86WidgetCatalog.CreateRegistry();
    private readonly List<DesignerControl> _controls = [];
    private readonly Dictionary<(string ProfileId, string Hex), Brush> _previewBrushes = [];
    private readonly Dictionary<string, FrameworkElement> _widgetPropertyEditors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<ResizeGripDirection, Thumb>> _resizeGrips = [];
    private readonly Dictionary<string, Canvas> _resizeLayers = [];
    private readonly Dictionary<string, (Thumb Start, Thumb End)> _lineEndpointGrips = [];
    private ClassicMacProject _project = null!;
    private string? _projectPath;
    private string? _selectedControlId;
    private string? _draggedControlId;
    private bool _userEventsCodeLoaded;
    private Point _dragStart;
    private double _dragOriginX;
    private double _dragOriginY;
    private int _nextControlId = 1;

    public FreeRTOSx86StudioWindow()
    {
        InitializeComponent();
        var cHighlighting = HighlightingManager.Instance.GetDefinition("C++");
        TaskCodeEditor.SyntaxHighlighting = cHighlighting;
        UserEventsCodeEditor.SyntaxHighlighting = cHighlighting;
        UserEventsHeaderEditor.SyntaxHighlighting = cHighlighting;
        DesignCanvas.MouseMove += CanvasControl_MouseMove;
        DesignCanvas.MouseLeftButtonUp += CanvasControl_MouseLeftButtonUp;
        BuildWidgetRibbon();
        TargetProfileSelector.ItemsSource = FreeRTOSx86TargetProfiles.All;
        TargetProfileSelector.SelectedIndex = 0;
        WidgetPalette.ItemsSource = _widgetRegistry.Definitions;
        WidgetPalette.SelectedIndex = 0;
        CreateNewProject();
    }

    private MacTargetProfile SelectedProfile =>
        (MacTargetProfile)TargetProfileSelector.SelectedItem;

    private void BuildWidgetRibbon()
    {
        var panels = new Dictionary<string, WrapPanel>(StringComparer.Ordinal)
        {
            ["Controls"] = ControlsRibbonPanel,
            ["Data"] = DataRibbonPanel,
            ["Containers"] = ContainersRibbonPanel,
            ["Media"] = MediaRibbonPanel,
            ["Drawing"] = DrawingRibbonPanel,
        };
        foreach (var descriptor in FreeRTOSx86WidgetCatalog.Descriptors)
        {
            var definition = descriptor.Definition;
            var button = new Button
            {
                Style = (Style)FindResource("RibbonCommandButton"),
                Tag = definition.Id,
                ToolTip = $"Insert {definition.DisplayName}",
                Margin = new Thickness(1, 0, 1, 0),
            };
            button.Click += InsertWidget_Click;
            var content = new StackPanel();
            content.Children.Add(new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = GetRibbonIcon(definition.Id),
                Width = 26,
                Height = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = definition.DisplayName,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            button.Content = content;
            if (!panels.TryGetValue(descriptor.RibbonGroup, out var panel))
            {
                throw new InvalidOperationException(
                    $"Widget '{definition.Id}' uses unknown ribbon group '{descriptor.RibbonGroup}'.");
            }
            panel.Children.Add(button);
        }
    }

    private static MaterialDesignThemes.Wpf.PackIconKind GetRibbonIcon(string widgetId) =>
        widgetId switch
        {
            "static-text" => MaterialDesignThemes.Wpf.PackIconKind.FormatText,
            "button" => MaterialDesignThemes.Wpf.PackIconKind.ButtonPointer,
            "checkbox" => MaterialDesignThemes.Wpf.PackIconKind.CheckboxMarkedOutline,
            "graphic-line" => MaterialDesignThemes.Wpf.PackIconKind.VectorLine,
            "graphic-rectangle-outline" => MaterialDesignThemes.Wpf.PackIconKind.RectangleOutline,
            "graphic-rectangle-filled" => MaterialDesignThemes.Wpf.PackIconKind.Rectangle,
            "graphic-circle-outline" => MaterialDesignThemes.Wpf.PackIconKind.CircleOutline,
            "graphic-circle-filled" => MaterialDesignThemes.Wpf.PackIconKind.Circle,
            _ => MaterialDesignThemes.Wpf.PackIconKind.Widgets,
        };

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        CreateNewProject();
        SetStatus("Created a new 8088 / Hercules LVGL project.");
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "FreeRTOSx86Studio projects (*.x86gui)|*.x86gui|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var project = ClassicMacProjectSerializer.Load(
                dialog.FileName,
                _widgetRegistry);
            LoadProject(project, dialog.FileName);
            SetStatus($"Opened {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception error) when (error is IOException or ArgumentException or
                                      NotSupportedException)
        {
            ShowError("Unable to open project", error);
        }
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        SaveProject();
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "FreeRTOSx86Studio projects (*.x86gui)|*.x86gui",
            DefaultExt = ".x86gui",
            AddExtension = true,
            FileName = _project.Name,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _projectPath = dialog.FileName;
        SaveProject();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenDosAgentDebug_Click(object sender, RoutedEventArgs e) =>
        OpenAgentConnectionWindow("COM4", paceSerialWrites: true);

    private void OpenAgentConnectionWindow(
        string preferredPortName,
        bool paceSerialWrites)
    {
        var window = new SerialAgentConnectionWindow(
            preferredPortName,
            paceSerialWrites)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        window.Show();
    }

    private void TargetProfileSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!IsInitialized || TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        if (profile.GraphicsBackendId == "cga")
        {
            SetStatus(
                $"CGA target selected: {profile.DisplayName}. Runtime build support is pending.");
        }
        else if (profile.GraphicsBackendId == "vga")
        {
            SetStatus("80386 / VGA design profile selected. Protected-mode runtime support is pending.");
        }
        else
        {
            SetStatus("8088 / Hercules with LVGL 1-bpp selected.");
        }
        if (_project is not null)
        {
            RefreshProjectSnapshot();
        }
        RenderDesigner();
        RefreshInspector();
        UpdateGeneratedCode();
    }

    private void AddControl_Click(object sender, RoutedEventArgs e)
    {
        if (WidgetPalette.SelectedItem is not DesignerWidgetDefinition definition)
        {
            SetStatus("Select a toolbox item first.");
            return;
        }

        AddControl(definition);
    }

    private void InsertWidget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string widgetId })
        {
            throw new InvalidOperationException(
                "The LVGL ribbon command is missing its widget identifier.");
        }

        var definition = _widgetRegistry.Definitions.FirstOrDefault(
            candidate => candidate.Id == widgetId);
        if (definition is null)
        {
            throw new InvalidOperationException(
                $"The LVGL ribbon command refers to unsupported widget '{widgetId}'.");
        }

        WidgetPalette.SelectedItem = definition;
        AddControl(definition);
    }

    private void AddControl(DesignerWidgetDefinition definition)
    {
        var id = $"control{_nextControlId++}";
        var x = 16d;
        var y = 16d;
        if (SnapToGridToggle.IsChecked == true)
        {
            if (!TryGetGridSpacing(out var spacing))
            {
                return;
            }

            x = DesignerGeometry.Snap(x, spacing);
            y = DesignerGeometry.Snap(y, spacing);
        }
        var control = _widgetRegistry.CreateControl(
            id,
            definition.Id,
            x,
            y);
        if (!control.FitsWithin(SelectedProfile.ScreenWidth, SelectedProfile.ScreenHeight))
        {
            SetStatus("The selected control does not fit on this display.");
            return;
        }

        _controls.Add(control);
        _selectedControlId = control.Id;
        RefreshProjectSnapshot();
        RenderDesigner();
        RefreshInspector();
        UpdateGeneratedCode();
        SetStatus($"Added {definition.DisplayName}.");
    }

    private void DeleteControl_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedControlId is null)
        {
            return;
        }

        _controls.RemoveAll(control => control.Id == _selectedControlId);
        _selectedControlId = null;
        RefreshProjectSnapshot();
        RenderDesigner();
        RefreshInspector();
        UpdateGeneratedCode();
        SetStatus("Control deleted.");
    }

    private void ApplyProperties_Click(object sender, RoutedEventArgs e)
    {
        var index = _controls.FindIndex(control => control.Id == _selectedControlId);
        if (index < 0)
        {
            SetStatus("Select a control to edit its properties.");
            return;
        }

        try
        {
            var x = ParseCoordinate(XBox.Text, nameof(XBox));
            var y = ParseCoordinate(YBox.Text, nameof(YBox));
            var width = ParseSize(WidthBox.Text, nameof(WidthBox));
            var height = ParseSize(HeightBox.Text, nameof(HeightBox));
            if (SnapToGridToggle.IsChecked == true)
            {
                if (!TryGetGridSpacing(out var spacing))
                {
                    return;
                }

                x = DesignerGeometry.Snap(x, spacing);
                y = DesignerGeometry.Snap(y, spacing);
                width = Math.Max(1, DesignerGeometry.Snap(width, spacing));
                height = Math.Max(1, DesignerGeometry.Snap(height, spacing));
            }
            if (SnapToGridToggle.IsChecked == true)
            {
                if (!TryGetGridSpacing(out var spacing))
                {
                    return;
                }

                x = DesignerGeometry.Snap(x, spacing);
                y = DesignerGeometry.Snap(y, spacing);
                width = Math.Max(1, DesignerGeometry.Snap(width, spacing));
                height = Math.Max(1, DesignerGeometry.Snap(height, spacing));
            }
            var updated = _controls[index]
                .WithText(TextBox.Text)
                .WithGeometry(x, y, width, height)
                .WithEvent("OnClick", ClickHandlerBox.IsEnabled ? ClickHandlerBox.Text : null);
            foreach (var (propertyId, editor) in _widgetPropertyEditors)
            {
                updated = updated.WithProperty(
                    propertyId,
                    ReadWidgetPropertyValue(propertyId, editor));
            }
            _widgetRegistry.ValidateControl(updated);
            if (!updated.FitsWithin(SelectedProfile.ScreenWidth, SelectedProfile.ScreenHeight))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(XBox),
                    "The control must fit within the selected display.");
            }

            _controls[index] = updated;
            RefreshProjectSnapshot();
            RenderDesigner();
            RefreshInspector();
            UpdateGeneratedCode();
            SetStatus("Control properties applied.");
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            ShowError("Invalid control properties", error);
        }
    }

    private async void BuildRun_Click(object sender, RoutedEventArgs e)
    {
        if (!SelectedProfile.BuildSupported ||
            SelectedProfile.GraphicsBackendId != "hercules")
        {
            SetStatus("Build is available only for the 8088 / Hercules target.");
            return;
        }

        if (_projectPath is null)
        {
            SaveProjectAs_Click(sender, e);
            if (_projectPath is null)
            {
                return;
            }
        }

        try
        {
            SaveProject();
            WriteGeneratedSources();
            var runtimeScript = Path.Combine(
                FindStudioRoot(),
                "runtime",
                "8088",
                "build-and-run.ps1");
            if (!File.Exists(runtimeScript))
            {
                throw new FileNotFoundException("The DOS build script was not found.", runtimeScript);
            }

            BuildRunButton.IsEnabled = false;
            BuildOutputEditor.Text = "Building the FreeRTOS DOS application...";
            BuildTabs.SelectedIndex = 0;
            SetStatus("Building the FreeRTOS DOS application...");
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(runtimeScript);
            startInfo.ArgumentList.Add("-ProjectDirectory");
            startInfo.ArgumentList.Add(Path.GetDirectoryName(_projectPath)!);
            if (EnableAgentDebugToggle.IsChecked == true)
            {
                startInfo.ArgumentList.Add("-AgentPort");
                startInfo.ArgumentList.Add("COM2");
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("The DOS build process did not start.");
            }
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await standardOutput;
            var errorOutput = await standardError;
            BuildOutputEditor.Text = output + errorOutput;
            BuildTabs.SelectedIndex = 0;
            SetStatus(process.ExitCode == 0
                ? "DOSBox-X session completed."
                : $"Build or DOSBox-X failed with exit code {process.ExitCode}.");
        }
        catch (Exception error)
        {
            ShowError("Build & Run failed", error);
        }
        finally
        {
            BuildRunButton.IsEnabled = true;
        }
    }

    private void CanvasControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb)
        {
            e.Handled = true;
            return;
        }

        if (sender is not Border border || border.Tag is not string controlId)
        {
            return;
        }

        _selectedControlId = controlId;
        _draggedControlId = controlId;
        _dragStart = e.GetPosition(DesignCanvas);
        var selected = _controls.First(control => control.Id == controlId);
        _dragOriginX = selected.X;
        _dragOriginY = selected.Y;
        DesignCanvas.CaptureMouse();
        RenderDesigner();
        RefreshInspector();
        e.Handled = true;
    }

    private void CanvasControl_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _draggedControlId is null ||
            sender is not Canvas)
        {
            return;
        }

        var current = e.GetPosition(DesignCanvas);
        var index = _controls.FindIndex(control => control.Id == _draggedControlId);
        if (index < 0)
        {
            return;
        }

        var control = _controls[index];
        var x = Math.Clamp(_dragOriginX + current.X - _dragStart.X, 0,
            SelectedProfile.ScreenWidth - control.Width);
        var y = Math.Clamp(_dragOriginY + current.Y - _dragStart.Y, 0,
            SelectedProfile.ScreenHeight - control.Height);
        if (SnapToGridToggle.IsChecked == true)
        {
            if (!TryGetGridSpacing(out var spacing))
            {
                return;
            }

            x = Math.Clamp(
                DesignerGeometry.Snap(x, spacing),
                0,
                SelectedProfile.ScreenWidth - control.Width);
            y = Math.Clamp(
                DesignerGeometry.Snap(y, spacing),
                0,
                SelectedProfile.ScreenHeight - control.Height);
        }

        _controls[index] = control.WithGeometry(x, y, control.Width, control.Height);
        var element = DesignCanvas.Children
            .OfType<Border>()
            .FirstOrDefault(candidate => candidate.Tag as string == control.Id);
        if (element is not null)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
        }
        RefreshInspector();
    }

    private void CanvasControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DesignCanvas.ReleaseMouseCapture();

        if (_draggedControlId is not null)
        {
            _draggedControlId = null;
            RefreshProjectSnapshot();
            RenderDesigner();
            RefreshInspector();
            UpdateGeneratedCode();
            SetStatus("Control moved.");
        }
    }

    private void GridSettings_Changed(object sender, RoutedEventArgs e) =>
        UpdateGridAppearance();

    private void GridSpacing_LostFocus(object sender, RoutedEventArgs e) =>
        UpdateGridAppearance();

    private void UpdateGridAppearance()
    {
        if (DesignCanvas is null ||
            GridSpacingSelector is null ||
            !TryGetGridSpacing(out var spacing))
        {
            return;
        }

        var isDosDisplay = TargetProfileSelector.SelectedItem is MacTargetProfile profile &&
                           profile.GraphicsBackendId is "hercules" or "cga" or "vga";
        var baseColor = isDosDisplay ? Colors.Black : Colors.White;
        if (ShowGridToggle.IsChecked != true)
        {
            DesignCanvas.Background = new SolidColorBrush(baseColor);
        }
        else
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(
                new SolidColorBrush(baseColor),
                null,
                new RectangleGeometry(new Rect(0, 0, spacing, spacing))));
            drawing.Children.Add(new GeometryDrawing(
                new SolidColorBrush(isDosDisplay
                    ? Color.FromRgb(50, 50, 50)
                    : Color.FromRgb(205, 205, 205)),
                null,
                new RectangleGeometry(new Rect(0, 0, 1, 1))));
            DesignCanvas.Background = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, spacing, spacing),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, spacing, spacing),
                ViewboxUnits = BrushMappingMode.Absolute,
            };
        }

        StatusText.Text = SnapToGridToggle.IsChecked == true
            ? $"Grid {spacing}px | snap enabled"
            : $"Grid {spacing}px | snap disabled";
    }

    private bool TryGetGridSpacing(out int spacing)
    {
        if (int.TryParse(
                GridSpacingSelector.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out spacing) &&
            spacing is >= 2 and <= 64)
        {
            return true;
        }

        StatusText.Text = "Grid spacing must be an integer from 2 to 64 pixels.";
        return false;
    }

    private void CreateNewProject()
    {
        var profile = FreeRTOSx86TargetProfiles.All[0];
        TargetProfileSelector.SelectedItem = profile;
        _projectPath = null;
        _userEventsCodeLoaded = false;
        _nextControlId = 1;
        _selectedControlId = null;
        _controls.Clear();
        _project = CreateProjectSnapshot(profile);
        ProjectPathText.Text = "Unsaved project";
        RenderDesigner();
        RefreshInspector();
        UpdateGeneratedCode();
    }

    private void LoadProject(ClassicMacProject project, string path)
    {
        var profile = FreeRTOSx86TargetProfiles.All.FirstOrDefault(
            candidate => candidate.Id == project.TargetProfileId) ??
            throw new InvalidDataException(
                $"Target profile '{project.TargetProfileId}' is not an x86 studio target.");
        TargetProfileSelector.SelectedItem = profile;
        _controls.Clear();
        _controls.AddRange(project.Controls);
        _project = project;
        _projectPath = path;
        _userEventsCodeLoaded = false;
        _selectedControlId = null;
        _nextControlId = _controls.Count + 1;
        ProjectPathText.Text = path;
        RenderDesigner();
        RefreshInspector();
        UpdateGeneratedCode();
    }

    private void SaveProject()
    {
        if (_projectPath is null)
        {
            SaveProjectAs_Click(this, new RoutedEventArgs());
            return;
        }

        try
        {
            RefreshProjectSnapshot();
            ClassicMacProjectSerializer.Save(_projectPath, _project);
            WriteGeneratedSources();
            ProjectPathText.Text = _projectPath;
            SetStatus($"Saved {Path.GetFileName(_projectPath)}.");
        }
        catch (Exception error) when (error is IOException or ArgumentException or
                                      InvalidDataException)
        {
            ShowError("Unable to save project", error);
        }
    }

    private void RefreshProjectSnapshot()
    {
        _project = CreateProjectSnapshot(SelectedProfile);
    }

    private ClassicMacProject CreateProjectSnapshot(MacTargetProfile profile) =>
        new(
            _project?.Name ?? "FreeRTOSx86Application",
            profile.Id,
            _project?.FormTitle ?? "FreeRTOS GUI",
            _controls,
            formProperties: new MacFormProperties
            {
                ShowTitleBar = false,
                Resizable = false,
                HasCloseBox = false,
                HasZoomBox = false,
                Movable = false,
                CenterOnScreen = true,
                Width = profile.ScreenWidth,
                Height = profile.ScreenHeight,
            });

    private void RenderDesigner()
    {
        if (DesignCanvas is null || TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        var preview = DosDisplayPreviewMetrics.Create(profile);
        DesignCanvas.Width = profile.ScreenWidth;
        DesignCanvas.Height = profile.ScreenHeight;
        DesignCanvas.LayoutTransform = new ScaleTransform(preview.ScaleX, preview.ScaleY);
        var ditherNote = profile.GraphicsBackendId is "hercules" or "cga"
            ? "; 8x8 ordered dithering is preview-only"
            : string.Empty;
        DisplayInfoText.Text =
            $"{profile.ScreenWidth} × {profile.ScreenHeight}, {profile.ColorDepthBits}-bpp " +
            $"{profile.GraphicsBackendId.ToUpperInvariant()} → " +
            $"{preview.Width:0} × {preview.Height:0} simulated display (4:3 physical aspect; " +
            $"editing remains in native pixels{ditherNote})";
        UpdateGridAppearance();
        DesignCanvas.Children.Clear();
        _resizeGrips.Clear();
        _resizeLayers.Clear();
        _lineEndpointGrips.Clear();
        foreach (var control in _controls)
        {
            var foreground = GetPreviewBrush(
                profile,
                control.Properties.GetValueOrDefault("color") ?? "#FFFFFF");
            var background = GetPreviewBrush(
                profile,
                control.Properties.GetValueOrDefault("backgroundColor") ?? "#000000");
            var element = CreateControlPreview(control, profile, foreground, background);
            Canvas.SetLeft(element, control.X);
            Canvas.SetTop(element, control.Y);
            DesignCanvas.Children.Add(element);
        }
    }

    private Border CreateControlPreview(
        DesignerControl control,
        MacTargetProfile profile,
        Brush foreground,
        Brush background)
    {
        var selected = control.Id == _selectedControlId;
        var isLine = control.WidgetTypeId == "graphic-line";
        var frame = new Border
        {
            Width = control.Width,
            Height = control.Height,
            Background = Brushes.Transparent,
            BorderBrush = selected && !isLine ? Brushes.DodgerBlue : Brushes.Transparent,
            BorderThickness = selected && !isLine ? new Thickness(1) : new Thickness(0),
            Tag = control.Id,
        };
        var content = new Grid { ClipToBounds = false };
        content.Children.Add(CreateWidgetPreview(control, profile, foreground, background));
        var resizeLayer = new Canvas
        {
            Background = Brushes.Transparent,
            ClipToBounds = false,
        };
        content.Children.Add(resizeLayer);
        if (isLine)
        {
            var startGrip = CreateLineEndpointGrip(isStart: true);
            var endGrip = CreateLineEndpointGrip(isStart: false);
            startGrip.DragStarted += (_, _) => SelectForResize(control.Id);
            startGrip.DragDelta += (_, args) =>
                ResizeLineEndpoint(
                    control.Id,
                    isStart: true,
                    args.HorizontalChange,
                    args.VerticalChange);
            startGrip.DragCompleted += (_, _) => CompleteResize(control.Id);
            endGrip.DragStarted += (_, _) => SelectForResize(control.Id);
            endGrip.DragDelta += (_, args) =>
                ResizeLineEndpoint(
                    control.Id,
                    isStart: false,
                    args.HorizontalChange,
                    args.VerticalChange);
            endGrip.DragCompleted += (_, _) => CompleteResize(control.Id);
            resizeLayer.Children.Add(startGrip);
            resizeLayer.Children.Add(endGrip);
            _lineEndpointGrips.Add(control.Id, (startGrip, endGrip));
        }
        else
        {
            var grips = new Dictionary<ResizeGripDirection, Thumb>();
            foreach (var direction in Enum.GetValues<ResizeGripDirection>())
            {
                var grip = CreateResizeGrip(direction);
                grip.DragStarted += (_, _) => SelectForResize(control.Id);
                grip.DragDelta += (_, args) =>
                    ResizeFromGrip(
                        control.Id,
                        direction,
                        args.HorizontalChange,
                        args.VerticalChange);
                grip.DragCompleted += (_, _) => CompleteResize(control.Id);
                resizeLayer.Children.Add(grip);
                grips.Add(direction, grip);
            }

            _resizeGrips.Add(control.Id, grips);
        }

        _resizeLayers.Add(control.Id, resizeLayer);
        PositionResizeGrips(control);
        SetResizeGripVisibility(control.Id, selected);
        frame.Child = content;
        frame.MouseLeftButtonDown += CanvasControl_MouseLeftButtonDown;
        return frame;
    }

    private static Thumb CreateResizeGrip(ResizeGripDirection direction) =>
        new()
        {
            Width = 8,
            Height = 8,
            Background = Brushes.White,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Cursor = direction switch
            {
                ResizeGripDirection.NorthWest or ResizeGripDirection.SouthEast =>
                    Cursors.SizeNWSE,
                ResizeGripDirection.NorthEast or ResizeGripDirection.SouthWest =>
                    Cursors.SizeNESW,
                ResizeGripDirection.North or ResizeGripDirection.South =>
                    Cursors.SizeNS,
                _ => Cursors.SizeWE,
            },
            ToolTip = "Drag to resize",
            Visibility = Visibility.Collapsed,
        };

    private static Thumb CreateLineEndpointGrip(bool isStart) =>
        new()
        {
            Width = 8,
            Height = 8,
            Background = Brushes.White,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Cross,
            ToolTip = isStart ? "Drag the line start point" : "Drag the line end point",
            Visibility = Visibility.Collapsed,
        };

    private void PositionResizeGrips(DesignerControl control)
    {
        if (!_resizeLayers.TryGetValue(control.Id, out var layer))
        {
            return;
        }

        layer.Width = control.Width;
        layer.Height = control.Height;
        if (_resizeGrips.TryGetValue(control.Id, out var grips))
        {
            foreach (var (direction, grip) in grips)
            {
                var center = direction switch
                {
                    ResizeGripDirection.NorthWest => (X: 0d, Y: 0d),
                    ResizeGripDirection.North => (X: control.Width / 2, Y: 0d),
                    ResizeGripDirection.NorthEast => (X: control.Width, Y: 0d),
                    ResizeGripDirection.East => (X: control.Width, Y: control.Height / 2),
                    ResizeGripDirection.SouthEast => (X: control.Width, Y: control.Height),
                    ResizeGripDirection.South => (X: control.Width / 2, Y: control.Height),
                    ResizeGripDirection.SouthWest => (X: 0d, Y: control.Height),
                    ResizeGripDirection.West => (X: 0d, Y: control.Height / 2),
                    _ => throw new ArgumentOutOfRangeException(nameof(direction)),
                };
                Canvas.SetLeft(grip, center.X - grip.Width / 2);
                Canvas.SetTop(grip, center.Y - grip.Height / 2);
            }
        }

        if (_lineEndpointGrips.TryGetValue(control.Id, out var endpoints))
        {
            PositionLineEndpointGrip(control, layer, endpoints.Start, isStart: true);
            PositionLineEndpointGrip(control, layer, endpoints.End, isStart: false);
        }
    }

    private static void PositionLineEndpointGrip(
        DesignerControl control,
        Canvas layer,
        Thumb grip,
        bool isStart)
    {
        var x = ReadPercentage(control, isStart ? "startX" : "endX");
        var y = ReadPercentage(control, isStart ? "startY" : "endY");
        var centerX = Math.Max(0, control.Width - 1) * x / 100;
        var centerY = Math.Max(0, control.Height - 1) * y / 100;
        Canvas.SetLeft(grip, centerX - grip.Width / 2);
        Canvas.SetTop(grip, centerY - grip.Height / 2);
    }

    private void SetResizeGripVisibility(string controlId, bool visible)
    {
        if (_resizeGrips.TryGetValue(controlId, out var grips))
        {
            foreach (var grip in grips.Values)
            {
                grip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (_lineEndpointGrips.TryGetValue(controlId, out var endpoints))
        {
            endpoints.Start.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            endpoints.End.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SelectForResize(string controlId)
    {
        _selectedControlId = controlId;
        foreach (var frame in DesignCanvas.Children.OfType<Border>())
        {
            if (frame.Tag is string frameControlId)
            {
                var isSelected = string.Equals(
                    frameControlId,
                    controlId,
                    StringComparison.Ordinal);
                var isLine = _controls.Any(control =>
                    control.Id == frameControlId &&
                    control.WidgetTypeId == "graphic-line");
                frame.BorderBrush = isSelected && !isLine
                    ? Brushes.DodgerBlue
                    : Brushes.Transparent;
                frame.BorderThickness = isSelected && !isLine
                    ? new Thickness(1)
                    : new Thickness(0);
                SetResizeGripVisibility(frameControlId, isSelected);
            }
        }

        RefreshInspector();
    }

    private void ResizeFromGrip(
        string controlId,
        ResizeGripDirection direction,
        double horizontalChange,
        double verticalChange)
    {
        var index = _controls.FindIndex(control => control.Id == controlId);
        if (index < 0 ||
            DesignCanvas is null ||
            TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        var model = _controls[index];
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
                profile.ScreenWidth);
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
                profile.ScreenHeight);
        }

        var updated = model.WithGeometry(left, top, right - left, bottom - top);
        _widgetRegistry.ValidateControl(updated);
        _controls[index] = updated;
        UpdateControlPreview(updated, profile);
        RefreshInspector();
    }

    private void ResizeLineEndpoint(
        string controlId,
        bool isStart,
        double horizontalChange,
        double verticalChange)
    {
        var index = _controls.FindIndex(control => control.Id == controlId);
        if (index < 0 ||
            TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        var model = _controls[index];
        if (model.WidgetTypeId != "graphic-line")
        {
            return;
        }

        var start = new Point(
            model.X + Math.Max(0, model.Width - 1) *
            ReadPercentage(model, "startX") / 100,
            model.Y + Math.Max(0, model.Height - 1) *
            ReadPercentage(model, "startY") / 100);
        var end = new Point(
            model.X + Math.Max(0, model.Width - 1) *
            ReadPercentage(model, "endX") / 100,
            model.Y + Math.Max(0, model.Height - 1) *
            ReadPercentage(model, "endY") / 100);
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

        moved.X = Math.Clamp(moved.X, 0, profile.ScreenWidth - 1);
        moved.Y = Math.Clamp(moved.Y, 0, profile.ScreenHeight - 1);
        if (isStart)
        {
            start = moved;
        }
        else
        {
            end = moved;
        }

        var definition = _widgetRegistry.GetRequired(model.WidgetTypeId);
        var (left, right) = ExpandLineBounds(
            start.X,
            end.X,
            definition.MinimumWidth,
            profile.ScreenWidth);
        var (top, bottom) = ExpandLineBounds(
            start.Y,
            end.Y,
            definition.MinimumHeight,
            profile.ScreenHeight);
        var width = right - left + 1;
        var height = bottom - top + 1;
        var updated = model.WithGeometry(left, top, width, height)
            .WithProperty("startX", ToPercent(start.X - left, width - 1))
            .WithProperty("startY", ToPercent(start.Y - top, height - 1))
            .WithProperty("endX", ToPercent(end.X - left, width - 1))
            .WithProperty("endY", ToPercent(end.Y - top, height - 1));
        _widgetRegistry.ValidateControl(updated);
        _controls[index] = updated;
        UpdateControlPreview(updated, profile);
        RefreshInspector();
    }

    private static (double Leading, double Trailing) ExpandLineBounds(
        double first,
        double second,
        double minimumSize,
        double surfaceSize)
    {
        var leading = Math.Min(first, second);
        var trailing = Math.Max(first, second);
        if (trailing - leading + 1 >= minimumSize)
        {
            return (leading, trailing);
        }

        var center = (leading + trailing) / 2;
        leading = Math.Clamp(
            center - (minimumSize - 1) / 2,
            0,
            surfaceSize - minimumSize);
        trailing = leading + minimumSize - 1;
        return (leading, trailing);
    }

    private static string ToPercent(double offset, double span) =>
        Math.Clamp(
            (int)Math.Round(offset * 100 / span, MidpointRounding.AwayFromZero),
            0,
            100).ToString(CultureInfo.InvariantCulture);

    private void UpdateControlPreview(
        DesignerControl control,
        MacTargetProfile profile)
    {
        var frame = DesignCanvas.Children
            .OfType<Border>()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Tag as string,
                    control.Id,
                    StringComparison.Ordinal));
        if (frame?.Child is not Grid content || content.Children.Count < 2)
        {
            throw new InvalidOperationException(
                $"The preview for control '{control.Id}' is not available.");
        }

        var foreground = GetPreviewBrush(
            profile,
            control.Properties.GetValueOrDefault("color") ?? "#FFFFFF");
        var background = GetPreviewBrush(
            profile,
            control.Properties.GetValueOrDefault("backgroundColor") ?? "#000000");
        frame.Width = control.Width;
        frame.Height = control.Height;
        content.Children.RemoveAt(0);
        content.Children.Insert(0, CreateWidgetPreview(control, profile, foreground, background));
        Canvas.SetLeft(frame, control.X);
        Canvas.SetTop(frame, control.Y);
        PositionResizeGrips(control);
    }

    private void CompleteResize(string controlId)
    {
        if (_controls.All(control => control.Id != controlId))
        {
            return;
        }

        RefreshProjectSnapshot();
        UpdateGeneratedCode();
        RefreshInspector();
        SetStatus("Control resized.");
    }

    private FrameworkElement CreateWidgetPreview(
        DesignerControl control,
        MacTargetProfile profile,
        Brush foreground,
        Brush background)
    {
        var text = control.Text;
        return control.WidgetTypeId switch
        {
            "static-text" => new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            },
            "button" => new Border
            {
                BorderBrush = foreground,
                BorderThickness = new Thickness(1),
                Background = background,
                Child = CenteredText(text, foreground),
            },
            "checkbox" => CreateCheckboxPreview(control, foreground, background),
            "graphic-line" => CreateLinePreview(control, foreground),
            "graphic-rectangle-outline" => new Rectangle
            {
                Stroke = foreground,
                StrokeThickness = 2,
                Width = Math.Max(0, control.Width - 2),
                Height = Math.Max(0, control.Height - 2),
            },
            "graphic-rectangle-filled" => new Rectangle
            {
                Fill = foreground,
                Width = Math.Max(0, control.Width - 2),
                Height = Math.Max(0, control.Height - 2),
            },
            "graphic-rectangle-dithered" => new Rectangle
            {
                Fill = foreground,
                Width = Math.Max(0, control.Width - 2),
                Height = Math.Max(0, control.Height - 2),
            },
            "graphic-circle-outline" => new Ellipse
            {
                Stroke = foreground,
                StrokeThickness = 1,
                Width = Math.Max(0, control.Width - 2),
                Height = Math.Max(0, control.Height - 2),
            },
            "graphic-circle-filled" => new Ellipse
            {
                Fill = foreground,
                Width = Math.Max(0, control.Width - 2),
                Height = Math.Max(0, control.Height - 2),
            },
            "graphic-circle-dithered" => new Ellipse
            {
                Fill = foreground,
                Width = Math.Max(0, control.Width - 2),
                Height = Math.Max(0, control.Height - 2),
            },
            "graphic-arc-outline" or "graphic-arc-filled" or "graphic-arc-dithered" =>
                CreateArcPreview(
                    control,
                    foreground,
                    ReadPercentage(control, "startAngle"),
                    ReadPercentage(control, "sweepAngle"),
                    showValue: false),
            _ when control.WidgetTypeId.StartsWith("lvgl-", StringComparison.Ordinal) =>
                CreateLvglPreview(control, profile, foreground, background),
            _ => new TextBlock { Text = $"Unsupported: {control.WidgetTypeId}" },
        };
    }

    private FrameworkElement CreateLvglPreview(
        DesignerControl control,
        MacTargetProfile profile,
        Brush foreground,
        Brush background)
    {
        var definition = _widgetRegistry.GetRequired(control.WidgetTypeId);
        var content = string.IsNullOrEmpty(control.Text)
            ? definition.DisplayName
            : control.Text;
        var value = ReadPreviewValue(control, "value", 50);
        var items = ReadPreviewItems(control);
        FrameworkElement widget = control.WidgetTypeId switch
        {
            "lvgl-arc" => CreateArcPreview(control, foreground, -135, value * 270 / 100, true),
            "lvgl-bar" => CreateBarPreview(control, foreground, background, value, false),
            "lvgl-button-matrix" => CreateMatrixPreview(items, foreground, background),
            "lvgl-canvas" or "lvgl-image" or "lvgl-animimg" or "lvgl-image-button" =>
                CreateImagePreview(content, foreground, background),
            "lvgl-dropdown" => CreateDropdownPreview(content, foreground, background),
            "lvgl-roller" => CreateRowsPreview(items, foreground, background, selectedIndex: 1),
            "lvgl-slider" => CreateBarPreview(control, foreground, background, value, true),
            "lvgl-switch" => CreateSwitchPreview(
                bool.TryParse(control.Properties.GetValueOrDefault("checked"), out var isChecked) &&
                isChecked,
                foreground,
                background),
            "lvgl-table" => CreateTablePreview(content, foreground, background),
            "lvgl-textarea" => CreateTextAreaPreview(content, foreground, background),
            "lvgl-calendar" => CreateCalendarPreview(foreground, background),
            "lvgl-chart" => CreateChartPreview(foreground, background),
            "lvgl-color-wheel" => CreateColorWheelPreview(control, profile, foreground),
            "lvgl-keyboard" => CreateKeyboardPreview(foreground, background),
            "lvgl-led" => new Ellipse
            {
                Fill = foreground,
                Stroke = foreground,
                StrokeThickness = 1,
                Margin = new Thickness(4),
            },
            "lvgl-list" or "lvgl-menu" => CreateRowsPreview(items, foreground, background),
            "lvgl-meter" => CreateArcPreview(control, foreground, -135, value * 270 / 100, true),
            "lvgl-message-box" => CreateDialogPreview(content, foreground, background),
            "lvgl-span" => new TextBlock
            {
                Text = content,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            },
            "lvgl-spinbox" => CreateSpinboxPreview(value, foreground, background),
            "lvgl-spinner" => CreateArcPreview(control, foreground, -135, 90, false),
            "lvgl-tabview" => CreateTabsPreview(items, foreground, background),
            "lvgl-tileview" => CreateTilePreview(foreground, background),
            "lvgl-window" => CreateWindowPreview(content, foreground, background),
            _ => CenteredText(content, foreground),
        };
        return new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Padding = new Thickness(2),
            Child = widget,
        };
    }

    private static FrameworkElement CreateBarPreview(
        DesignerControl control,
        Brush foreground,
        Brush background,
        int value,
        bool showKnob)
    {
        var innerWidth = Math.Max(1, control.Width - 12);
        var trackHeight = Math.Max(4, Math.Min(8, control.Height / 3));
        var grid = new Grid();
        grid.Children.Add(new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Height = trackHeight,
            VerticalAlignment = VerticalAlignment.Center,
        });
        grid.Children.Add(new Border
        {
            Background = foreground,
            Width = innerWidth * value / 100,
            Height = Math.Max(2, trackHeight - 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (showKnob)
        {
            grid.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = foreground,
                Stroke = background,
                StrokeThickness = 1,
                Margin = new Thickness(innerWidth * value / 100, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        return grid;
    }

    private static FrameworkElement CreateSwitchPreview(
        bool isChecked,
        Brush foreground,
        Brush background)
    {
        var grid = new Grid();
        grid.Children.Add(new Border
        {
            Background = isChecked ? foreground : background,
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        });
        grid.Children.Add(new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = isChecked ? background : foreground,
            HorizontalAlignment = isChecked ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(3),
        });
        return grid;
    }

    private static FrameworkElement CreateArcPreview(
        DesignerControl control,
        Brush foreground,
        int startAngle,
        int sweepAngle,
        bool showValue)
    {
        var grid = new Grid();
        grid.Children.Add(CreateArcPath(
            control.Width,
            control.Height,
            startAngle,
            sweepAngle,
            foreground));
        if (showValue)
        {
            grid.Children.Add(CenteredText(
                $"{ReadPreviewValue(control, "value", 50)}%",
                foreground));
        }
        return grid;
    }

    private static System.Windows.Shapes.Path CreateArcPath(
        double width,
        double height,
        int startAngle,
        int sweepAngle,
        Brush foreground)
    {
        var radiusX = Math.Max(1, (width - 10) / 2);
        var radiusY = Math.Max(1, (height - 10) / 2);
        var centerX = width / 2;
        var centerY = height / 2;
        Point AtAngle(double angle)
        {
            var radians = angle * Math.PI / 180;
            return new Point(
                centerX + radiusX * Math.Cos(radians),
                centerY + radiusY * Math.Sin(radians));
        }

        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = AtAngle(startAngle) };
        figure.Segments.Add(new ArcSegment(
            AtAngle(startAngle + sweepAngle),
            new Size(radiusX, radiusY),
            0,
            Math.Abs(sweepAngle) > 180,
            sweepAngle >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            true));
        geometry.Figures.Add(figure);
        return new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = foreground,
            StrokeThickness = 2,
            Stretch = Stretch.Fill,
            Margin = new Thickness(2),
        };
    }

    private static FrameworkElement CreateDropdownPreview(
        string text,
        Brush foreground,
        Brush background)
    {
        var row = new DockPanel();
        row.Children.Add(new TextBlock
        {
            Text = "▼",
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 0, 2, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Padding = new Thickness(3),
            Child = row,
        };
    }

    private static FrameworkElement CreateRowsPreview(
        IReadOnlyList<string> items,
        Brush foreground,
        Brush background,
        int selectedIndex = -1)
    {
        var rows = new StackPanel();
        var visibleItems = items.Take(4).ToArray();
        for (var index = 0; index < visibleItems.Length; index++)
        {
            rows.Children.Add(new Border
            {
                Background = index == selectedIndex ? foreground : background,
                BorderBrush = foreground,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(3, 1, 3, 1),
                Child = new TextBlock
                {
                    Text = visibleItems[index],
                    Foreground = index == selectedIndex ? background : foreground,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            });
        }
        return rows;
    }

    private static FrameworkElement CreateMatrixPreview(
        IReadOnlyList<string> items,
        Brush foreground,
        Brush background)
    {
        var columns = Math.Max(1, Math.Min(3, items.Count));
        var matrix = new UniformGrid { Columns = columns };
        foreach (var item in items.Take(6))
        {
            matrix.Children.Add(new Border
            {
                BorderBrush = foreground,
                BorderThickness = new Thickness(1),
                Background = background,
                Margin = new Thickness(1),
                Child = CenteredText(item, foreground),
            });
        }
        return matrix;
    }

    private static FrameworkElement CreateTablePreview(
        string text,
        Brush foreground,
        Brush background)
    {
        var table = new UniformGrid { Columns = 2, Rows = 2 };
        foreach (var cell in new[] { text, "1", "2", "3" })
        {
            table.Children.Add(new Border
            {
                BorderBrush = foreground,
                BorderThickness = new Thickness(0.5),
                Background = background,
                Child = CenteredText(cell, foreground),
            });
        }
        return table;
    }

    private static FrameworkElement CreateTextAreaPreview(
        string text,
        Brush foreground,
        Brush background) =>
        new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Padding = new Thickness(3),
            Child = new TextBlock
            {
                Text = text + "│",
                Foreground = foreground,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
            },
        };

    private static FrameworkElement CreateCalendarPreview(
        Brush foreground,
        Brush background)
    {
        var calendar = new DockPanel();
        var header = new Border
        {
            Background = foreground,
            Child = CenteredText("January 2025", background),
        };
        DockPanel.SetDock(header, Dock.Top);
        calendar.Children.Add(header);
        var days = new UniformGrid { Columns = 7, Rows = 5 };
        for (var day = 1; day <= 35; day++)
        {
            days.Children.Add(new Border
            {
                BorderBrush = foreground,
                BorderThickness = new Thickness(0.25),
                Child = CenteredText(day <= 31 ? day.ToString(CultureInfo.InvariantCulture) : string.Empty, foreground),
            });
        }
        calendar.Children.Add(days);
        return calendar;
    }

    private static FrameworkElement CreateChartPreview(
        Brush foreground,
        Brush background)
    {
        var chart = new Canvas { ClipToBounds = true };
        chart.Children.Add(new Line
        {
            X1 = 2,
            Y1 = 2,
            X2 = 2,
            Y2 = 100,
            Stroke = foreground,
            StrokeThickness = 1,
        });
        chart.Children.Add(new Line
        {
            X1 = 2,
            Y1 = 100,
            X2 = 160,
            Y2 = 100,
            Stroke = foreground,
            StrokeThickness = 1,
        });
        var series = new Polyline
        {
            Stroke = foreground,
            StrokeThickness = 2,
            Points = new PointCollection
            {
                new(4, 78), new(30, 54), new(58, 64),
                new(86, 28), new(118, 42), new(154, 12),
            },
        };
        chart.Children.Add(series);
        return new Border
        {
            Background = background,
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Child = chart,
        };
    }

    private static FrameworkElement CreateImagePreview(
        string text,
        Brush foreground,
        Brush background)
    {
        var image = new Grid { ClipToBounds = true };
        image.Children.Add(new Rectangle
        {
            Fill = background,
            Stroke = foreground,
            StrokeThickness = 1,
        });
        image.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = foreground,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(3),
        });
        var mountains = new Polyline
        {
            Stroke = foreground,
            StrokeThickness = 2,
            Points = new PointCollection
            {
                new(0, 44), new(18, 22), new(30, 34), new(48, 12), new(72, 44),
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        image.Children.Add(mountains);
        image.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Background = background,
        });
        return image;
    }

    private static FrameworkElement CreateKeyboardPreview(
        Brush foreground,
        Brush background)
    {
        var keys = new UniformGrid { Columns = 10, Rows = 3 };
        foreach (var key in "QWERTYUIOPASDFGHJKLZXCVBNM")
        {
            keys.Children.Add(new Border
            {
                BorderBrush = foreground,
                BorderThickness = new Thickness(0.5),
                Background = background,
                Child = CenteredText(key.ToString(), foreground),
            });
        }
        return keys;
    }

    private FrameworkElement CreateColorWheelPreview(
        DesignerControl control,
        MacTargetProfile profile,
        Brush foreground)
    {
        var palette = GetHardwareColorChoices(profile)
            .Take(6)
            .Select(choice => GetPreviewBrush(profile, choice.Hex))
            .ToArray();
        var wheelTiles = new UniformGrid
        {
            Columns = 3,
            Rows = 2,
            ClipToBounds = true,
            Clip = new EllipseGeometry(
                new Rect(0, 0, control.Width, control.Height)),
        };
        foreach (var brush in palette)
        {
            wheelTiles.Children.Add(new Border { Background = brush });
        }

        var wheel = new Grid { ClipToBounds = true };
        wheel.Children.Add(wheelTiles);
        wheel.Children.Add(new Ellipse
        {
            Stroke = foreground,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });
        return wheel;
    }

    private static FrameworkElement CreateSpinboxPreview(
        int value,
        Brush foreground,
        Brush background)
    {
        var row = new DockPanel();
        var buttons = new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Child = CenteredText("▲\n▼", foreground),
        };
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(CenteredText(value.ToString(CultureInfo.InvariantCulture), foreground));
        return row;
    }

    private static FrameworkElement CreateTabsPreview(
        IReadOnlyList<string> items,
        Brush foreground,
        Brush background)
    {
        var panel = new DockPanel();
        var tabs = new UniformGrid { Columns = Math.Max(1, Math.Min(4, items.Count)) };
        foreach (var item in items.Take(4))
        {
            tabs.Children.Add(new Border
            {
                Background = foreground,
                BorderBrush = foreground,
                BorderThickness = new Thickness(1),
                Child = CenteredText(item, background),
            });
        }
        DockPanel.SetDock(tabs, Dock.Top);
        panel.Children.Add(tabs);
        panel.Children.Add(new Border
        {
            Background = background,
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
        });
        return panel;
    }

    private static FrameworkElement CreateTilePreview(Brush foreground, Brush background)
    {
        var tiles = new UniformGrid { Columns = 2, Rows = 2 };
        for (var index = 0; index < 4; index++)
        {
            tiles.Children.Add(new Border
            {
                Background = background,
                BorderBrush = foreground,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(1),
            });
        }
        return tiles;
    }

    private static FrameworkElement CreateDialogPreview(
        string text,
        Brush foreground,
        Brush background)
    {
        var panel = new DockPanel();
        var title = new Border
        {
            Background = foreground,
            Child = CenteredText("Message", background),
        };
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        var buttons = new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = CenteredText("OK", foreground),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4),
        });
        return new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Child = panel,
        };
    }

    private static FrameworkElement CreateWindowPreview(
        string text,
        Brush foreground,
        Brush background)
    {
        var panel = new DockPanel();
        var title = new Border
        {
            Background = foreground,
            Child = CenteredText(text, background),
        };
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(CenteredText(text, foreground));
        return new Border
        {
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Child = panel,
        };
    }

    private static int ReadPreviewValue(
        DesignerControl control,
        string propertyName,
        int fallback)
    {
        var rawValue = control.Properties.GetValueOrDefault(propertyName);
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 100)
            : fallback;
    }

    private static IReadOnlyList<string> ReadPreviewItems(DesignerControl control)
    {
        var value = control.Properties.GetValueOrDefault("items");
        return string.IsNullOrWhiteSpace(value)
            ? [control.Text is { Length: > 0 } text ? text : "Item 1", "Item 2", "Item 3"]
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private Brush GetPreviewBrush(MacTargetProfile profile, string value)
    {
        var color = ClassicMacRgbColor.TryParse(value, out var parsed)
            ? parsed
            : ClassicMacRgbColor.Parse("#FFFFFF");
        var hex = FormatColor(color);
        var key = (profile.Id, hex);
        if (_previewBrushes.TryGetValue(key, out var cachedBrush))
        {
            return cachedBrush;
        }

        Brush brush;
        if (profile.GraphicsBackendId is "hercules" or "cga")
        {
            var palette = GetHardwareColorChoices(profile)
                .Select(choice => ClassicMacRgbColor.Parse(choice.Hex))
                .ToArray();
            var pattern = DosDisplayDither.CreateOrdered8x8Pattern(color, palette);
            brush = pattern.All(pixel => pixel == pattern[0])
                ? CreateSolidBrush(pattern[0])
                : CreateDitherBrush(pattern);
        }
        else
        {
            var choice = GetNearestColorChoice(
                hex,
                GetHardwareColorChoices(profile));
            brush = CreateSolidBrush(ClassicMacRgbColor.Parse(choice.Hex));
        }

        if (brush.CanFreeze)
        {
            brush.Freeze();
        }
        _previewBrushes.Add(key, brush);
        return brush;
    }

    private static Brush CreateSolidBrush(ClassicMacRgbColor color) =>
        new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));

    private static Brush CreateDitherBrush(
        IReadOnlyList<ClassicMacRgbColor> pattern)
    {
        var drawing = new DrawingGroup();
        for (var index = 0; index < pattern.Count; index++)
        {
            var x = index % 8;
            var y = index / 8;
            drawing.Children.Add(new GeometryDrawing(
                CreateSolidBrush(pattern[index]),
                null,
                new RectangleGeometry(new Rect(x, y, 1, 1))));
        }

        return new DrawingBrush(drawing)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 8, 8),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
    }

    private static FrameworkElement CenteredText(string text, Brush foreground) =>
        new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private static FrameworkElement CreateCheckboxPreview(
        DesignerControl control,
        Brush foreground,
        Brush background)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new Border
        {
            Width = 12,
            Height = 12,
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            Background = background,
            Margin = new Thickness(1, 0, 4, 0),
            Child = bool.TryParse(
                control.Properties.GetValueOrDefault("checked"),
                out var isChecked) && isChecked
                ? CenteredText("X", foreground)
                : null,
        };
        panel.Children.Add(box);
        panel.Children.Add(CenteredText(control.Text, foreground));
        return panel;
    }

    private static FrameworkElement CreateLinePreview(
        DesignerControl control,
        Brush foreground)
    {
        var lineCanvas = new Canvas
        {
            Width = control.Width,
            Height = control.Height,
        };
        var startX = (control.Width - 1) *
                     ReadPercentage(control, "startX") / 100;
        var startY = (control.Height - 1) *
                     ReadPercentage(control, "startY") / 100;
        var endX = (control.Width - 1) *
                   ReadPercentage(control, "endX") / 100;
        var endY = (control.Height - 1) *
                   ReadPercentage(control, "endY") / 100;
        lineCanvas.Children.Add(new Line
        {
            X1 = startX,
            Y1 = startY,
            X2 = endX,
            Y2 = endY,
            Stroke = foreground,
            StrokeThickness = 1,
        });
        return lineCanvas;
    }

    private void RefreshInspector()
    {
        var control = _controls.FirstOrDefault(
            candidate => candidate.Id == _selectedControlId);
        ControlIdBox.Text = control?.Id ?? string.Empty;
        TextBox.Text = control?.Text ?? string.Empty;
        var definition = control is null
            ? null
            : _widgetRegistry.GetRequired(control.WidgetTypeId);
        TextBox.IsEnabled = definition?.SupportsTextAccess == true;
        XBox.Text = control?.X.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        YBox.Text = control?.Y.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        WidthBox.Text = control?.Width.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        HeightBox.Text = control?.Height.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        _widgetPropertyEditors.Clear();
        WidgetPropertiesPanel.Children.Clear();
        if (definition is not null && control is not null)
        {
            foreach (var property in definition.Properties)
            {
                var editor = CreateWidgetPropertyEditor(
                    property,
                    control.Properties.GetValueOrDefault(property.Id) ?? property.DefaultValue);
                var field = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                field.Children.Add(new TextBlock { Text = property.DisplayName });
                field.Children.Add(editor);
                WidgetPropertiesPanel.Children.Add(field);
                _widgetPropertyEditors.Add(property.Id, editor);
            }
        }

        var isInteractive = definition?.IsInteractive == true;
        ClickHandlerBox.IsEnabled = isInteractive;
        ClickHandlerBox.Text = control?.Events.GetValueOrDefault("OnClick") ?? string.Empty;
    }

    private FrameworkElement CreateWidgetPropertyEditor(
        DesignerWidgetPropertyDefinition property,
        string value)
    {
        switch (property.EditorKind)
        {
            case DesignerWidgetPropertyEditorKind.Boolean:
                return new CheckBox
                {
                    IsChecked = bool.TryParse(value, out var isChecked) && isChecked,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            case DesignerWidgetPropertyEditorKind.Color:
                var choices = GetTargetColorChoices(SelectedProfile);
                var selectedColor = GetNearestColorChoice(value, choices);
                return new ComboBox
                {
                    ItemsSource = choices,
                    DisplayMemberPath = nameof(WidgetColorChoice.Label),
                    SelectedValuePath = nameof(WidgetColorChoice.Hex),
                    SelectedValue = selectedColor.Hex,
                    Height = 30,
                };
            case DesignerWidgetPropertyEditorKind.MultiLineText:
                return new TextBox
                {
                    Text = value,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MinHeight = 48,
                };
            default:
                return new TextBox
                {
                    Text = value,
                    MinHeight = 28,
                };
        }
    }

    private static string ReadWidgetPropertyValue(
        string propertyId,
        FrameworkElement editor) =>
        editor switch
        {
            CheckBox checkBox =>
                (checkBox.IsChecked == true).ToString(CultureInfo.InvariantCulture),
            ComboBox { SelectedValue: string selectedValue } => selectedValue,
            TextBox textBox => textBox.Text,
            _ => throw new InvalidOperationException(
                $"The property editor for '{propertyId}' has an unsupported control type."),
        };

    private static IReadOnlyList<WidgetColorChoice> GetTargetColorChoices(
        MacTargetProfile profile)
    {
        var hardwareChoices = GetHardwareColorChoices(profile);
        if (profile.GraphicsBackendId is not ("hercules" or "cga"))
        {
            return hardwareChoices;
        }

        var choices = hardwareChoices.ToList();
        var palette = hardwareChoices
            .Select(choice => ClassicMacRgbColor.Parse(choice.Hex))
            .ToArray();
        choices.AddRange(DosDisplayDither.GetRepresentativeChoices(palette)
            .Select(choice => new WidgetColorChoice(
                $"8x8 ordered {choice.HighColorPercent}% " +
                $"{FormatColor(choice.HighColor)} + " +
                $"{100 - choice.HighColorPercent}% {FormatColor(choice.LowColor)} " +
                $"→ {FormatColor(choice.Color)}",
                FormatColor(choice.Color))));
        return choices;
    }

    private static IReadOnlyList<WidgetColorChoice> GetHardwareColorChoices(
        MacTargetProfile profile)
    {
        if (profile.GraphicsBackendId == "hercules" ||
            profile.Id == "dos-8088-cga-640")
        {
            return
            [
                new("Black (#000000)", "#000000"),
                new("White (#FFFFFF)", "#FFFFFF"),
            ];
        }

        if (profile.Id == "dos-8088-cga-320-palette0")
        {
            return
            [
                new("Black (#000000)", "#000000"),
                new("Green (#00AA00)", "#00AA00"),
                new("Red (#AA0000)", "#AA0000"),
                new("Brown (#AA5500)", "#AA5500"),
            ];
        }
        if (profile.Id == "dos-8088-cga-320-palette1")
        {
            return
            [
                new("Black (#000000)", "#000000"),
                new("Cyan (#00AAAA)", "#00AAAA"),
                new("Magenta (#AA00AA)", "#AA00AA"),
                new("Light gray (#AAAAAA)", "#AAAAAA"),
            ];
        }

        return
        [
            new("Black (#000000)", "#000000"),
            new("Blue (#0000AA)", "#0000AA"),
            new("Green (#00AA00)", "#00AA00"),
            new("Cyan (#00AAAA)", "#00AAAA"),
            new("Red (#AA0000)", "#AA0000"),
            new("Magenta (#AA00AA)", "#AA00AA"),
            new("Brown (#AA5500)", "#AA5500"),
            new("Light gray (#AAAAAA)", "#AAAAAA"),
            new("Dark gray (#555555)", "#555555"),
            new("Bright blue (#5555FF)", "#5555FF"),
            new("Bright green (#55FF55)", "#55FF55"),
            new("Bright cyan (#55FFFF)", "#55FFFF"),
            new("Bright red (#FF5555)", "#FF5555"),
            new("Bright magenta (#FF55FF)", "#FF55FF"),
            new("Yellow (#FFFF55)", "#FFFF55"),
            new("White (#FFFFFF)", "#FFFFFF"),
        ];
    }

    private static string FormatColor(ClassicMacRgbColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static WidgetColorChoice GetNearestColorChoice(
        string value,
        IReadOnlyList<WidgetColorChoice> choices)
    {
        var requested = ClassicMacRgbColor.TryParse(value, out var parsed)
            ? parsed
            : new ClassicMacRgbColor(255, 255, 255);
        return choices
            .MinBy(choice =>
            {
                var candidate = ClassicMacRgbColor.Parse(choice.Hex);
                var red = requested.Red - candidate.Red;
                var green = requested.Green - candidate.Green;
                var blue = requested.Blue - candidate.Blue;
                return red * red + green * green + blue * blue;
            }) ?? throw new InvalidOperationException("The target has no available colors.");
    }

    private void UpdateGeneratedCode()
    {
        if (TargetProfileSelector.SelectedItem is not MacTargetProfile profile)
        {
            return;
        }

        if (profile.GraphicsBackendId is not ("hercules" or "cga" or "vga"))
        {
            TaskCodeEditor.Text =
                "Design and target-specific colors are available, but this profile has no DOS LVGL code generator.";
            return;
        }

        try
        {
            var generated = FreeRTOSx86GuiCodeGenerator.Generate(
                profile,
                _controls,
                _widgetRegistry);
            TaskCodeEditor.Text = generated.TaskSource;
            UserEventsHeaderEditor.Text = generated.UserEventsHeader;
            if (!_userEventsCodeLoaded)
            {
                var existingUserEventsPath = _projectPath is null
                    ? null
                    : Path.Combine(
                        Path.GetDirectoryName(_projectPath)!,
                        "generated",
                        "user_events.c");
                UserEventsCodeEditor.Text = existingUserEventsPath is not null &&
                                            File.Exists(existingUserEventsPath)
                    ? File.ReadAllText(existingUserEventsPath)
                    : generated.UserEventsSource;
                _userEventsCodeLoaded = true;
            }
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            TaskCodeEditor.Text = $"Code generation error: {error.Message}";
        }
    }

    private void WriteGeneratedSources()
    {
        if (_projectPath is null)
        {
            throw new InvalidOperationException("Save the project before building it.");
        }

        var generated = FreeRTOSx86GuiCodeGenerator.Generate(
            SelectedProfile,
            _controls,
            _widgetRegistry);
        var directory = Path.Combine(Path.GetDirectoryName(_projectPath)!, "generated");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "gui_task.c"), generated.TaskSource);
        File.WriteAllText(Path.Combine(directory, "gui_task.h"), generated.TaskHeader);
        File.WriteAllText(Path.Combine(directory, "user_events.h"), generated.UserEventsHeader);
        File.WriteAllText(
            Path.Combine(directory, "lvgl_widget_config.h"),
            generated.LvglWidgetConfiguration);
        File.WriteAllLines(
            Path.Combine(directory, "lvgl_widget_sources.txt"),
            generated.WidgetSourcePaths.Select(path =>
                path.Replace('/', Path.DirectorySeparatorChar)));
        File.WriteAllText(
            Path.Combine(directory, "user_events.c"),
            UserEventsCodeEditor.Text);
    }

    private static int ReadPercentage(DesignerControl control, string propertyName) =>
        int.TryParse(
            control.Properties.GetValueOrDefault(propertyName),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static double ParseCoordinate(string value, string parameterName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            !double.IsFinite(result) ||
            result < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Enter a non-negative number.");
        }

        return result;
    }

    private static double ParseSize(string value, string parameterName)
    {
        var result = ParseCoordinate(value, parameterName);
        if (result <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Enter a positive number.");
        }

        return result;
    }

    private static string FindStudioRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "runtime", "8088", "build-and-run.ps1")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the FreeRTOSx86Studio runtime folder.");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void ShowError(string title, Exception error)
    {
        SetStatus($"{title}: {error.Message}");
        MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
}
