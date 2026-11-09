using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClassicMacIde.App;

public partial class SerialTerminalWindow : Window
{
    private static readonly Regex ComPortPattern = new(
        @"\ACOM[1-9][0-9]*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private SerialPort? _serialPort;

    public SerialTerminalWindow(string? basiliskSerialA, string? basiliskSerialB)
    {
        InitializeComponent();

        foreach (var portName in SerialPort.GetPortNames().OrderBy(
                     name => name,
                     StringComparer.OrdinalIgnoreCase))
        {
            TerminalComPortComboBox.Items.Add(portName);
            BasiliskComPortComboBox.Items.Add(portName);
        }

        if (!string.IsNullOrWhiteSpace(basiliskSerialA))
        {
            BasiliskChannelComboBox.SelectedIndex = 0;
            BasiliskComPortComboBox.Text = basiliskSerialA;
        }
        else if (!string.IsNullOrWhiteSpace(basiliskSerialB))
        {
            BasiliskChannelComboBox.SelectedIndex = 1;
            BasiliskComPortComboBox.Text = basiliskSerialB;
        }

        AppendOutput(
            "Terminal uses 8 data bits, no parity, one stop bit, and ASCII text.\r\n");
    }

    public event Action<string?, string?>? BasiliskSerialPortsChanged;

    private void ApplyBasiliskSettings_Click(object sender, RoutedEventArgs e) =>
        TryApplyBasiliskSettings();

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(BasiliskComPortComboBox.Text) &&
            !TryApplyBasiliskSettings())
        {
            return;
        }

        try
        {
            var terminalPortName = NormalizeComPort(TerminalComPortComboBox.Text);
            if (string.Equals(
                    terminalPortName,
                    NormalizeOptionalComPort(BasiliskComPortComboBox.Text),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The IDE and Basilisk endpoints must be different COM ports.");
            }

            var baudRate = GetSelectedBaudRate();
            var port = new SerialPort(
                terminalPortName,
                baudRate,
                Parity.None,
                8,
                StopBits.One)
            {
                Encoding = Encoding.ASCII,
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
            };
            port.DataReceived += SerialPort_DataReceived;
            try
            {
                port.Open();
                _serialPort = port;
            }
            catch (Exception error) when (
                error is UnauthorizedAccessException or
                    IOException or
                    ArgumentException or
                    InvalidOperationException or
                    TimeoutException or
                    Win32Exception)
            {
                port.DataReceived -= SerialPort_DataReceived;
                port.Dispose();
                throw;
            }

            UpdateConnectionControls();
            SetStatus($"Connected to {terminalPortName} at {baudRate} baud.");
            if (string.IsNullOrWhiteSpace(BasiliskComPortComboBox.Text))
            {
                AppendOutput(
                    "Basilisk COM endpoint is not configured; apply one to enable guest communication.\r\n");
            }
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
                IOException or
                ArgumentException or
                InvalidOperationException or
                TimeoutException or
                Win32Exception)
        {
            SetStatus($"Could not connect: {error.Message}");
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e) =>
        DisconnectSerialPort("Disconnected.");

    private void SendButton_Click(object sender, RoutedEventArgs e) =>
        SendInput();

    private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SendInput();
        }
    }

    private void SerialPort_DataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (sender is not SerialPort port)
        {
            return;
        }

        try
        {
            var receivedText = port.ReadExisting();
            if (receivedText.Length > 0)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_serialPort, port))
                    {
                        AppendOutput(NormalizeLineEndings(receivedText));
                    }
                });
            }
        }
        catch (Exception error) when (
            error is IOException or InvalidOperationException or TimeoutException)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(_serialPort, port))
                {
                    DisconnectSerialPort($"Serial read failed: {error.Message}");
                }
            });
        }
    }

    private bool TryApplyBasiliskSettings()
    {
        try
        {
            var basiliskPort = NormalizeComPort(BasiliskComPortComboBox.Text);
            var terminalPort = NormalizeOptionalComPort(TerminalComPortComboBox.Text);
            if (string.Equals(
                    basiliskPort,
                    terminalPort,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The IDE and Basilisk endpoints must be different COM ports.");
            }

            if (BasiliskChannelComboBox.SelectedItem is not ComboBoxItem channelItem ||
                channelItem.Tag is not string channel)
            {
                throw new InvalidOperationException("Select Basilisk serial A or serial B.");
            }

            var serialA = channel == "A" ? basiliskPort : null;
            var serialB = channel == "B" ? basiliskPort : null;
            BasiliskSerialPortsChanged?.Invoke(serialA, serialB);
            SetStatus(
                $"Basilisk will use {basiliskPort} on serial {channel}. The IDE must open the paired COM port.");
            return true;
        }
        catch (ArgumentException error)
        {
            SetStatus(error.Message);
            return false;
        }
        catch (InvalidOperationException error)
        {
            SetStatus(error.Message);
            return false;
        }
    }

    private void SendInput()
    {
        var port = _serialPort;
        if (port is null || !port.IsOpen)
        {
            SetStatus("Connect to the IDE-side COM port before sending text.");
            return;
        }

        var text = SendTextBox.Text
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace('\n', '\r');
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            var bytes = Encoding.ASCII.GetBytes(text + "\r");
            port.Write(bytes, 0, bytes.Length);
            AppendOutput($"> {text}\r\n");
            SendTextBox.Clear();
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
                IOException or
                InvalidOperationException or
                TimeoutException or
                Win32Exception)
        {
            DisconnectSerialPort($"Serial write failed: {error.Message}");
        }
    }

    private void DisconnectSerialPort(string status)
    {
        var port = _serialPort;
        _serialPort = null;
        if (port is not null)
        {
            port.DataReceived -= SerialPort_DataReceived;
            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch (Exception error) when (
                error is UnauthorizedAccessException or
                    IOException or
                    InvalidOperationException)
            {
                status += $" Close failed: {error.Message}";
            }
            finally
            {
                port.Dispose();
            }
        }

        UpdateConnectionControls();
        SetStatus(status);
    }

    private void SerialTerminalWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        DisconnectSerialPort("Terminal closed.");

    private void UpdateConnectionControls()
    {
        var connected = _serialPort?.IsOpen == true;
        ConnectButton.IsEnabled = !connected;
        DisconnectButton.IsEnabled = connected;
        SendButton.IsEnabled = connected;
    }

    private int GetSelectedBaudRate()
    {
        if (BaudRateComboBox.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Content?.ToString(), out var baudRate))
        {
            throw new InvalidOperationException("Select a supported baud rate.");
        }

        return baudRate;
    }

    private static string NormalizeComPort(string? portName)
    {
        var normalized = portName?.Trim().ToUpperInvariant() ?? string.Empty;
        return ComPortPattern.IsMatch(normalized)
            ? normalized
            : throw new ArgumentException(
                "Enter a Windows COM port such as COM3.");
    }

    private static string NormalizeOptionalComPort(string? portName) =>
        string.IsNullOrWhiteSpace(portName)
            ? string.Empty
            : NormalizeComPort(portName);

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal);

    private void AppendOutput(string text)
    {
        TerminalOutput.AppendText(text);
        TerminalOutput.ScrollToEnd();
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
