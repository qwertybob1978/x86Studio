using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Windows;
using Microsoft.Win32;
using ClassicMacIde.Core;

namespace ClassicMacIde.App;

public partial class SerialAgentConnectionWindow : Window
{
    private const uint PutCapability = 1U << 0;
    private const uint RunCapability = 1U << 1;
    private const uint StopCapability = 1U << 2;
    private const ushort Dos8088MachineType = 0x8088;
    private const ushort Dos386MachineType = 0x0386;

    private readonly string? _preferredPortName;
    private readonly bool _paceSerialWrites;
    private SerialPort? _serialPort;
    private CancellableSerialPortStream? _serialStream;
    private MacStudioAgentSession? _session;
    private CancellationTokenSource? _connectionCancellation;
    private CancellationTokenSource? _transferCancellation;
    private CancellationTokenSource? _commandCancellation;
    private CancellationTokenSource? _debugReceiveCancellation;
    private Task? _debugReceiveTask;
    private byte[]? _lastBuildId;
    private uint _agentCapabilities;
    private bool _isConnecting;
    private bool _isTransferring;
    private bool _isCommandInProgress;
    private bool _isApplicationRunning;
    private bool _closeAfterTransfer;
    private bool _closeAfterCommand;
    private bool _isClosing;

    public SerialAgentConnectionWindow(
        string? preferredPortName = null,
        bool paceSerialWrites = false)
    {
        InitializeComponent();
        _preferredPortName = preferredPortName;
        _paceSerialWrites = paceSerialWrites;
        RefreshPorts();
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e) =>
        RefreshPorts();

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) =>
        await ConnectToAgentAsync();

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectButton.IsEnabled = false;
        try
        {
            await StopDebugReceiverAsync();
            if (_session?.State == MacStudioAgentConnectionState.Connected)
            {
                await _session.DisconnectAsync();
            }

            SetStatus("Disconnected.");
        }
        catch (Exception error) when (
            error is IOException or
                InvalidOperationException or
                TimeoutException or
                UnauthorizedAccessException or
                Win32Exception)
        {
            SetStatus($"Disconnect failed: {error.Message}");
        }
        finally
        {
            ResetConnection();
            UpdateControls();
        }
    }

    private async void SendPackageButton_Click(object sender, RoutedEventArgs e) =>
        await SendPackageAsync();

    private async void RunButton_Click(object sender, RoutedEventArgs e) =>
        await SendBuildCommandAsync(stop: false);

    private async void StopButton_Click(object sender, RoutedEventArgs e) =>
        await SendBuildCommandAsync(stop: true);

    private void CancelTransferButton_Click(object sender, RoutedEventArgs e)
    {
        _transferCancellation?.Cancel();
        SetStatus("Cancelling the active transfer...");
    }

    private async void SerialAgentConnectionWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_isTransferring)
        {
            e.Cancel = true;
            _closeAfterTransfer = true;
            _transferCancellation?.Cancel();
            SetStatus("Cancelling the active transfer before closing...");
            return;
        }

        if (_isCommandInProgress)
        {
            e.Cancel = true;
            _isClosing = true;
            _closeAfterCommand = true;
            _commandCancellation?.Cancel();
            SetStatus("Cancelling the active Agent command before closing...");
            return;
        }

        if (_debugReceiveTask is not null)
        {
            e.Cancel = true;
            _isClosing = true;
            await StopDebugReceiverAsync();
            ResetConnection();
            Close();
            return;
        }

        _isClosing = true;
        _connectionCancellation?.Cancel();
        ResetConnection();
    }

    private async Task SendPackageAsync()
    {
        if (_isTransferring ||
            _session?.State != MacStudioAgentConnectionState.Connected)
        {
            return;
        }

        var destinationName = DestinationNameTextBox.Text.Trim();
        var isDosAgent = IsDosAgent(_session?.AgentInfo?.MachineType);
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = isDosAgent
                ? "DOS files (*.exe;*.com;*.bin)|*.exe;*.com;*.bin|All files (*.*)|*.*"
                : "MacBinary II packages (*.bin;*.macbin)|*.bin;*.macbin|All files (*.*)|*.*",
            Multiselect = false,
            Title = isDosAgent
                ? "Select a file to transfer to DOS"
                : "Select a MacBinary II application package",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var session = _session;
        if (session?.State != MacStudioAgentConnectionState.Connected)
        {
            SetStatus("The Agent disconnected before the transfer could start.");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _transferCancellation = cancellation;
        _isTransferring = true;
        UpdateControls();
        SetStatus($"Sending {Path.GetFileName(dialog.FileName)} to {destinationName}...");

        try
        {
            await StopDebugReceiverAsync();
            ReadOnlyMemory<byte> buildId;
            uint fileLength;
            uint fileCrc32;
            string completionMessage;
            if (isDosAgent)
            {
                var file = await session.PutFileAsync(
                    dialog.FileName,
                    destinationName,
                    cancellation.Token);
                buildId = file.BuildId;
                fileLength = file.FileLength;
                fileCrc32 = file.FileCrc32;
                completionMessage = $"Transferred {destinationName} ({fileLength:N0} bytes).";
            }
            else
            {
                var package = await session.PutAsync(
                    dialog.FileName,
                    destinationName,
                    cancellation.Token);
                buildId = package.BuildId;
                fileLength = package.PackageLength;
                fileCrc32 = package.PackageCrc32;
                completionMessage = $"Installed {destinationName} ({fileLength:N0} bytes).";
            }

            if (_isClosing)
            {
                return;
            }

            _lastBuildId = buildId.ToArray();
            _isApplicationRunning = false;
            SetStatus(completionMessage);
            AgentDetailsText.Text += string.Format(
                CultureInfo.InvariantCulture,
                "\nLast transfer: CRC-32 {0:X8}; Build ID {1}.",
                fileCrc32,
                Convert.ToHexString(buildId.Span));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("Transfer cancelled.");
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
                IOException or
                InvalidDataException or
                ArgumentException or
                InvalidOperationException or
                TimeoutException or
                Win32Exception)
        {
            SetStatus($"Transfer failed: {error.Message}");
        }
        finally
        {
            if (ReferenceEquals(_transferCancellation, cancellation))
            {
                _transferCancellation = null;
            }

            _isTransferring = false;
            UpdateControls();
            if (!_isClosing &&
                _isApplicationRunning &&
                _session is { State: MacStudioAgentConnectionState.Connected } activeSession)
            {
                StartDebugReceiver(activeSession);
            }
            if (_closeAfterTransfer)
            {
                Close();
            }
        }
    }

    private async Task SendBuildCommandAsync(bool stop)
    {
        var session = _session;
        var buildId = _lastBuildId;
        if (_isTransferring ||
            _isCommandInProgress ||
            buildId is null ||
            session is null ||
            session.State != MacStudioAgentConnectionState.Connected)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _commandCancellation = cancellation;
        _isCommandInProgress = true;
        UpdateControls();
        SetStatus(stop ? "Requesting the application to quit..." : "Launching the installed application...");

        try
        {
            await StopDebugReceiverAsync();
            var resultCode = stop
                ? await session.StopAsync(buildId, cancellation.Token)
                : await session.RunAsync(buildId, cancellation.Token);
            if (_isClosing)
            {
                return;
            }

            if (stop && resultCode == 0)
            {
                _isApplicationRunning = false;
                SetStatus("The Agent accepted the application quit request.");
            }
            else if (!stop && resultCode == 0)
            {
                _isApplicationRunning = true;
                SetStatus("The Agent launched the installed application.");
            }
            else
            {
                if (stop && resultCode == 1)
                {
                    _isApplicationRunning = false;
                }

                SetStatus(stop
                    ? $"Stop request failed (Agent result {resultCode})."
                    : $"Launch request failed (Agent result {resultCode}).");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isClosing)
            {
                SetStatus("Agent command cancelled.");
            }
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
                IOException or
                InvalidDataException or
                ArgumentException or
                InvalidOperationException or
                TimeoutException or
                Win32Exception)
        {
            if (!_isClosing)
            {
                SetStatus($"Agent command failed: {error.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_commandCancellation, cancellation))
            {
                _commandCancellation = null;
            }

            _isCommandInProgress = false;
            UpdateControls();
            if (!_isClosing &&
                _session is { State: MacStudioAgentConnectionState.Connected } activeSession)
            {
                StartDebugReceiver(activeSession);
            }
            if (_closeAfterCommand)
            {
                Close();
            }
        }
    }

    private void StartDebugReceiver(MacStudioAgentSession session)
    {
        if (_debugReceiveTask is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _debugReceiveCancellation = cancellation;
        _debugReceiveTask = ReceiveDebugRecordsAsync(session, cancellation.Token);
    }

    private async Task StopDebugReceiverAsync()
    {
        var receiveTask = _debugReceiveTask;
        if (receiveTask is null)
        {
            return;
        }

        var cancellation = _debugReceiveCancellation;
        _debugReceiveTask = null;
        _debugReceiveCancellation = null;
        if (cancellation is null)
        {
            await receiveTask;
            return;
        }

        cancellation.Cancel();
        try
        {
            await receiveTask;
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ReceiveDebugRecordsAsync(
        MacStudioAgentSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var frame = await session.ReceiveAsync(cancellationToken);
                if (frame.MessageType != (byte)MacStudioAgentMessageType.DebugRecord)
                {
                    SetStatus($"Received unexpected Agent message 0x{frame.MessageType:X2}.");
                    continue;
                }

                var record = MacStudioAgentDebugRecordCodec.Decode(frame);
                await session.SendAsync(
                    MacStudioAgentDebugRecordCodec.CreateAcknowledgment(
                        session.SessionId,
                        record.Sequence,
                        0),
                    cancellationToken);
                AppendDebugRecord(record);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (
            error is IOException or
                InvalidOperationException or
                UnauthorizedAccessException)
        {
            if (!_isClosing)
            {
                SetStatus($"Debug receive failed: {error.Message}");
            }
        }
    }

    private void AppendDebugRecord(MacStudioAgentDebugRecord record)
    {
        const int maximumLogCharacters = 16_384;
        var entry = string.Format(
            CultureInfo.InvariantCulture,
            "[{0:D8}] severity {1}: {2}{3}",
            record.Sequence,
            record.Severity,
            record.Message,
            Environment.NewLine);
        var text = DebugLogTextBox.Text + entry;
        if (text.Length > maximumLogCharacters)
        {
            var start = text.IndexOf(
                '\n',
                text.Length - maximumLogCharacters);
            text = start >= 0
                ? text[(start + 1)..]
                : text[^maximumLogCharacters..];
        }

        DebugLogTextBox.Text = text;
        DebugLogTextBox.CaretIndex = text.Length;
        DebugLogTextBox.ScrollToEnd();
    }

    private async Task ConnectToAgentAsync()
    {
        if (_isConnecting || _session is not null)
        {
            return;
        }

        int baudRate;
        try
        {
            baudRate = GetSelectedBaudRate();
        }
        catch (InvalidOperationException error)
        {
            SetStatus(error.Message);
            return;
        }

        if (ComPortComboBox.SelectedItem is not string portName)
        {
            SetStatus("Select an available PC COM port.");
            return;
        }

        _isConnecting = true;
        using var cancellation = new CancellationTokenSource();
        _connectionCancellation = cancellation;
        UpdateControls();
        SetStatus($"Connecting to {portName} at {baudRate:N0} baud...");
        AgentDetailsText.Text = "Waiting for the Agent handshake.";

        try
        {
            var port = new SerialPort(
                portName,
                baudRate,
                Parity.None,
                8,
                StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
            };
            _serialPort = port;
            port.Open();

            var serialStream = new CancellableSerialPortStream(
                port,
                _paceSerialWrites ? TimeSpan.FromMilliseconds(10) : TimeSpan.Zero);
            _serialStream = serialStream;
            var session = new MacStudioAgentSession(serialStream);
            _session = session;
            var agent = await session.ConnectAsync(cancellation.Token);
            if (_isClosing)
            {
                return;
            }

            if ((agent.SupportedBaudRates &
                MacStudioAgentBaudRates.GetCapabilityFlag(baudRate)) == 0)
            {
                throw new InvalidDataException(
                    $"The Agent does not advertise support for {baudRate:N0} baud.");
            }

            _agentCapabilities = agent.Capabilities;
            if (IsDosAgent(agent.MachineType))
            {
                DestinationNameLabel.Text = "DOS file name:";
                if (string.IsNullOrWhiteSpace(DestinationNameTextBox.Text) ||
                    DestinationNameTextBox.Text == "Application")
                {
                    DestinationNameTextBox.Text = "APP.EXE";
                }
                SendPackageButton.Content = "Send File...";
            }
            else
            {
                DestinationNameLabel.Text = "Target app name:";
                SendPackageButton.Content = "Send Package...";
            }
            var targetDescription = agent.MachineType switch
            {
                0x8088 => "DOS / 8088",
                0x0386 => "DOS / 80386",
                _ => $"machine 0x{agent.MachineType:X4}",
            };
            SetStatus($"Connected to the Studio Agent ({targetDescription}) on {portName}.");
            AgentDetailsText.Text =
                $"Agent v{agent.AgentMajor}.{agent.AgentMinor}.{agent.AgentPatch} " +
                $"(protocol {agent.ProtocolVersion})\n" +
                $"Target: {targetDescription}; system {agent.SystemMajor}.{agent.SystemMinor}\n" +
                $"Capabilities: 0x{agent.Capabilities:X8}; free bytes: " +
                $"{agent.FreeBytes.ToString("N0", CultureInfo.InvariantCulture)}\n" +
                $"Supported baud flags: 0x{agent.SupportedBaudRates:X8}\n" +
                $"Connection baud rate: {baudRate:N0}";
            StartDebugReceiver(session);
        }
        catch (OperationCanceledException)
        {
            if (!_isClosing)
            {
                SetStatus("Connection cancelled.");
            }
            ResetConnection();
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
                IOException or
                InvalidDataException or
                ArgumentException or
                InvalidOperationException or
                TimeoutException or
                Win32Exception)
        {
            if (!_isClosing)
            {
                SetStatus($"Could not connect: {error.Message}");
            }
            ResetConnection();
        }
        finally
        {
            if (ReferenceEquals(_connectionCancellation, cancellation))
            {
                _connectionCancellation = null;
            }

            _isConnecting = false;
            UpdateControls();
        }
    }

    private void RefreshPorts()
    {
        try
        {
            var selectedPort = ComPortComboBox.SelectedItem as string;
            var ports = SerialPort.GetPortNames()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ComPortComboBox.Items.Clear();
            foreach (var port in ports)
            {
                ComPortComboBox.Items.Add(port);
            }

            if (selectedPort is not null &&
                ports.Contains(selectedPort, StringComparer.OrdinalIgnoreCase))
            {
                ComPortComboBox.SelectedItem = ports.First(
                    port => string.Equals(
                        port,
                        selectedPort,
                        StringComparison.OrdinalIgnoreCase));
            }
            else if (ports.Length > 0)
            {
                var preferredPort = _preferredPortName is null
                    ? null
                    : ports.FirstOrDefault(port => string.Equals(
                        port,
                        _preferredPortName,
                        StringComparison.OrdinalIgnoreCase));
                ComPortComboBox.SelectedItem = preferredPort ?? ports[0];
            }

            SetStatus(ports.Length == 0
                ? "No PC COM ports are available. Connect an adapter or create a virtual COM pair, then refresh."
                : $"{ports.Length} PC COM port(s) available.");
        }
        catch (Win32Exception error)
        {
            SetStatus($"Could not enumerate PC COM ports: {error.Message}");
        }
    }

    private void ResetConnection()
    {
        _session = null;
        _agentCapabilities = 0U;
        var serialStream = _serialStream;
        _serialStream = null;
        serialStream?.Dispose();
        var port = _serialPort;
        _serialPort = null;
        port?.Dispose();
    }

    private void UpdateControls()
    {
        var connected = _session?.State == MacStudioAgentConnectionState.Connected;
        ConnectButton.IsEnabled = !_isConnecting && !connected;
        DisconnectButton.IsEnabled = connected && !_isTransferring && !_isCommandInProgress;
        SendPackageButton.IsEnabled = connected &&
            (_agentCapabilities & PutCapability) != 0U &&
            !_isTransferring &&
            !_isCommandInProgress &&
            !_isApplicationRunning;
        CancelTransferButton.IsEnabled = _isTransferring;
        RunButton.IsEnabled = connected &&
            (_agentCapabilities & RunCapability) != 0U &&
            !_isTransferring &&
            !_isCommandInProgress &&
            !_isApplicationRunning &&
            _lastBuildId is not null;
        StopButton.IsEnabled = connected &&
            (_agentCapabilities & StopCapability) != 0U &&
            !_isTransferring &&
            !_isCommandInProgress &&
            _isApplicationRunning;
        DestinationNameTextBox.IsEnabled =
            connected &&
            (_agentCapabilities & PutCapability) != 0U &&
            !_isTransferring &&
            !_isCommandInProgress;
        ComPortComboBox.IsEnabled = !_isConnecting && !connected;
        BaudRateComboBox.IsEnabled = !_isConnecting && !connected;
        RefreshPortsButton.IsEnabled = !_isConnecting && !connected;
    }

    private int GetSelectedBaudRate()
    {
        if (BaudRateComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item ||
            !int.TryParse(
                item.Content?.ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var baudRate))
        {
            throw new InvalidOperationException("Select a supported Studio Agent baud rate.");
        }

        _ = MacStudioAgentBaudRates.GetCapabilityFlag(baudRate);
        return baudRate;
    }

    private void SetStatus(string status)
    {
        if (!_isClosing)
        {
            StatusText.Text = status;
        }
    }

    private static bool IsDosAgent(ushort? machineType) =>
        machineType is Dos8088MachineType or Dos386MachineType;
}
