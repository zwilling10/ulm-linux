namespace ULM.Linux;

internal static class LinuxReleaseNotes
{
    public static bool ShouldShow(string previous, string current) =>
        !string.IsNullOrWhiteSpace(previous) && previous != current;

    public static string GetNotes(bool german) => german
        ? "Linux an Windows 2.45 angeglichen:\n\n• ISO-Suche mit aktuellem DistroWatch-User-Agent und DuckDuckGo-Fallback.\n• Vorschau-Popup mit Screenshot, Fakten, Beschreibung und Tags.\n• Übernommene ISO-Suchtreffer werden direkt zur Download-Liste hinzugefügt.\n• Discovery-Infos wechseln Sprache ohne alten Cache-Text.\n• Ventoy-Theme wird auch nach Updates wiederhergestellt."
        : "Linux aligned with Windows 2.45:\n\n• ISO search with current DistroWatch user agent and DuckDuckGo fallback.\n• Preview popup with screenshot, facts, description, and tags.\n• Added ISO search results go straight to the download list.\n• Discovery info changes language without stale cached text.\n• Ventoy theme is restored after updates too.";
}
