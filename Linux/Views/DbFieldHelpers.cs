using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ULM.Core.Models;
using ULM.Infrastructure;

namespace ULM.Linux.Views
{
    /// <summary>Avalonia-Pendant zu den WPF-`AppRes`-Formularhelfern (Views/Dialogs/
    /// DownloadDialogs.cs:19-59) — nur die tatsächlich gebrauchten Helfer (Label+TextBox-Paar,
    /// Kategorie-ComboBox mit übersetztem Label/internem Schlüssel im Tag). Styling kommt
    /// automatisch aus Linux/Themes/DarkTheme.axaml (TextBox/ComboBox sind dort per Typ-Selector
    /// gestylt), hier nur Label-TextBlocks + Layout.</summary>
    internal static class DbFieldHelpers
    {
        public static TextBox AddField(StackPanel root, string label, string value, bool multiLine = false)
        {
            root.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            var tb = new TextBox
            {
                Text = value,
                Margin = new Thickness(0, 0, 0, 10),
                MinHeight = multiLine ? 70 : 30,
                AcceptsReturn = multiLine,
                TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
            root.Children.Add(tb);
            return tb;
        }

        public static ComboBox AddCategoryCombo(StackPanel root, string selected)
        {
            root.Children.Add(new TextBlock { Text = LocalizationService.T(Str.Db_Field_Category), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            var cb = new ComboBox { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
            FillCategoryCombo(cb, selected);
            root.Children.Add(cb);
            return cb;
        }

        /// <summary>Combo-Einträge zeigen das übersetzte Kategorie-Label (Content), der interne,
        /// stabile Kategorie-Schlüssel bleibt im Tag hinterlegt — SelectedCategory() liest ihn
        /// zurück, damit ein Sprachwechsel den gespeicherten Wert nicht verändert.</summary>
        public static void FillCategoryCombo(ComboBox cb, string selected)
        {
            cb.Items.Clear();
            foreach (string cat in Constants.Categories)
                cb.Items.Add(new ComboBoxItem { Content = Constants.CategoryLabel(cat), Tag = cat });
            string sel = Constants.Categories.Contains(selected) ? selected : "Einsteiger";
            cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == sel);
        }

        public static string SelectedCategory(ComboBox cb) =>
            (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "Einsteiger";
    }
}
