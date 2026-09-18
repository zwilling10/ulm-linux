using System;
using System.IO;
using System.Text;
using Avalonia.Platform;

namespace ULM.Linux
{
    /// <summary>Nutzerwunsch: ULM soll sich "wie andere Programme auch" automatisch ins
    /// Anwendungsmenü/den Dateimanager eintragen (eigenes Icon statt des generischen
    /// "ausführbare Datei"-Symbols) — ULM ist eine portable Single-File-Binary ohne Installer,
    /// daher übernimmt die App das selbst beim Start statt über ein Paket-Postinstall-Skript.
    /// Legt einen Freedesktop-".desktop"-Eintrag plus Icon unter den XDG-Standardpfaden an
    /// (~/.local/share/applications, ~/.local/share/icons/hicolor/256x256/apps) — das ist der
    /// einzige Weg, wie Linux-Dateimanager/App-Menüs einer Binärdatei ein eigenes Icon zuordnen;
    /// eine rohe .exe-artige "eingebettetes Icon"-Anzeige wie unter Windows existiert dort nicht.</summary>
    public static class LinuxDesktopIntegrationService
    {
        private const string DesktopFileName = "ulm-linux.desktop";
        private const string IconName = "ulm-linux";

        public static void EnsureInstalled()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                string dataHome = ResolveXdgDataHome();
                string iconPath = Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", IconName + ".png");
                string desktopPath = Path.Combine(dataHome, "applications", DesktopFileName);

                WriteIfChanged(iconPath, ReadEmbeddedIcon());
                WriteIfChanged(desktopPath, Encoding.UTF8.GetBytes(BuildDesktopEntry(exePath)));

#pragma warning disable CA1416 // UnixFileMode ist plattformspezifisch — dieses Projekt ist per
                               // RuntimeIdentifier fest auf linux-x64 gepinnt, läuft nie unter Windows.
                File.SetUnixFileMode(desktopPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
            }
            catch
            {
                // Desktop-Integration ist rein kosmetisch — nie ein Grund, den Start sichtbar
                // scheitern zu lassen (z.B. schreibgeschütztes Home-Verzeichnis, fehlende Rechte).
            }
        }

        private static byte[] ReadEmbeddedIcon()
        {
            using var stream = AssetLoader.Open(new Uri("avares://ulm-linux/Assets/app-icon.png"));
            using var mem = new MemoryStream();
            stream.CopyTo(mem);
            return mem.ToArray();
        }

        private static string BuildDesktopEntry(string exePath) =>
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Universal Linux Manager\n" +
            "Comment=Ventoy-Multiboot-USB-Sticks einrichten und aktuell halten\n" +
            $"Exec=\"{exePath}\"\n" +
            $"Icon={IconName}\n" +
            "Terminal=false\n" +
            "Categories=Utility;System;\n";

        private static void WriteIfChanged(string path, byte[] desired)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(desired)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, desired);
        }

        private static string ResolveXdgDataHome()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share");
        }
    }
}
