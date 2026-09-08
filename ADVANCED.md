# Advanced hardware access

NotchSense is designed to run as a normal Windows application and does **not**
bundle or require a kernel driver by default. This is intentional.

## CPU sensors and PawnIO

On some CPUs and Windows configurations, LibreHardwareMonitor cannot expose
CPU temperature and/or detailed real-time CPU clock sensors to a normal,
non-elevated process. For those sensors, LibreHardwareMonitor can use its
low-level hardware-access path through **PawnIO**.

If you want those additional CPU sensors on an affected system, you may need
to:

1. Install PawnIO separately from its official project/release.
2. Run NotchSense **as Administrator** so LibreHardwareMonitor can access the
   required low-level sensors.

PawnIO is **not bundled with NotchSense** and is not installed automatically.
Installing a kernel-level component is an optional advanced-user choice and
is outside the default NotchSense setup.

Without that low-level access, NotchSense may show `Sensor no disponible` /
`Sensor unavailable` for CPU temperature. CPU frequency has Windows-based
fallbacks, but those may not expose the same per-core dynamic clock information
as a low-level hardware reader.

## Design inspiration

The creation of NotchSense was inspired by **Codenotch** by VinzDG, especially
its minimalist edge-mounted notch concept, hover interaction, and use of a
compact shape integrated with the screen edge.

Codenotch is an independent project and is not part of NotchSense.
