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
            "Character-Umbenennung inkl. konsistentem Text-Umschreiben via KI (Grammatik!). " +
            "Für Rename ohne KI: nur Character-Objekt-Namen werden getauscht. " +
            "v0.6.0: automatisch, ohne explizites Mapping-UI (kommt v0.6.1)."),
        // Translate: v0.7 — TranslationConfig braucht Pre-translated strings,
        // Batch-KI-Loop muss separat ausgeführt werden. Zu komplex für v0.6.0.
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
        // Für v0.6.0: keine UI, nur Character-Object-Rename via Prompt.
        // v0.6.1 bekommt einen richtigen Dialog mit DataGrid.
        Dispatcher.UIThread.Post(async () =>
            await _host.Dialogs.ShowMessageAsync("Rename-Konfiguration",
                $"{characters.Count} Character(e) erkannt: " +
                string.Join(", ", characters.Take(10).Select(c => $"{c.VarName}=„{c.DisplayName}\"")) +
                (characters.Count > 10 ? " …" : "") + "\n\n" +
                "v0.6.0: automatisch Character-Object-Rename mit KI-Body-Rewriter (falls KI verfügbar). " +
                "Explizite Mappings-UI kommt in v0.6.1."));
        // Empty mapping = kein Rename, aber Analyse läuft trotzdem. Für v0.6.0 überspringen.
        return null;
    }

    private TranslationConfig? AskTranslationConfig(ModAnalysis analysis)
    {
        // v0.6.0: Translate nicht unterstützt. Callback kehrt null zurück
        // (User-Cancel-Semantik) — der Builder bricht den Translate-Build ab.
        return null;
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
