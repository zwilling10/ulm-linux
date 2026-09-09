using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu Views/Dialogs/DatabaseDialogs.cs' IsoListDialog (WPF, Zeilen
    /// 1-175) — reines Code-behind wie das Original, arbeitet direkt gegen den übergebenen
    /// IsoDatabaseService (unter Windows wie hier: IsoDatabaseService.Instance, nicht über die
    /// ViewModel-Injektionsschicht — MoveUp/MoveDown existieren nur auf der konkreten Klasse, nicht
    /// im IIsoDatabaseService-Interface).</summary>
    public sealed class IsoListDialog : Window
    {
        private readonly IsoDatabaseService _db;
        private readonly ListBox _list;

        /// <summary>Mindestens ein neuer Eintrag wurde über "Neu" angelegt — der Aufrufer kann das
        /// nutzen, um gezielt nur dann einen Gesundheitscheck für den neuen, unverifizierten
        /// Eintrag auszulösen (siehe Windows-Pendant BtnEditDb_Click).</summary>
        public bool AnyEntryAdded { get; private set; }

        public IsoListDialog(IsoDatabaseService db)
        {
            _db = db;
            Title = LocalizationService.T(Str.Db_ListDialog_Title);
            Width = 780;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("*,Auto") };

            _list = new ListBox { BorderBrush = new SolidColorBrush(Color.Parse("#336B9E")), BorderThickness = new Thickness(1) };
            FillList();
            Grid.SetRow(_list, 0);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            AddBtn(btns, LocalizationService.T(Str.Db_Btn_New), (_, _) => _ = OnAddAsync(), "success");
            AddBtn(btns, LocalizationService.T(Str.Db_Btn_Edit), (_, _) => _ = OnEditAsync(), "primary");
            AddBtn(btns, LocalizationService.T(Str.Db_Btn_Delete), (_, _) => _ = OnDeleteAsync(), "danger");
            AddBtn(btns, LocalizationService.T(Str.Db_Btn_MoveUp), (_, _) => OnUp(), "ghost");
            AddBtn(btns, LocalizationService.T(Str.Db_Btn_MoveDown), (_, _) => OnDown(), "ghost");

            var close = new Button { Content = LocalizationService.T(Str.Db_Btn_Close), Classes = { "primary" }, Margin = new Thickness(20, 0, 0, 0), MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => { _db.Save(); Close(true); };
            btns.Children.Add(close);
            Grid.SetRow(btns, 1);

            root.Children.Add(_list);
            root.Children.Add(btns);
            Content = root;
        }

        /// <summary>Gruppiert nach Kategorie (feste Constants.Categories-Reihenfolge, Kategorie-
        /// Überschriften nicht selektierbar) — Reihenfolge INNERHALB einer Kategorie bleibt exakt
        /// die der Rohdaten (_db.Entries), damit Hoch/Runter sichtbar wirkt.</summary>
        private void FillList()
        {
            _list.Items.Clear();
            var byCat = new Dictionary<string, List<(int Index, IsoEntry Entry)>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _db.Count; i++)
            {
                IsoEntry e = _db.Entries[i];
                string cat = Constants.Categories.Contains(e.Category, StringComparer.OrdinalIgnoreCase) ? e.Category : "Einsteiger";
                if (!byCat.TryGetValue(cat, out var members)) byCat[cat] = members = new List<(int, IsoEntry)>();
                members.Add((i, e));
            }
            foreach (string cat in Constants.Categories)
            {
                if (!byCat.TryGetValue(cat, out var members) || members.Count == 0) continue;
                _list.Items.Add(MakeCategoryHeader($"{Constants.CategoryLabel(cat)}   ·   {members.Count}"));
                foreach ((int idx, IsoEntry e) in members)
                {
                    var item = new ListBoxItem { Content = "     " + e.Name, Tag = idx };
                    ToolTip.SetTip(item, string.IsNullOrWhiteSpace(e.Filename) ? LocalizationService.T(Str.Db_NoFilenameTooltip) : e.Filename);
                    _list.Items.Add(item);
                }
            }
        }

        private static ListBoxItem MakeCategoryHeader(string text) => new()
        {
            Content = text,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#3AAEEF")),
            Background = new SolidColorBrush(Color.Parse("#132A44")),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 6, 0, 2),
            IsHitTestVisible = false,
            Focusable = false,
        };

        private static void AddBtn(Panel p, string label, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler, string cssClass)
        {
            var btn = new Button { Content = label, Classes = { cssClass }, Margin = new Thickness(0, 0, 6, 0), MinWidth = 110 };
            btn.Click += handler;
            p.Children.Add(btn);
        }

        private int SelectedIndex() => _list.SelectedItem is ListBoxItem li && li.Tag is int i ? i : -1;

        /// <summary>Wählt per Roh-Index in _db.Entries (nicht per ListBox-Position — durch die
        /// Kategorie-Header stimmen beide seit der Gruppierung nicht mehr überein).</summary>
        private void SelectByEntryIndex(int entryIndex)
        {
            foreach (object? obj in _list.Items)
                if (obj is ListBoxItem li && li.Tag is int t && t == entryIndex) { _list.SelectedItem = li; return; }
        }

        private async System.Threading.Tasks.Task OnAddAsync()
        {
            var entry = new IsoEntry { Category = "Einsteiger" };
            var dlg = new IsoEditDialog(entry, isNew: true);
            bool saved = await dlg.ShowDialog<bool>(this);
            if (!saved) return;
            _db.Add(entry);
            AnyEntryAdded = true;
            FillList();
            SelectByEntryIndex(_db.Count - 1);
        }

        private async System.Threading.Tasks.Task OnEditAsync()
        {
            int idx = SelectedIndex();
            if (idx < 0) return;
            var dlg = new IsoEditDialog(_db.Entries[idx], isNew: false);
            bool saved = await dlg.ShowDialog<bool>(this);
            if (saved) { FillList(); SelectByEntryIndex(idx); }
        }

        private async System.Threading.Tasks.Task OnDeleteAsync()
        {
            int idx = SelectedIndex();
            if (idx < 0) return;
            bool confirmed = await ConfirmDialog.ShowAsync(this,
                LocalizationService.T(Str.Db_DeleteEntryConfirm_Title),
                string.Format(LocalizationService.T(Str.Db_DeleteEntryConfirm_Body), _db.Entries[idx].Name));
            if (!confirmed) return;
            _db.Remove(idx);
            FillList();
        }

        private void OnUp()
        {
            int idx = SelectedIndex();
            if (idx <= 0) return;
            _db.MoveUp(idx);
            FillList();
            SelectByEntryIndex(idx - 1);
        }

        private void OnDown()
        {
            int idx = SelectedIndex();
            if (idx < 0 || idx >= _db.Count - 1) return;
            _db.MoveDown(idx);
            FillList();
            SelectByEntryIndex(idx + 1);
        }
    }
}
