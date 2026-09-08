using System.Globalization;

namespace NotchSense;

internal static class Localization
{
    public static bool IsSpanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase);

    public static string NormalMode => IsSpanish ? "Modo normal" : "Normal mode";
    public static string DiscreteMode => IsSpanish ? "Modo discreto" : "Discrete mode";
    public static string Edge => IsSpanish ? "Borde" : "Edge";
    public static string Left => IsSpanish ? "Izquierda" : "Left";
    public static string Right => IsSpanish ? "Derecha" : "Right";
    public static string Startup => IsSpanish ? "Iniciar con Windows" : "Start with Windows";
    public static string Exit => IsSpanish ? "Salir" : "Exit";
    public static string RamUsage => IsSpanish ? "Uso de RAM" : "RAM usage";
    public static string Frequency => IsSpanish ? "FRECUENCIA" : "FREQUENCY";
    public static string Temperature => IsSpanish ? "TEMPERATURA" : "TEMPERATURE";
    public static string Load => IsSpanish ? "Carga" : "Load";
    public static string Ram => IsSpanish ? "Memoria RAM" : "RAM";
    public static string InUse(double percent) => IsSpanish ? $"{Math.Round(percent):0}% en uso" : $"{Math.Round(percent):0}% in use";
    public static string SensorUnavailable => IsSpanish ? "Sensor no disponible" : "Sensor unavailable";
}
