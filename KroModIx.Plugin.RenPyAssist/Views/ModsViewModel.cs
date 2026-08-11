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
    private readonly IHostServices _host;

    [ObservableProperty] private ModTypeOption? _selectedType;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Wähle einen Mod-Typ und klick 'Bauen'.";
    [ObservableProperty] private int _progressCurrent;
    [ObservableProperty] private int _progressTotal;
    [ObservableProperty] private string _progressFile = "";
    [ObservableProperty] private string _manifestInfo = "";
    [ObservableProperty] private bool _hasManifest;
    [ObservableProperty] private string _targetLocale = "de";

    public ObservableCollection<ModTypeOption> Types { get; } = new()
    {
        new(ModTypeId.Walkthrough, "Walkthrough", "🚩",
            "Zeigt in Choice-Menus die besten Optionen — Variablen-basiert per Regex-Analyse."),
        new(ModTypeId.Cheat, "Cheat", "💰",
            "F11-Overlay im Spiel: alle Store-Variablen live editieren (Geld, Beziehungswerte, Flags)."),
        new(ModTypeId.Rename, "Rename", "✏",
            "Character-Umbenennung mit Editor-Dialog (Alt→Neu). Wenn KI konfiguriert: " +
            "Body-Texte werden konsistent umgeschrieben (Grammatik, Beziehungswörter)."),
        new(ModTypeId.Translate, "Translate", "🌐",
            "KI-Batch-Übersetzung aller Dialoge in eine Zielsprache. Braucht Host-KI " +
            "(Ollama/Cloud). Ollama: ~5-10 s/Batch, Cloud: ~2-3 s/Batch. Bei 500 Says ≈ 1-3 min."),
    };

    public ModsViewModel(string containerPath, string? activeSubPath,
        OneClickModBuilder builder, IHostServices host)
    {
        _containerPath = containerPath;
        _activeSubPath = activeSubPath;
        _builder = builder;
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
            ? $"KrosteMod installiert (Manifest: {manifestPath})"
            : "Kein KrosteMod aktiv.";
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (SelectedType is null) return;
        if (!Directory.Exists(GameDir))
        {
            await _host.Dialogs.ShowMessageAsync("game/-Ordner nicht gefunden",
                $"Erwartet: {GameDir}");
            return;
        }

        // KI ist optional (Rename nutzt KI für Body-Rewrite falls verfügbar,
        // Walkthrough/Cheat kommen ohne KI aus).

        var ok = await _host.Dialogs.ConfirmAsync($"{SelectedType.DisplayName}-Mod bauen?",
            $"Der Mod wird für „{Path.GetFileName(_containerPath)}\" gebaut und in " +
            $"„{GameDir}\" deployt. Alle originalen .rpyc werden als .krostemod-bak " +
            $"gesichert. Deinstallation über „🗑 Deinstallieren\" — restauriert Originale.\n\n" +
            $"Fortfahren?");
        if (!ok) return;

        try
        {
            IsBusy = true;
            StatusText = $"Baue {SelectedType.DisplayName} …";
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

            StatusText = $"✔ Mod deployed: {result.DeployedFileCount} Datei(en) in {GameDir}";
            _host.Notifications.Notify(
                $"{SelectedType.DisplayName} für {Path.GetFileName(_containerPath)} installiert " +
                $"({result.DeployedFileCount} Datei(en))",
                NotificationLevel.Success);
            RefreshManifestInfo();
        }
        catch (Exception ex)
        {
            StatusText = "Fehler: " + ex.Message;
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
                StatusText = $"🌐 KI-Übersetzung ({targetLang.ToNativeName()}) läuft …";
                ProgressCurrent = 0;
                ProgressTotal = uniqueTexts.Count;
            });

            var translator = new KrosteAiTranslator(new HostAiProviderAdapter(_host));
            var progress = new Progress<AiTranslateProgress>(p => Dispatcher.UIThread.Post(() =>
            {
                ProgressCurrent = p.Done;
                ProgressTotal = p.Total;
                ProgressFile = $"KI-Übersetzung {p.CurrentLanguage.ToNativeName()}";
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
                StatusText = "KI-Übersetzung fehlgeschlagen: " + ex.Message);
            return null;
        }
    }

    private static Avalonia.Controls.Window? MainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desk
            ? desk.MainWindow : null;
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (!HasManifest)
        {
            StatusText = "Kein KrosteMod installiert — nichts zu deinstallieren.";
            return;
        }
        var ok = await _host.Dialogs.ConfirmAsync("KrosteMod deinstallieren?",
            $"Alle modifizierten .rpy werden gelöscht, .rpyc-Backups (.krostemod-bak) werden " +
            $"restauriert.\n\nFortfahren?");
        if (!ok) return;
        try
        {
            IsBusy = true;
            StatusText = "Deinstalliere …";
            var result = await Task.Run(() => _builder.Uninstall(GameDir));
            StatusText = $"✔ {result.RemovedFiles} Datei(en) entfernt, {result.RestoredBackups} Backup(s) restauriert.";
            _host.Notifications.Notify("KrosteMod deinstalliert", NotificationLevel.Success);
            RefreshManifestInfo();
        }
        catch (Exception ex)
        {
            StatusText = "Fehler: " + ex.Message;
        }
        finally { IsBusy = false; }
    }
}

public sealed record ModTypeOption(ModTypeId Id, string DisplayName, string Icon, string Description);
