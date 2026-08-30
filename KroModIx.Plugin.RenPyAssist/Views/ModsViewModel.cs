using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;
using KroModIx.Plugin.RenPyAssist.Services.Modding;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Mods-Tab v0.6.0: „Knopf-für-Dumme"-Pipeline. User wählt Mod-Typ,
/// klickt Bauen, Plugin dekompiliert alle <c>.rpyc</c> im aktiven Sub-Ordner,
/// analysiert die <c>.rpy</c>, generiert den Mod (Walkthrough/Cheat/Rename/
/// Translate) und deployt. Uninstall liest das <c>KROSTEMOD_MANIFEST.json</c>
/// und stellt Backups wieder her.</summary>
public sealed partial class ModsViewModel : ObservableObject
{
    private readonly string _containerPath;
    private readonly string? _activeSubPath;
    private readonly OneClickModBuilder _builder;
    private readonly RpycBatchService _rpycBatch;
    private readonly IHostServices _host;

    [ObservableProperty] private ModTypeOption? _selectedType;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = Strings.T("status.mods_default");
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal;
    [ObservableProperty] private string _progressFile = "";
    [ObservableProperty] private string _manifestInfo = "";
    [ObservableProperty] private bool _hasManifest;
    [ObservableProperty] private string _targetLocale = "de";

    public ObservableCollection<ModTypeOption> Types { get; } = new()
    {
        new(ModTypeId.Walkthrough, Strings.T("mod.walkthrough.name"), "🚩",
            Strings.T("mod.walkthrough.desc")),
        new(ModTypeId.Cheat, Strings.T("mod.cheat.name"), "💰",
            Strings.T("mod.cheat.desc")),
        new(ModTypeId.Rename, Strings.T("mod.rename.name"), "✏",
            Strings.T("mod.rename.desc")),
        new(ModTypeId.Translate, Strings.T("mod.translate.name"), "🌐",
            Strings.T("mod.translate.desc")),
    };

    public ModsViewModel(string containerPath, string? activeSubPath,
        OneClickModBuilder builder, RpycBatchService rpycBatch, IHostServices host)
    {
        _containerPath = containerPath;
        _activeSubPath = activeSubPath;
        _builder = builder;
        _rpycBatch = rpycBatch;
        _host = host;
        SelectedType = Types.First();
        RefreshManifestInfo();
    }

    private string GameDir => string.IsNullOrEmpty(_activeSubPath)
        ? Path.Combine(_containerPath, "game")
        : Path.Combine(_containerPath, _activeSubPath!, "game");

    private void RefreshManifestInfo()
    {
        var manifestPath = Path.Combine(GameDir, OneClickModBuilder.ManifestFileName);
        HasManifest = File.Exists(manifestPath);
        ManifestInfo = HasManifest
            ? string.Format(Strings.T("status.krostemod_installed"), manifestPath)
            : Strings.T("status.krostemod_none");
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (SelectedType is null) return;
        if (!Directory.Exists(GameDir))
        {
            await _host.Dialogs.ShowMessageAsync(Strings.T("dialog.mods_game_dir_title"),
                string.Format(Strings.T("dialog.mods_game_dir_msg"), GameDir));
            return;
        }

        // KI ist optional (Rename nutzt KI für Body-Rewrite falls verfügbar,
        // Walkthrough/Cheat kommen ohne KI aus).

        var ok = await _host.Dialogs.ConfirmAsync(
            string.Format(Strings.T("dialog.build_confirm_title"), SelectedType.DisplayName),
            string.Format(Strings.T("dialog.build_confirm_msg"), Path.GetFileName(_containerPath), GameDir));
        if (!ok) return;

        try
        {
            IsBusy = true;
            StatusText = string.Format(Strings.T("status.building"), SelectedType.DisplayName);
            ProgressCurrent = 0; ProgressTotal = 0; ProgressFile = "";

            var progress = new Progress<OneClickProgress>(p => Dispatcher.UIThread.Post(() =>
            {
                ProgressCurrent = p.Done;
                ProgressTotal = p.Total;
                ProgressFile = p.CurrentFile;
                StatusText = $"{p.Phase}: {p.Done}/{p.Total} {p.CurrentFile}";
            }));

            var result = await Task.Run(() => _builder.Build(GameDir, SelectedType.Id, progress,
                CancellationToken.None,
                renameConfigProvider: AskRenameConfig,
                translationConfigProvider: AskTranslationConfig));

            StatusText = string.Format(Strings.T("status.mod_deployed"), result.DeployedFileCount, GameDir);
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.mod_installed"),
                    SelectedType.DisplayName, Path.GetFileName(_containerPath), result.DeployedFileCount),
                NotificationLevel.Success);
            RefreshManifestInfo();
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.T("status.error_prefix"), ex.Message);
            _host.Logger.Warn(ex, "Mod-Build fehlgeschlagen");
        }
        finally { IsBusy = false; }
    }

    private RenameConfig? AskRenameConfig(IReadOnlyList<RpyCharacter> characters,
        IReadOnlyList<RpySayStatement> sayStatements)
    {
        // Läuft auf Worker-Thread (via Task.Run vom Build). Modal-Dialog
        // braucht UI-Thread → InvokeAsync-Hop.
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new RenameConfigDialog(characters);
            var owner = MainWindow();
            if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
            return dialog.Result;
        }).GetAwaiter().GetResult();
    }

    private TranslationConfig? AskTranslationConfig(ModAnalysis analysis)
    {
        // 1. Sprach-Wahl vom User via Setup-Dialog
        var sayTexts = analysis.SayStatements
            .Select(s => s.RawTextInFile)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        var uniqueTexts = sayTexts.Distinct(StringComparer.Ordinal).ToList();

        var setupResult = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new TranslateSetupDialog(sayTexts.Count, uniqueTexts.Count);
            var owner = MainWindow();
            if (owner is not null) await dialog.ShowDialog(owner); else dialog.Show();
            return dialog.SelectedLanguage;
        }).GetAwaiter().GetResult();

        if (setupResult is not TargetLanguage targetLang) return null;

        // 2. KI-Batch-Übersetzung (blockiert, aber wir sind bereits im Worker-
        // Thread des Build-Prozesses; StatusText-Updates gehen per Dispatcher)
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = string.Format(Strings.T("translate.status"), targetLang.ToNativeName());
                ProgressCurrent = 0;
                ProgressTotal = uniqueTexts.Count;
            });

            var translator = new KrosteAiTranslator(new HostAiProviderAdapter(_host));
            var progress = new Progress<AiTranslateProgress>(p => Dispatcher.UIThread.Post(() =>
            {
                ProgressCurrent = p.Done;
                ProgressTotal = p.Total;
                ProgressFile = string.Format(Strings.T("translate.progress_label"), p.CurrentLanguage.ToNativeName());
            }));
            var translated = translator.TranslateAsync(uniqueTexts, targetLang,
                sourceLanguage: TargetLanguage.English,
                progress: progress).GetAwaiter().GetResult();

            var byLang = new Dictionary<TargetLanguage, IReadOnlyDictionary<string, string>>
            {
                [targetLang] = translated,
            };
            return new TranslationConfig(
                TargetLanguages: new[] { targetLang },
                SourceLanguage: TargetLanguage.English,
                TranslatedStrings: byLang);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "KI-Batch-Übersetzung fehlgeschlagen");
            Dispatcher.UIThread.Post(() =>
                StatusText = string.Format(Strings.T("translate.fail"), ex.Message));
            return null;
        }
    }

    /// <summary>Wirft, wenn ein blockierender UI-Hop versehentlich vom
    /// UI-Thread aufgerufen wird — das waere ein Deadlock ohne Fehlermeldung.</summary>
    private static void EnsureWorkerThread(string caller)
    {
        if (!Dispatcher.UIThread.CheckAccess()) return;
        throw new InvalidOperationException(
            $"{caller} blockiert auf dem UI-Thread — das ist ein Deadlock. " +
            "Der Aufruf gehoert in den Task.Run des Build-Ablaufs.");
    }

    private static Avalonia.Controls.Window? MainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk
            ? desk.MainWindow : null;
    }

    /// <summary>Dekompiliert alle <c>.rpyc</c> im aktiven <c>game/</c>-Ordner
    /// (Standalone, ohne KrosteMod-Build). Portiert aus RenPack — nutzt
    /// <see cref="RpycBatchService"/> mit Progress + skipUpToDate (kein
    /// unnoetiges Re-Decompile beim zweiten Klick).</summary>
    [RelayCommand]
    private async Task DecompileRpycsAsync()
    {
        if (!Directory.Exists(GameDir))
        {
            StatusText = string.Format(Strings.T("status.game_dir_missing"), GameDir);
            return;
        }
        try
        {
            IsBusy = true;
            StatusText = Strings.T("status.searching_rpyc");
            ProgressCurrent = 0; ProgressTotal = 0; ProgressFile = "";
            var progress = new Progress<(int done, int total, string current)>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressCurrent = p.done; ProgressTotal = p.total;
                    ProgressFile = Path.GetFileName(p.current);
                    StatusText = string.Format(Strings.T("status.decompile_progress"), p.done, p.total);
                }));
            var result = await Task.Run(() =>
                _rpycBatch.DecompileDirectory(GameDir, progress, skipUpToDate: true));
            StatusText = result.Failed == 0
                ? string.Format(Strings.T("status.decompile_ok"), result.Succeeded, result.Skipped)
                : string.Format(Strings.T("status.decompile_partial"), result.Succeeded, result.Failed, result.Skipped);
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.decompile_summary"), result.Succeeded, result.Total),
                result.Failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "RenPy-Decompile fehlgeschlagen: {Dir}", GameDir);
            StatusText = string.Format(Strings.T("status.error_prefix"), ex.Message);
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.decompile_error"), ex.Message),
                NotificationLevel.Error);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (!HasManifest)
        {
            StatusText = Strings.T("status.no_krostemod");
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync(Strings.T("dialog.krostemod_uninstall_title"),
            Strings.T("dialog.krostemod_uninstall_msg"));
        if (!ok) return;
        try
        {
            IsBusy = true;
            StatusText = Strings.T("status.uninstalling");
            var result = await Task.Run(() => _builder.Uninstall(GameDir));
            StatusText = string.Format(Strings.T("status.uninstall_ok"),
                result.RemovedFiles, result.RestoredBackups);
            _host.Notifications.Notify(Strings.T("notify.krostemod_uninstalled"), NotificationLevel.Success);
            RefreshManifestInfo();
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.T("status.error_prefix"), ex.Message);
        }
        finally { IsBusy = false; }
    }
}

public sealed record ModTypeOption(ModTypeId Id, string DisplayName, string Icon, string Description);
