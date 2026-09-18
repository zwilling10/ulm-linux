using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Assistant.Models;

namespace ULM.Linux.Views;

public sealed class AssistantAvatarButton : Button
{
    private AssistantChatWindow? _openWindow;

    public Func<AssistantLanguage> GetLanguage { get; set; } = () => AssistantLanguage.English;

    public AssistantAvatarButton()
    {
        Width = 52;
        Height = 52;
        Padding = new Thickness(0);
        Content = "🐧";
        FontSize = 26;
        Background = new SolidColorBrush(Color.Parse("#2EA8FF"));
        CornerRadius = new CornerRadius(26);
        ToolTip.SetTip(this, "Uli");
        Click += (_, _) => OpenOrActivate();
    }

    private void OpenOrActivate()
    {
        if (_openWindow is { IsVisible: true })
        {
            _openWindow.Activate();
            return;
        }

        var chat = new AssistantChatWindow(GetLanguage());
        if (VisualRoot is Window owner)
        {
            chat.Position = ComputeBottomRightPosition(owner.Position, owner.Bounds.Width, owner.Bounds.Height, chat.Width, chat.Height);
            chat.Show(owner);
        }
        else
        {
            chat.Show();
        }

        _openWindow = chat;
        chat.Closed += (_, _) => _openWindow = null;
    }

    internal static PixelPoint ComputeBottomRightPosition(PixelPoint ownerPosition, double ownerWidth, double ownerHeight, double chatWidth, double chatHeight)
    {
        const int margin = 16;
        int left = ownerPosition.X + (int)Math.Ceiling(ownerWidth) - (int)Math.Ceiling(chatWidth) - margin;
        int top = ownerPosition.Y + (int)Math.Ceiling(ownerHeight) - (int)Math.Ceiling(chatHeight) - margin;
        return new PixelPoint(left, top);
    }
}
