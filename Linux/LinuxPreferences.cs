using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using ULM.Infrastructure;

namespace ULM.Linux;

public sealed class LinuxPreferences
{
    public string Theme { get; set; } = "System";
    public bool ExpertMode { get; set; } = true;
    public static string NormalizeTheme(string? value) => value?.ToLowerInvariant() switch
    { "light" => "Light", "dark" => "Dark", _ => "System" };
    public static LinuxPreferences Load(string? path = null) => new()
    {
        Theme = NormalizeTheme(IniService.Read(path ?? LinuxPaths.SettingsIni, "App", "Theme", "System")),
        ExpertMode = IniService.Read(path ?? LinuxPaths.SettingsIni, "App", "ExpertMode", "true") != "false"
    };
    public void Save(string? path = null)
    {
        path ??= LinuxPaths.SettingsIni;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        IniService.Write(path, "App", "Theme", NormalizeTheme(Theme));
        IniService.Write(path, "App", "ExpertMode", ExpertMode ? "true" : "false");
    }
    public static void ApplyTheme(Application app, string theme) => app.RequestedThemeVariant = NormalizeTheme(theme) switch
    { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
}

public static class LinuxAutostart
{
    public static string EntryPath => Path.Combine(GetConfigHome(), "autostart", "ulm-linux.desktop");
    private static string GetConfigHome()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) ? xdg :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }
    public static bool IsEnabled => File.Exists(EntryPath) &&
        IniService.Read(EntryPath, "Desktop Entry", "Hidden", "false") != "true" &&
        IniService.Read(EntryPath, "Desktop Entry", "X-GNOME-Autostart-enabled", "true") != "false";

    public static string BuildDesktopEntry(string executable, string[] arguments)
    {
        static string Quote(string value)
        {
            if (value.Any(char.IsControl)) throw new ArgumentException("Invalid control character in startup command.");
            // Exec quoting followed by desktop-entry string escaping; no shell involved.
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("$", "\\$").Replace("`", "\\`").Replace("%", "%%");
            return "\"" + escaped.Replace("\\", "\\\\") + "\"";
        }
        if (!Path.IsPathRooted(executable)) throw new ArgumentException("Startup executable must be an absolute path.");
        return "[Desktop Entry]\nType=Application\nName=ULM\nExec=" + Quote(executable) +
            string.Concat(arguments.Select(arg => " " + Quote(arg))) +
            "\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
    }
    public static void SetEnabled(bool enabled, string? entryPath = null, string? executable = null, string[]? arguments = null)
    {
        string path = entryPath ?? EntryPath;
        if (!enabled) { if (File.Exists(path)) File.Delete(path); return; }
        executable ??= Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine application executable.");
        arguments ??= Path.GetFileNameWithoutExtension(executable) == "dotnet"
            ? new[] { Path.Combine(AppContext.BaseDirectory, "ulm-linux.dll") } : Array.Empty<string>();
        string entry = BuildDesktopEntry(executable, arguments);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try { File.WriteAllText(temporary, entry); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
