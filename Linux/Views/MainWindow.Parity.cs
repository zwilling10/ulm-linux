using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ULM.Core.Models;
using ULM.Core.Services;
using ULM.Infrastructure;

namespace ULM.Linux.Views;

public partial class MainWindow
{
    private readonly HashSet<string> _failedDownloads = new(StringComparer.OrdinalIgnoreCase);
    private bool _sourceRepairOpen;
    private readonly System.Threading.SemaphoreSlim _maintenanceDialogs = new(1, 1);

    private static string ParityText(string de, string en) =>
        LocalizationService.Current == AppLanguage.German ? de : en;

    private async Task ShowReleaseNotesAsync(bool automatic)
    {
        string previous = IniService.Read(LinuxPaths.SettingsIni, "App", "LastSeenVersion", "");
        if (!automatic || LinuxReleaseNotes.ShouldShow(previous, Constants.AppVersion))
            await InfoDialog.ShowAsync(this, ParityText("Was ist neu?", "What's new?") + " — " + Constants.AppVersion,
                LinuxReleaseNotes.GetNotes(LocalizationService.Current == AppLanguage.German));
        IniService.Write(LinuxPaths.SettingsIni, "App", "LastSeenVersion", Constants.AppVersion);
    }

    private async void BtnChangelog_Click(object? sender, RoutedEventArgs e) => await ShowReleaseNotesAsync(false);

    private async void BtnManualSource_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.IsBusy) return;
        var selected = _vm.GetSelectedEntries();
        if (selected.Count != 1)
        {
            await InfoDialog.ShowAsync(this, Constants.AppTitle,
                ParityText("Bitte genau eine ISO markieren, deren Quelle repariert werden soll.",
                    "Select exactly one ISO whose source you want to repair."));
            return;
        }
        await RepairSourceAsync(selected[0].Name);
    }

    private async void OnManualSearchRequested(string name) => await RepairSourceAsync(name);

    private async Task RepairSourceAsync(string name)
    {
        if (_vm is null || _sourceRepairOpen) return;
        if (_vm.IsBusy)
        {
            await InfoDialog.ShowAsync(this, Constants.AppTitle,
                ParityText("Bitte zuerst den laufenden Vorgang beenden oder abbrechen.",
                    "Finish or cancel the current operation first."));
            return;
        }
        var entry = IsoDatabaseService.Instance.Entries.FirstOrDefault(e => e.Name == name);
        if (entry is null) return;
        _sourceRepairOpen = true;
        try
        {
            if (!await new ManualSourceSearchDialog(entry).ShowDialog<bool>(this)) return;
            IsoDatabaseService.Instance.Save();
            _vm.RefreshRows();
            if (string.IsNullOrWhiteSpace(entry.Url)) return;
            if (!await ConfirmDialog.ShowAsync(this, Constants.AppTitle,
                    ParityText("Quelle gespeichert. Diese ISO jetzt erneut herunterladen?",
                        "Source saved. Download this ISO again now?"))) return;
            if (_vm.IsBusy) return;
            OpenProgressDialog(new[] { entry.Name }, true, false);
            await _vm.DownloadQueueAsync(new List<IsoEntry> { entry }, null, false, false, 1);
        }
        catch (Exception ex) { await InfoDialog.ShowAsync(this, Constants.AppTitle, ex.Message); }
        finally { _sourceRepairOpen = false; }
    }

    private void OnDownloadItemFailed(string name)
    {
        _failedDownloads.Add(name);
        _downloadProgressDialog?.SetManualSearchCandidates(_failedDownloads);
    }

    private async void OnMissingOnStickDetected(List<IsoEntry> entries, string mountPoint)
    {
        if (_vm is null || entries.Count == 0) return;
        await _maintenanceDialogs.WaitAsync();
        try
        {
            if (_vm.IsBusy || _vm.SelectedDrive?.MountPoint != mountPoint) return;
            var fresh = entries.Where(e => _vm.MarkCopyOffered(mountPoint, e.Filename)).ToList();
            if (fresh.Count == 0) return;
            string body = string.Format(LocalizationService.T(Str.Msg_LocalNotOnStick), entries.Count, mountPoint)
                + "\n\n" + string.Join("\n", fresh.Select(e => "• " + e.Name))
                + "\n\n" + LocalizationService.T(Str.Msg_CopyNow);
            if (!await ConfirmDialog.ShowAsync(this, LocalizationService.T(Str.Msg_LocalNotOnStick_Title), body)) return;
            if (_vm.IsBusy || _vm.SelectedDrive?.MountPoint != mountPoint) return;
            if (!await ConfirmEnoughFreeSpaceAsync(fresh, mountPoint)) return;
            bool deleteAfter = await ConfirmDialog.ShowAsync(this,
                LocalizationService.T(Str.Msg_DeleteFiles_Title),
                LocalizationService.T(Str.Msg_DeleteLocalAfterCopy_Immediate));
            if (_vm.IsBusy || _vm.SelectedDrive?.MountPoint != mountPoint) return;
            OpenProgressDialog(fresh.Select(e => e.Name), false, true);
            await _vm.CopyEntriesToStickAsync(fresh, mountPoint, deleteAfter);
        }
        catch (Exception ex) { await InfoDialog.ShowAsync(this, Constants.AppTitle, ex.Message); }
        finally { _maintenanceDialogs.Release(); }
    }

    private async void OnIncompleteIsosOnStickDetected(List<UsbService.StickIso> files, string mountPoint)
    {
        if (_vm is null || files.Count == 0) return;
        await _maintenanceDialogs.WaitAsync();
        try
        {
            if (_vm.IsBusy || _vm.SelectedDrive?.MountPoint != mountPoint) return;
            var fresh = files.Where(f => _vm.MarkIncompleteStickIsoOffered(mountPoint, f.Filename)).ToList();
            if (fresh.Count == 0) return;
            var dialog = new OrphanedDownloadsDialog(fresh.Select(f => (f.FullPath, f.Size)).ToList(),
                LocalizationService.T(Str.Msg_OrphanedIncomplete_Title),
                LocalizationService.T(Str.Msg_OrphanedIncomplete_Description));
            if (!await dialog.ShowDialog<bool>(this)) return;
            if (_vm.IsBusy || _vm.SelectedDrive?.MountPoint != mountPoint) return;
            foreach (string path in dialog.ToDelete)
                IsoEntry.TryDelete(path, line => _vm.LogEntries.Add(line));
            await _vm.PollDrivesAsync();
        }
        catch (Exception ex) { await InfoDialog.ShowAsync(this, Constants.AppTitle, ex.Message); }
        finally { _maintenanceDialogs.Release(); }
    }
}
