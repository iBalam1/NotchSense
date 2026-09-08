using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace NotchSense;

public partial class MainWindow : Window, IDisposable
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExNoActivate = 0x08000000;
    private readonly NotchSettings _settings = NotchSettings.Load();
    private readonly CancellationTokenSource _pollCts = new();
    private readonly DispatcherTimer _collapseTimer;
    private Forms.NotifyIcon? _trayIcon;
    private HwndSource? _source;
    private DetailWindow? _detail;
    private HardwareSnapshot _lastSnapshot = UnavailableSnapshot();
    private bool _isDiscrete;
    private bool _disposed;
    private volatile bool _sensorResetRequested;

    public MainWindow()
    {
        InitializeComponent();
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _collapseTimer.Tick += (_, _) => HideDetails();

        SourceInitialized += OnSourceInitialized;
        ContentRendered += (_, _) =>
        {
            // SizeToContent can settle one layout pass after ContentRendered.
            // Position again at Render priority so the first launch uses the same
            // dimensions as after an edge change. No visual geometry is changed.
            UpdateLayout();
            PositionWindow();
            Dispatcher.BeginInvoke(PositionWindow, DispatcherPriority.Render);
            Dispatcher.BeginInvoke(PositionWindow, DispatcherPriority.ContextIdle);
        };
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            Dispatcher.BeginInvoke(PositionWindow, DispatcherPriority.Render);
        };
        MouseEnter += (_, _) => _collapseTimer.Stop();
        MouseLeave += (_, _) => BeginCollapseDelay();
        Closed += (_, _) => Dispose();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        foreach (var ring in new[] { CpuRing, GpuRing, RamRing })
        {
            ring.MouseEnter += OnRingMouseEnter;
            ring.MouseLeave += (_, _) => BeginCollapseDelay();
        }

        ApplyEdgeLayout();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        SetExtendedStyle(WsExNoActivate, enabled: true);
        CreateTrayIcon();
        _ = Task.Run(() => PollSensorsAsync(_pollCts.Token));
    }

    private async Task PollSensorsAsync(CancellationToken token)
    {
        HardwareReader? reader = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (reader is null || _sensorResetRequested)
                {
                    reader?.Dispose();
                    reader = null;
                    _sensorResetRequested = false;
                    try { reader = new HardwareReader(); } catch { }
                }

                var read = reader?.Read() ?? UnavailableSnapshot();
                var snapshot = MergeSnapshot(read, _lastSnapshot);
                _lastSnapshot = snapshot;

                await Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (OperationCanceledException) { }
        finally { reader?.Dispose(); }
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
        CpuRing.SetReading(snapshot.Cpu);
        GpuRing.SetReading(snapshot.Gpu);
        RamRing.SetReading(snapshot.Ram);
        if (_detail?.IsVisible == true)
            _detail.Update(CurrentReading(_detail.MetricName), _settings.Edge);
    }

    private void OnRingMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDiscrete || sender is not RingControl ring) return;
        _collapseTimer.Stop();
        ShowDetails(ring.Reading, ring);
    }

    private void ShowDetails(MetricReading reading, RingControl ring)
    {
        _detail ??= CreateDetailWindow();
        _detail.Update(reading, _settings.Edge);
        PositionDetail(ring);
        if (!_detail.IsVisible) _detail.Show();
    }

    private DetailWindow CreateDetailWindow()
    {
        var detail = new DetailWindow();
        detail.PointerEntered += (_, _) => _collapseTimer.Stop();
        detail.PointerLeft += (_, _) => BeginCollapseDelay();
        return detail;
    }

    private void PositionDetail(RingControl ring)
    {
        if (_detail is null) return;
        var localCenter = ring.TranslatePoint(new Point(ring.ActualWidth / 2, ring.ActualHeight / 2), this);
        var ringCenter = PointToScreen(localCenter);
        const double gap = 6;
        _detail.Left = _settings.Edge == NotchEdge.Left
            ? Left + ActualWidth + gap
            : Left - _detail.Width - gap;
        _detail.Top = ringCenter.Y - _detail.Height / 2;
    }

    private MetricReading CurrentReading(string name) => name switch
    {
        "CPU" => CpuRing.Reading,
        "GPU" => GpuRing.Reading,
        "RAM" => RamRing.Reading,
        _ => MetricReading.Unavailable(name)
    };

    private void BeginCollapseDelay()
    {
        if (!_isDiscrete && _detail?.IsVisible == true)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void HideDetails()
    {
        _collapseTimer.Stop();
        _detail?.Hide();
    }

    private void ApplyEdgeLayout()
    {
        HideDetails();
        // Left/Right are the only supported placements. Keep the existing vertical layout.
        Width = 74;
        Height = 238;
        RingsPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
        Pill.CornerRadius = _settings.Edge switch
        {
            NotchEdge.Right => new CornerRadius(20, 0, 0, 20),
            NotchEdge.Left => new CornerRadius(0, 20, 20, 0),
            _ => new CornerRadius(20, 0, 0, 20)
        };
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            PositionWindow();
        }, DispatcherPriority.Loaded);
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        UpdateLayout();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        Left = _settings.Edge == NotchEdge.Left ? workArea.Left : workArea.Right - ActualWidth;
        Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
    }

    private void SetDiscreteMode(bool enabled)
    {
        _isDiscrete = enabled;
        HideDetails();
        // Keep the window geometry identical in both modes. Changing Padding here
        // changes SizeToContent and therefore moves the notch when it is edge-anchored.
        // The background alone is enough to make the notch visually disappear.
        Pill.Background = enabled ? Brushes.Transparent : Brushes.Black;
        Pill.Padding = new Thickness(8);
        SetExtendedStyle(WsExTransparent, enabled);
        PositionWindow();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var normal = new Forms.ToolStripMenuItem(Localization.NormalMode, null, (_, _) => Dispatcher.Invoke(() => SetDiscreteMode(false)));
        var discrete = new Forms.ToolStripMenuItem(Localization.DiscreteMode, null, (_, _) => Dispatcher.Invoke(() => SetDiscreteMode(true)));
        var edgeMenu = new Forms.ToolStripMenuItem(Localization.Edge);
        foreach (var edge in Enum.GetValues<NotchEdge>())
        {
            var label = edge == NotchEdge.Left ? Localization.Left : Localization.Right;
            edgeMenu.DropDownItems.Add(label, null, (_, _) => Dispatcher.Invoke(() =>
            {
                _settings.Edge = edge;
                _settings.Save();
                ApplyEdgeLayout();
            }));
        }
        var startup = new Forms.ToolStripMenuItem(Localization.Startup) { Checked = _settings.StartWithWindows, CheckOnClick = true };
        startup.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.StartWithWindows = startup.Checked;
            _settings.Save();
            StartupRegistration.Set(startup.Checked);
        });
        var exit = new Forms.ToolStripMenuItem(Localization.Exit, null, (_, _) => Dispatcher.Invoke(() =>
        {
            Close();
            Application.Current.Shutdown();
        }));
        menu.Items.AddRange(new Forms.ToolStripItem[] { normal, discrete, new Forms.ToolStripSeparator(), edgeMenu, startup, new Forms.ToolStripSeparator(), exit });
        var trayIcon = System.Drawing.SystemIcons.Application;
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath) ?? trayIcon;
            }
        }
        catch
        {
            // Fall back to the standard application icon if the executable icon
            // cannot be extracted (for example, during an unusual dev launch).
        }

        _trayIcon = new Forms.NotifyIcon { Icon = trayIcon, Text = "NotchSense", Visible = true, ContextMenuStrip = menu };
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _sensorResetRequested = true;
    }

    private void SetExtendedStyle(long flag, bool enabled)
    {
        if (_source is null) return;
        var style = GetWindowLongPtr(_source.Handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(_source.Handle, GwlExStyle, new IntPtr(enabled ? style | flag : style & ~flag));
    }

    private static HardwareSnapshot UnavailableSnapshot() => new(MetricReading.Unavailable("CPU"), MetricReading.Unavailable("GPU"), MetricReading.Unavailable("RAM"));
    private static HardwareSnapshot MergeSnapshot(HardwareSnapshot current, HardwareSnapshot previous) => new(
        MergeReading(current.Cpu, previous.Cpu),
        MergeReading(current.Gpu, previous.Gpu),
        MergeReading(current.Ram, previous.Ram));

    // LibreHardwareMonitor can return a partial tree during a normal refresh. A missing
    // field is not a zero and must not erase the last legitimate value from the UI.
    private static MetricReading MergeReading(MetricReading current, MetricReading previous)
    {
        var hasPercent = current.Percent is not null;
        var hasCard = current.CardPercent is not null || IsValue(current.CardValue);
        // CardLabel is semantic metadata, not a sensor value. Never let a previous
        // reading such as "Carga" overwrite the current CPU label "Temperatura"
        // merely because the temperature sensor is unavailable.
        var cardLabel = !string.IsNullOrWhiteSpace(current.CardLabel) ? current.CardLabel : previous.CardLabel;
        var cardValue = IsValue(current.CardValue) ? current.CardValue :
                        current.CardLabel == "Temperatura" ? "—" : previous.CardValue;
        return current with
        {
            Percent = current.Percent ?? previous.Percent,
            PrimaryDetail = IsValue(current.PrimaryDetail) ? current.PrimaryDetail : previous.PrimaryDetail,
            SecondaryDetail = IsValue(current.SecondaryDetail) ? current.SecondaryDetail : previous.SecondaryDetail,
            Model = IsModel(current.Model, current.Name) ? current.Model : previous.Model,
            CardPercent = current.CardPercent ?? previous.CardPercent,
            CardLabel = cardLabel,
            CardValue = cardValue,
            CardDescription = IsValue(current.CardDescription) ? current.CardDescription :
                              current.CardLabel == "Temperatura" ? "Sensor no disponible" : previous.CardDescription,
            IsStale = !hasPercent || !hasCard
        };
    }

    private static bool IsValue(string value) => !string.IsNullOrWhiteSpace(value) && value != "—" && value != "Sensor no disponible";
    private static bool IsModel(string value, string metric) => !string.IsNullOrWhiteSpace(value) && value != metric && value != "Virtual Memory";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _pollCts.Cancel();
        _detail?.Close();
        _trayIcon?.Dispose();
        _pollCts.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}
