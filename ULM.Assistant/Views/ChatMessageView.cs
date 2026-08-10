// ULM.Assistant/Views/ChatMessageView.cs
using System.Windows;
using System.Windows.Media;
using ULM.Assistant.Models;

namespace ULM.Assistant.Views
{
    // Bindbare Hülle um ChatMessage für die WPF-Anzeige (Blasenfarbe + Ausrichtung je nach
    // Sender) — bewusst getrennt von Models.ChatMessage, damit das reine Datenmodell frei von
    // WPF-Typen (Brush, HorizontalAlignment) bleibt. Liest Farben zur Laufzeit per String-
    // Schlüssel aus Application.Current.Resources — funktioniert automatisch im aktuell
    // aktiven Hell/Dunkel-Theme der Haupt-App, ganz ohne Projekt-Referenz dorthin (dieselbe
    // Technik wie AppRes.Brush(...) in Views/Dialogs/DownloadDialogs.cs der Haupt-App).
    public sealed class ChatMessageView
    {
        public string Text { get; }
        public Brush BubbleBrush { get; }
        public HorizontalAlignment BubbleAlignment { get; }

        public ChatMessageView(ChatMessage message)
        {
            Text = message.Text;
            bool fromUser = message.Sender == ChatSender.User;
            BubbleAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            BubbleBrush = Application.Current?.Resources[fromUser ? "BrushBlue" : "BrushCard"] as Brush
                ?? Brushes.LightGray;
        }
    }
}
