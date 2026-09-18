using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Assistant.Models;
using ULM.Assistant.Services;

namespace ULM.Linux.Views;

public sealed class AssistantChatWindow : Window
{
    private static readonly IBrush BrushBg = new SolidColorBrush(Color.Parse("#0B1220"));
    private static readonly IBrush BrushCard = new SolidColorBrush(Color.Parse("#112036"));
    private static readonly IBrush BrushBorder = new SolidColorBrush(Color.Parse("#284767"));
    private static readonly IBrush BrushDim = new SolidColorBrush(Color.Parse("#8BA3BE"));
    private static readonly IBrush BrushHeader = new SolidColorBrush(Color.Parse("#EAF1F8"));
    private static readonly IBrush BrushBlue = new SolidColorBrush(Color.Parse("#2EA8FF"));

    private readonly AssistantLanguage _language;
    private readonly IReadOnlyList<FaqEntry> _catalog;
    private readonly StackPanel _messagesPanel = new();
    private readonly WrapPanel _suggestionsPanel = new();
    private readonly ScrollViewer _messagesScroll;
    private readonly TextBox _inputBox = new();
    private readonly TextBlock _placeholder = new();

    public AssistantChatWindow(AssistantLanguage language)
    {
        _language = language;
        _catalog = FaqCatalogService.Instance.Catalog;

        Title = AssistantStrings.T(AssistantStr.WindowTitle, _language);
        Width = 640;
        Height = 480;
        MinWidth = 480;
        MinHeight = 380;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = BrushBg;

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };

        var header = new Border { Background = new SolidColorBrush(Color.Parse("#172437")), Padding = new Thickness(18, 14) };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = BrushBlue,
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock { Text = "🐧", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        headerStack.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Uli", FontSize = 14.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                new TextBlock { Text = AssistantStrings.T(AssistantStr.WindowTitle, _language), FontSize = 11, Foreground = BrushDim, Margin = new Thickness(0, 2, 0, 0) },
            },
        });
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _messagesScroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _messagesPanel.Margin = new Thickness(16, 12);
        _messagesScroll.Content = _messagesPanel;
        Grid.SetRow(_messagesScroll, 1);
        root.Children.Add(_messagesScroll);

        _suggestionsPanel.Margin = new Thickness(16, 0, 16, 8);
        Grid.SetRow(_suggestionsPanel, 2);
        root.Children.Add(_suggestionsPanel);

        root.Children.Add(BuildInputRow());
        Content = root;

        AddUliMessage(AssistantStrings.T(AssistantStr.Greeting, _language));
        ShowMainTopics();
    }

    private Grid BuildInputRow()
    {
        var inputRow = new Grid
        {
            Margin = new Thickness(16, 0, 16, 14),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var inputBorder = new Border
        {
            Background = BrushCard,
            BorderBrush = BrushBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 8, 0),
        };
        var inputGrid = new Grid();
        _placeholder.Text = AssistantStrings.T(AssistantStr.InputPlaceholder, _language);
        _placeholder.Margin = new Thickness(14, 0);
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.FontSize = 12.5;
        _placeholder.Foreground = BrushDim;
        _placeholder.IsHitTestVisible = false;

        _inputBox.Background = Brushes.Transparent;
        _inputBox.BorderThickness = new Thickness(0);
        _inputBox.Padding = new Thickness(14, 9);
        _inputBox.FontSize = 12.5;
        _inputBox.Foreground = BrushHeader;
        _inputBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SubmitInput(); };
        _inputBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) _placeholder.IsVisible = string.IsNullOrEmpty(_inputBox.Text);
        };
        inputGrid.Children.Add(_placeholder);
        inputGrid.Children.Add(_inputBox);
        inputBorder.Child = inputGrid;
        Grid.SetColumn(inputBorder, 0);
        inputRow.Children.Add(inputBorder);

        var send = new Button
        {
            Content = "→",
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Background = BrushBlue,
            CornerRadius = new CornerRadius(18),
        };
        send.Click += (_, _) => SubmitInput();
        Grid.SetColumn(send, 1);
        inputRow.Children.Add(send);
        Grid.SetRow(inputRow, 3);
        return inputRow;
    }

    private void ShowMainTopics()
    {
        _suggestionsPanel.Children.Clear();
        foreach (var entry in _catalog) _suggestionsPanel.Children.Add(BuildSuggestionButton(entry));
    }

    private void ShowRelated(FaqEntry current)
    {
        _suggestionsPanel.Children.Clear();
        foreach (string relatedId in current.RelatedIds)
        {
            var related = _catalog.FirstOrDefault(e => e.Id == relatedId);
            if (related is not null) _suggestionsPanel.Children.Add(BuildSuggestionButton(related));
        }

        var back = BuildChip(AssistantStrings.T(AssistantStr.BackToOverview, _language));
        back.Click += (_, _) => ShowMainTopics();
        _suggestionsPanel.Children.Add(back);
    }

    private Button BuildSuggestionButton(FaqEntry entry)
    {
        string chip = _language == AssistantLanguage.German ? entry.ChipLabelDe : entry.ChipLabelEn;
        if (string.IsNullOrWhiteSpace(chip))
            chip = _language == AssistantLanguage.German ? entry.QuestionLabelDe : entry.QuestionLabelEn;

        var button = BuildChip(chip);
        button.Click += (_, _) => AnswerTopic(entry);
        return button;
    }

    private Button BuildChip(string text) => new()
    {
        Content = text,
        Padding = new Thickness(12, 6),
        Margin = new Thickness(0, 0, 6, 6),
        FontSize = 12,
        Foreground = BrushBlue,
        Background = Brushes.Transparent,
        BorderBrush = BrushBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
    };

    private void AnswerTopic(FaqEntry entry)
    {
        string label = _language == AssistantLanguage.German ? entry.QuestionLabelDe : entry.QuestionLabelEn;
        string answer = _language == AssistantLanguage.German ? entry.AnswerDe : entry.AnswerEn;
        AddUserMessage(label);
        AddUliMessage(answer);
        ShowRelated(entry);
    }

    private void SubmitInput()
    {
        string text = (_inputBox.Text ?? string.Empty).Trim();
        if (text.Length == 0) return;
        _inputBox.Text = string.Empty;
        AddUserMessage(text);

        var result = FaqMatchingEngine.Match(_catalog, _language, text);
        var match = result.Id is null ? null : _catalog.FirstOrDefault(e => e.Id == result.Id);
        if (match is null)
        {
            AddUliMessage(AssistantStrings.T(AssistantStr.Fallback, _language));
            ShowMainTopics();
            return;
        }

        string answer = _language == AssistantLanguage.German ? match.AnswerDe : match.AnswerEn;
        string? hint = result.IsBestGuess ? AssistantStrings.T(AssistantStr.BestGuessPrefix, _language) : null;
        AddUliMessage(answer, hint);
        ShowRelated(match);
    }

    private void AddUserMessage(string text) => AddMessage(ChatSender.User, text, null);

    private void AddUliMessage(string text, string? hint = null) => AddMessage(ChatSender.Uli, text, hint);

    private void AddMessage(ChatSender sender, string text, string? hint)
    {
        bool fromUser = sender == ChatSender.User;
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(0, 4),
        };

        if (!fromUser)
        {
            line.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = BrushBlue,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = "🐧", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        }

        var bubbleContent = new StackPanel();
        if (!string.IsNullOrEmpty(hint))
            bubbleContent.Children.Add(new TextBlock { Text = hint, FontSize = 10.5, FontStyle = FontStyle.Italic, Foreground = BrushDim, Margin = new Thickness(0, 0, 0, 4) });
        bubbleContent.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = BrushHeader });

        line.Children.Add(new Border
        {
            Padding = new Thickness(12, 8),
            CornerRadius = fromUser ? new CornerRadius(12, 3, 12, 12) : new CornerRadius(3, 12, 12, 12),
            Background = fromUser ? BrushBlue : BrushCard,
            MaxWidth = 320,
            Child = bubbleContent,
        });

        _messagesPanel.Children.Add(line);
        _messagesScroll.ScrollToEnd();
    }
}
