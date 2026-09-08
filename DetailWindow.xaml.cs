using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NotchSense;

public partial class DetailWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;

    public event EventHandler? PointerEntered;
    public event EventHandler? PointerLeft;
    public string MetricName { get; private set; } = string.Empty;

    public DetailWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = ((HwndSource)PresentationSource.FromVisual(this)!).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() | WsExNoActivate;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        };
        MouseEnter += (_, _) => PointerEntered?.Invoke(this, EventArgs.Empty);
        MouseLeave += (_, _) => PointerLeft?.Invoke(this, EventArgs.Empty);
    }

    public void Update(MetricReading reading, NotchEdge edge)
    {
        // Keep the stable metric key separate from the human-readable model name.
        // The model (e.g. "NVIDIA GeForce RTX 4060") is not a valid key for
        // MainWindow.CurrentReading() on the next sensor refresh.
        MetricName = reading.Name;
        HeaderText.Text = string.IsNullOrWhiteSpace(reading.Model) ? reading.Name : reading.Model;
        CardLabel.Text = reading.CardLabel == "Carga" ? Localization.Load : reading.CardLabel;
        CardValue.Text = reading.CardValue;
        UsageText.Text = reading.CardDescription;
        UsageFill.Width = reading.CardPercent is { } percent ? Math.Round(216 * Math.Clamp(percent, 0, 100) / 100) : 0;
        UsageFill.Background = new SolidColorBrush(MetricReading.ColorFor(reading.CardPercent));

        if (reading.Name == "RAM")
        {
            // Keep the useful used/total figure beside the usage label and bar,
            // but remove the duplicated lower memory readout.
            CardLabel.Text = Localization.RamUsage;
            CardValue.Text = reading.CardValue;
            // Keep the same vertical rhythm as CPU/GPU without reintroducing
            // the duplicated memory readout. The usage percentage remains
            // represented by the ring and the bar/value row above.
            UsageText.Visibility = Visibility.Visible;
            UsageText.Text = reading.CardPercent is { } ramPercent
                ? Localization.InUse(ramPercent)
                : "—";
            DetailsGrid.Margin = new Thickness(0);
            PrimaryCaption.Text = Localization.Frequency;
            PrimaryValue.Text = reading.SecondaryDetail;
            SecondaryPanel.Visibility = Visibility.Collapsed;
        }
        else if (reading.Name == "CPU")
        {
            DetailsGrid.Margin = new Thickness(0);
            UsageText.Visibility = Visibility.Visible;
            PrimaryCaption.Text = Localization.Frequency;
            PrimaryValue.Text = reading.PrimaryDetail;
            SecondaryPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailsGrid.Margin = new Thickness(0);
            UsageText.Visibility = Visibility.Visible;
            PrimaryCaption.Text = Localization.Frequency;
            PrimaryValue.Text = reading.PrimaryDetail;
            SecondaryCaption.Text = Localization.Temperature;
            SecondaryValue.Text = reading.SecondaryDetail;
            SecondaryPanel.Visibility = Visibility.Visible;
        }

        ConfigureTail(edge);
    }

    private void ConfigureTail(NotchEdge edge)
    {
        const double tail = 12;
        Surface.Width = 260;
        Surface.Height = 154;
        Width = Surface.Width;
        Height = Surface.Height;
        Canvas.SetLeft(Card, edge == NotchEdge.Left ? tail : 0);
        Canvas.SetTop(Card, 0);

        Tail.Points = edge switch
        {
            NotchEdge.Right => new PointCollection { new(248, 68), new(260, 77), new(248, 86) },
            _ => new PointCollection { new(12, 68), new(0, 77), new(12, 86) }
        };
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
}
