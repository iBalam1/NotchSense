using System.Windows.Media;

namespace NotchSense;

public sealed record MetricReading(
    string Name,
    double? Percent,
    string PrimaryDetail,
    string SecondaryDetail,
    bool IsStale = false,
    string Model = "",
    double? CardPercent = null,
    string CardLabel = "Carga",
    string CardValue = "—",
    string CardDescription = "")
{
    public string DisplayPercent => Percent is { } value ? $"{Math.Round(value):0}%" : "—";

    public Color Color => ColorFor(Percent);

    public static Color ColorFor(double? value) => value switch
    {
        null => Color.FromRgb(135, 142, 154),
        < 50 => Color.FromRgb(67, 201, 123),
        < 80 => Color.FromRgb(242, 199, 76),
        _ => Color.FromRgb(248, 121, 80)
    };

    public static MetricReading Unavailable(string name) => new(name, null, "—", "—", Model: name, CardValue: "—", CardDescription: Localization.SensorUnavailable);
}

public sealed record HardwareSnapshot(MetricReading Cpu, MetricReading Gpu, MetricReading Ram);
