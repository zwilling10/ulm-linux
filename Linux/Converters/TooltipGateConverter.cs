using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ULM.Linux.Converters
{
    /// <summary>Nutzerfund (2026-09-08): "die Checkbox (Mouse over) im Hauptfenster hat keine
    /// Funktion, es wird auch bei Abwahl angezeigt" — LinuxMainViewModel.ShowInfoOnHover existierte
    /// bereits (aus einer früheren Phase, Windows-Pendant der Statusleisten-Checkbox), war aber nie
    /// verdrahtet: LinuxIsoRow.TipTooltip (neu hinzugekommen) kennt die Checkbox nicht. Kombiniert
    /// beide Werte per MultiBinding, da sie an zwei unterschiedlichen DataContexts hängen (Zeile
    /// bzw. Fenster) — reagiert dadurch live auf ein Umschalten der Checkbox, ohne dass die Zeilen
    /// neu aufgebaut werden müssen.</summary>
    public sealed class TooltipGateConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2 || values[1] is not true) return null;
            return values[0] as string;
        }
    }
}
