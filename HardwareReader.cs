using LibreHardwareMonitor.Hardware;
using System.Management;
using System.Runtime.InteropServices;

namespace NotchSense;

public sealed class HardwareReader : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true
    };
    private string? _selectedGpuId;
    private bool _opened;

    public HardwareReader()
    {
        _computer.Open();
        _opened = true;
    }

    public HardwareSnapshot Read()
    {
        try
        {
            UpdateAll();
            var all = EnumerateHardware(_computer.Hardware).ToList();
            var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            var memory = all.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            var gpu = SelectGpu(all);

            return new HardwareSnapshot(
                ReadCpu(cpu),
                ReadGpu(gpu),
                ReadRam(memory));
        }
        catch
        {
            return new HardwareSnapshot(
                MetricReading.Unavailable("CPU"),
                MetricReading.Unavailable("GPU"),
                MetricReading.Unavailable("RAM"));
        }
    }

    private void UpdateAll()
    {
        foreach (var hardware in _computer.Hardware)
            hardware.Accept(new UpdateVisitor());
    }

    private IHardware? SelectGpu(IReadOnlyCollection<IHardware> hardware)
    {
        var gpus = hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToList();
        if (gpus.Count == 0) return null;

        if (_selectedGpuId is not null)
            return gpus.FirstOrDefault(g => g.Identifier.ToString() == _selectedGpuId);

        // Prefer a discrete GPU. If there are several, use a short initial reading as a tie-breaker,
        // then keep that choice for this whole session so the ring cannot jump between adapters.
        var candidates = gpus.Where(g => g.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd).ToList();
        if (candidates.Count == 0) candidates = gpus;
        var selected = candidates
            .OrderByDescending(GetLoad)
            .First();
        _selectedGpuId = selected.Identifier.ToString();
        return selected;
    }

    private static MetricReading ReadCpu(IHardware? cpu)
    {
        if (cpu is null) return MetricReading.Unavailable("CPU");
        var load = FindSensor(cpu, SensorType.Load, "CPU Total") ?? FindSensor(cpu, SensorType.Load);
        // Never fall back to the CPU Bus Speed (usually ~100 MHz). If LHM cannot
        // expose a core clock, use Windows' current processor-frequency API instead.
        var clock = FindLiveCpuClock(cpu);
        var temperature = FindSensorRecursive(cpu, SensorType.Temperature, "CPU Package")
                          ?? FindSensorRecursive(cpu, SensorType.Temperature, "Core Max")
                          ?? FindSensorRecursive(cpu, SensorType.Temperature, "Core Average")
                          ?? FindSensorRecursive(cpu, SensorType.Temperature, "Core #1")
                          ?? FindSensorRecursive(cpu, SensorType.Temperature);
        var cpuClockMhz = clock ?? GetWindowsCurrentProcessorClockMhzPerf() ?? GetWindowsCurrentProcessorClockMhz();
        var cpuTemperature = temperature?.Value;
        var temperatureText = FormatTemperature(cpuTemperature);
        return new MetricReading("CPU", load?.Value, FormatClock(cpuClockMhz), temperatureText,
            Model: cpu.Name,
            CardPercent: cpuTemperature,
            CardLabel: Localization.Temperature,
            CardValue: temperatureText,
            CardDescription: cpuTemperature is { } tempValue ? $"{tempValue:0} °C" : Localization.SensorUnavailable);
    }

    private static MetricReading ReadGpu(IHardware? gpu)
    {
        if (gpu is null) return MetricReading.Unavailable("GPU");
        var load = FindSensor(gpu, SensorType.Load, "GPU Core") ?? FindSensor(gpu, SensorType.Load);
        var clock = FindSensor(gpu, SensorType.Clock, "GPU Core") ?? FindSensor(gpu, SensorType.Clock);
        var temperature = FindSensor(gpu, SensorType.Temperature, "GPU Core") ?? FindSensor(gpu, SensorType.Temperature);
        var usedVram = FindSensor(gpu, SensorType.SmallData, "GPU Memory Used")?.Value;
        var totalVram = FindSensor(gpu, SensorType.SmallData, "GPU Memory Total")?.Value;
        var vramPercent = usedVram is { } used && totalVram is > 0 ? used / totalVram * 100 : null;
        var vramText = usedVram is { } usedValue && totalVram is { } totalValue && totalValue > 0
            ? $"{usedValue / 1024:0.0} / {totalValue / 1024:0.0} GB"
            : "—";
        return new MetricReading("GPU", load?.Value, FormatClock(clock?.Value), FormatTemperature(temperature?.Value),
            Model: gpu.Name,
            CardPercent: vramPercent,
            CardLabel: "VRAM",
            CardValue: vramText,
            CardDescription: vramPercent is { } value ? (Localization.IsSpanish ? $"{value:0}% de VRAM en uso" : $"{value:0}% VRAM in use") : (Localization.IsSpanish ? "VRAM no disponible" : "VRAM unavailable"));
    }

    private static MetricReading ReadRam(IHardware? memory)
    {
        if (memory is null) return MetricReading.Unavailable("RAM");
        var physical = GetPhysicalMemory();
        var totalGb = physical.TotalBytes / 1024d / 1024 / 1024;
        var usedGb = (physical.TotalBytes - physical.AvailableBytes) / 1024d / 1024 / 1024;
        var percent = physical.TotalBytes > 0 ? usedGb / totalGb * 100 : (double?)null;
        var details = totalGb > 0 ? $"{usedGb:0.0} / {totalGb:0.0} GB" : "—";
        // ConfiguredClockSpeed is the BIOS/UEFI memory speed Windows reports for
        // the installed DIMMs (e.g. 3200 MHz). LHM's Memory Clock can be a raw
        // controller/clock value and is therefore only a fallback here.
        var configuredClock = GetConfiguredMemoryClockMhz();
        var clock = configuredClock ?? FindSensor(memory, SensorType.Clock, "Memory Clock")?.Value;
        return new MetricReading("RAM", percent, details, FormatMemoryClock(clock),
            Model: "Memoria RAM",
            CardPercent: percent,
            CardLabel: Localization.RamUsage,
            CardValue: details,
            CardDescription: "");
    }

    private static float? FindLiveCpuClock(IHardware cpu)
    {
        var clocks = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock &&
                        (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                         s.Name.Contains("P-Core", StringComparison.OrdinalIgnoreCase) ||
                         s.Name.Contains("E-Core", StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.Value)
            .Where(v => v is > 0)
            .Select(v => v!.Value)
            .ToList();

        // The highest live core clock is a better representation of current CPU
        // frequency than the nominal/base clock or the ~100 MHz bus clock.
        return clocks.Count > 0 ? clocks.Max() : GetWindowsCurrentProcessorClockMhz();
    }

    private static ISensor? FindSensorRecursive(IHardware hardware, SensorType type, string? preferredName = null)
    {
        var local = FindSensor(hardware, type, preferredName);
        if (local is not null) return local;

        foreach (var child in hardware.SubHardware)
        {
            var nested = FindSensorRecursive(child, type, preferredName);
            if (nested is not null) return nested;
        }

        return null;
    }

    private static ISensor? FindSensor(IHardware hardware, SensorType type, string? preferredName = null)
    {
        var matches = hardware.Sensors
            .Where(s => s.SensorType == type && (preferredName is null || s.Name.Contains(preferredName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // LibreHardwareMonitor can expose several sensors with the same logical name
        // while only some have a value. Prefer a live sensor so a null first entry
        // does not hide a valid reading from another core/package sensor.
        return matches
            .OrderByDescending(s => s.Value.HasValue)
            .FirstOrDefault();
    }

    private static float GetLoad(IHardware hardware) => FindSensor(hardware, SensorType.Load, "GPU Core")?.Value ?? 0;
    private static string FormatClock(float? value) => value is { } clock ? $"{clock / 1000:0.00} GHz" : "—";
    private static string FormatTemperature(float? value) => value is { } temp ? $"{temp:0} °C" : "—";

    // Windows exposes the current processor frequency through CallNtPowerInformation.
    // This gives us a lightweight fallback when LibreHardwareMonitor cannot read
    // the CPU clock sensor (for example when its low-level access is unavailable).
    private static float? GetWindowsCurrentProcessorClockMhzPerf()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
            foreach (ManagementObject item in searcher.Get())
            {
                if (item["ProcessorFrequency"] is not null &&
                    float.TryParse(item["ProcessorFrequency"]!.ToString(), out var mhz) && mhz > 0)
                    return mhz;
            }
        }
        catch { }
        return null;
    }

    private static float? GetWindowsCurrentProcessorClockMhz()
    {
        try
        {
            var count = Environment.ProcessorCount;
            var size = Marshal.SizeOf<ProcessorPowerInformation>();
            var buffer = Marshal.AllocHGlobal(size * count);
            try
            {
                var status = CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, (uint)(size * count));
                if (status != 0) return null;

                double total = 0;
                var valid = 0;
                for (var i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<ProcessorPowerInformation>(IntPtr.Add(buffer, i * size));
                    if (info.CurrentMhz > 0)
                    {
                        total += info.CurrentMhz;
                        valid++;
                    }
                }

                return valid > 0 ? (float)(total / valid) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    private static float? GetConfiguredMemoryClockMhz()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");

            var speeds = new List<double>();

            foreach (ManagementObject module in searcher.Get())
            {
                if (module["ConfiguredClockSpeed"] is not null &&
                    double.TryParse(module["ConfiguredClockSpeed"]!.ToString(), out var configured) &&
                    configured > 0)
                {
                    speeds.Add(configured);
                    continue;
                }

                if (module["Speed"] is not null &&
                    double.TryParse(module["Speed"]!.ToString(), out var speed) &&
                    speed > 0)
                {
                    speeds.Add(speed);
                }
            }

            return speeds.Count > 0 ? (float)speeds.Max() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatMemoryClock(float? value) => value is { } clock ? $"{clock:0} MT/s" : "—";
    private static string FormatLoad(float? value) => value is { } load ? $"{load:0}%" : "—";

    private static PhysicalMemory GetPhysicalMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? new PhysicalMemory(status.TotalPhysical, status.AvailablePhysical)
            : default;
    }

    private static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> root)
    {
        foreach (var hardware in root)
        {
            yield return hardware;
            foreach (var child in EnumerateHardware(hardware.SubHardware))
                yield return child;
        }
    }

    public void Dispose()
    {
        if (!_opened) return;
        _computer.Close();
        _opened = false;
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private readonly record struct PhysicalMemory(ulong TotalBytes, ulong AvailableBytes);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint CallNtPowerInformation(
        int informationLevel, IntPtr inputBuffer, uint inputBufferLength,
        IntPtr outputBuffer, uint outputBufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }
}
