using System;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Views;

namespace KroModIx.Plugin.RenPyAssist.Services;

/// <summary>Der ganze Weg von „da liegt ein Archiv" bis „die Kachel steht in
/// der Sidebar": Spiele-Ordner bestimmen, Datei wählen, Inhalt prüfen, Name
/// und Thread abfragen, auspacken, registrieren, beim Host anmelden.
///
/// <para><b>Warum als Dienst und nicht im ViewModel:</b> der Einstieg ist
/// seit v0.28.0 eine globale Aktion (<see cref="IGlobalAction"/>, Host
/// v1.36) und hängt damit an keinem Spiel mehr. Vorher lag der Ablauf im
/// <c>RenPyGameViewModel</c> und war nur über die Übersicht eines
/// <em>anderen</em> Spiels erreichbar — man brauchte ein Spiel, um ein Spiel
/// anzulegen.</para></summary>
public sealed class NewGameFlow
{
    private readonly GameInstallService _installer;
    private readonly GamesRegistry _registry;
    private readonly RenPySettingsService _settings;
    private readonly IHostServices _host;

    public NewGameFlow(GameInstallService installer, GamesRegistry registry,
        RenPySettingsService settings, IHostServices host)
    {
        _installer = installer;
        _registry = registry;
        _settings = settings;
        _host = host;
    }

    /// <summary>Führt den Ablauf aus. Bricht der User irgendwo ab, passiert
    /// nichts — jeder Abbruch ist ein stilles Return, keine Meldung.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Der Spiele-Ordner wird aus den vorhandenen Containern abgeleitet.
        // Eine Einstellung dafuer gibt es, sie steht in der Praxis aber leer,
        // weil die Spiele ueber den Host-Wizard kamen.
        var root = GameInstallService.GuessGamesRoot(_registry.Games);
        if (root is null)
        {
            await _host.Dialogs.ShowMessageAsync(
                Strings.T("dialog.no_games_root"), Strings.T("dialog.no_games_root_msg"));
            return;
        }

        var archiv = await _host.Dialogs.PickFileInAsync(
            Strings.T("dialog.pick_archive"), _settings.Current.DownloadsWatchDir,
            ("Archiv", new[] { "*.zip", "*.rar", "*.7z" }));
        if (string.IsNullOrWhiteSpace(archiv)) return;

        // Vorab pruefen statt den User 6 GB auspacken zu lassen, um dann zu
        // erfahren dass es kein Ren'Py-Spiel war.
        if (!_installer.LooksLikeRenPyGame(archiv!))
        {
            await _host.Dialogs.ShowMessageAsync(
                Strings.T("dialog.not_renpy"), Strings.T("dialog.not_renpy_msg"));
            return;
        }

        // Name aus dem Build-Ordner im Archiv, sonst aus dem Dateinamen — der
        // Build-Ordner ist der genauere Ausgangspunkt.
        var quelle = _installer.DetectBuildFolder(archiv!) ?? archiv!;
        var wunsch = await NewGameDialog.PromptAsync(archiv!, root,
            ArchiveNameParser.SuggestName(quelle));
        if (wunsch is null) return;

        using var progress = _host.BeginProgress(
            string.Format(Strings.T("dialog.new_game_running"), wunsch.DisplayName));

        var res = await _installer.InstallAsync(wunsch.ArchivePath, root, wunsch.DisplayName, ct: ct)
            .ConfigureAwait(true);
        if (!res.Success || res.ContainerPath is null)
        {
            await _host.Dialogs.ShowMessageAsync(
                Strings.T("dialog.new_game_fail"), res.Error ?? "");
            return;
        }

        // Registry zuerst: dann traegt die Kachel ab der ersten Sekunde
        // Version und Thread, statt sie beim naechsten Scan nachzuziehen.
        var eintrag = _registry.EnsureFromContainer(res.ContainerPath);
        if (!string.IsNullOrWhiteSpace(wunsch.ThreadUrl))
        {
            eintrag.ThreadUrl = wunsch.ThreadUrl;
            _registry.Update(eintrag);
        }

        // Erst jetzt beim Host anmelden. Der legt die Kachel an und meldet das
        // Spiel per OnGameAddedAsync zurueck — die Registry steht da schon,
        // der Rueckruf findet also einen vollstaendigen Eintrag.
        if (!_host.TryAddManualGame(wunsch.DisplayName, res.ContainerPath, "renpy"))
            _host.Logger.Warn("Kachel fuer {Name} konnte nicht angelegt werden "
                + "(Pfad schon belegt?)", wunsch.DisplayName);

        _host.Notifications.Notify(
            string.Format(Strings.T("dialog.new_game_done"), wunsch.DisplayName),
            NotificationLevel.Success);
    }
}

/// <summary>„Spiel aus Archiv" als spiel-unabhängige Aktion in der Sidebar
/// (Host v1.36+). Hier und nicht in einem Tab, weil es beim Anlegen des
/// ersten Spiels noch kein Spiel gibt, in dessen Tab der Knopf stehen
/// könnte.</summary>
public sealed class NewGameAction : IGlobalAction
{
    private readonly NewGameFlow _flow;
    private readonly GamesRegistry _registry;

    public NewGameAction(NewGameFlow flow, GamesRegistry registry)
    {
        _flow = flow;
        _registry = registry;
    }

    public string Id => "new-game-from-archive";
    public string Label => Strings.T("action.new_game");
    public string Icon => "\U0001F4E6"; // 📦
    public string? Description => Strings.T("action.new_game_hint");
    public int Order => 10;

    /// <summary>Braucht mindestens ein bekanntes Spiel, weil der Zielordner
    /// aus den vorhandenen Containern abgeleitet wird. Ohne eines führt der
    /// Weg über den Host-Wizard — der Flow sagt das auch, aber ein
    /// ausgegrauter Eintrag ist ehrlicher als einer, der nur eine Erklärung
    /// öffnet.</summary>
    public bool IsEnabled => _registry.Games.Count > 0;

    public Task InvokeAsync(CancellationToken ct = default) => _flow.RunAsync(ct);
}
