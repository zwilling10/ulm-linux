using System;
using System.IO;

namespace ULM.Linux
{
    /// <summary>
    /// XDG-Basisverzeichnisse für die Linux-GUI (siehe
    /// docs/superpowers/specs/2026-07-28-linux-gui-design.md — bewusster Bruch mit dem
    /// Windows-Portable-Muster aus Infrastructure/AppPaths.cs).
    /// </summary>
    public static class LinuxPaths
    {
        public static string ConfigDir => Path.Combine(ResolveXdg("XDG_CONFIG_HOME", ".config"), "ulm");
        public static string DataDir   => Path.Combine(ResolveXdg("XDG_DATA_HOME", ".local/share"), "ulm");

        public static string SettingsIni => Path.Combine(ConfigDir, "ulm_settings.ini");

        private static string ResolveXdg(string envVar, string relativeToHome)
        {
            string? fromEnv = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, relativeToHome);
        }
    }
}
