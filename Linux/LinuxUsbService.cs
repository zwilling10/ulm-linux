using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ULM.Core.Services;

namespace ULM.Linux
{
    /// <summary>Ein per lsblk erkannter Wechseldatenträger.</summary>
    public sealed record LinuxBlockDevice(
        string DeviceNode,
        string? MountPoint,
        long SizeBytes,
        string Model,
        bool IsVentoyInstalled);

    public sealed class LinuxUsbService
    {
        public delegate Task<string> RunLsblkFunc();

        private readonly RunLsblkFunc _runLsblk;

        public LinuxUsbService(RunLsblkFunc? runLsblk = null)
        {
            _runLsblk = runLsblk ?? DefaultRunLsblk;
        }

        public async Task<List<LinuxBlockDevice>> ListRemovableDevicesAsync()
        {
            string json = await _runLsblk().ConfigureAwait(false);
            return ParseLsblkJson(json);
        }

        /// <summary>
        /// Reine, testbare Parse-Funktion — kein Prozessaufruf. lsblk-JSON-Feldtypen (rm/size)
        /// variieren je nach util-linux-Version zwischen echten JSON-Zahlen/Booleans und Strings —
        /// GetRawText()+Parsing deckt beide Fälle ab, statt sich auf einen festen Typ zu verlassen.
        /// </summary>
        internal static List<LinuxBlockDevice> ParseLsblkJson(string json)
        {
            var result = new List<LinuxBlockDevice>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blockdevices", out var devices)) return result;

            foreach (var dev in devices.EnumerateArray())
            {
                string type = GetString(dev, "type") ?? string.Empty;
                bool removable = GetBool(dev, "rm");
                if (type != "disk" || !removable) continue;

                string name = GetString(dev, "name") ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                string? mountPoint = GetString(dev, "mountpoint");
                if (!IsRealMountPoint(mountPoint)) mountPoint = null;
                if (mountPoint is null && dev.TryGetProperty("children", out var children))
                {
                    long bestSize = -1;
                    foreach (var child in children.EnumerateArray())
                    {
                        string? childMount = GetString(child, "mountpoint");
                        if (!IsRealMountPoint(childMount)) continue;
                        long childSize = GetLong(child, "size");
                        if (childSize > bestSize) { bestSize = childSize; mountPoint = childMount; }
                    }
                }

                result.Add(new LinuxBlockDevice(
                    DeviceNode: "/dev/" + name,
                    MountPoint: mountPoint,
                    SizeBytes: GetLong(dev, "size"),
                    Model: GetString(dev, "model") ?? string.Empty,
                    IsVentoyInstalled: mountPoint is not null && UsbService.IsVentoyInstalled(mountPoint)));
            }
            return result;
        }

        /// <summary>
        /// lsblk meldet Swap-Partitionen als mountpoint="[SWAP]" — kein echter, nutzbarer
        /// Dateisystempfad (in echter WSL-lsblk-Ausgabe beobachtet, Task 1 nur mit
        /// selbstgebautem Fixture-JSON getestet). Ohne diesen Filter würde ein Wechseldatenträger
        /// mit Swap-Partition fälschlich als "hier gemountet" gelten.
        /// </summary>
        private static bool IsRealMountPoint(string? mountPoint) =>
            !string.IsNullOrEmpty(mountPoint) && !(mountPoint.StartsWith('[') && mountPoint.EndsWith(']'));

        private static string? GetString(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

        private static bool GetBool(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return false;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => v.GetString() is "1" or "true",
                JsonValueKind.Number => v.GetInt32() != 0,
                _ => false,
            };
        }

        private static long GetLong(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return 0L;
            if (v.ValueKind == JsonValueKind.Number) return v.GetInt64();
            return long.TryParse(v.GetString(), out long n) ? n : 0L;
        }

        private static async Task<string> DefaultRunLsblk()
        {
            var psi = new ProcessStartInfo("lsblk", "-J -b -o NAME,SIZE,MODEL,RM,TYPE,MOUNTPOINT")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc is null) return "{}";
            string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            proc.WaitForExit(5000);
            return output;
        }
    }
}
