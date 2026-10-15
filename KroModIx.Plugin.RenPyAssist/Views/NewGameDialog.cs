using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Was der User im Dialog festgelegt hat.</summary>
public sealed record NewGameRequest(string ArchivePath, string DisplayName, string? ThreadUrl);

/// <summary>Abfrage für „Spiel aus Archiv anlegen": Name und optional der
/// f95zone-Thread.
///
/// <para>Der Name kommt vorausgefüllt aus <see cref="ArchiveNameParser"/>.
/// An den 22 Archiven, die hier liegen, trifft der Vorschlag 15 mal — der
/// Rest ist nicht mechanisch ableitbar („Shadowsofseduction" wird zu
/// „Shadows of seduction", und wo die Wortgrenzen liegen, weiß nur ein
/// Mensch). Deshalb ein Eingabefeld mit Vorschlag statt einer Festlegung,
/// und der Ordnername steht live darunter, damit vor dem Anlegen sichtbar
/// ist, was auf der Platte entsteht.</para>
///
/// <para>Die Thread-URL ist optional: ohne sie funktioniert alles außer der
/// Update-Erkennung, und sie lässt sich jederzeit im Einstellungen-Tab
/// nachtragen.</para></summary>
public sealed class NewGameDialog : Window
{
    private readonly TextBox _name;
    private readonly TextBox _thread;
    private readonly TextBlock _pfadVorschau;
    private readonly string _archivePath;
    private readonly string _gamesRoot;
    private NewGameRequest? _result;

    public NewGameDialog(string archivePath, string gamesRoot, string nameSuggestion)
    {
        _archivePath = archivePath;
        _gamesRoot = gamesRoot;

        Title = Strings.T("dialog.new_game_title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        WindowDecorations = WindowDecorations.BorderOnly;

        var header = new TextBlock { Text = Strings.T("dialog.new_game_title") };
        header.Classes.Add("h2");

        var archivZeile = new TextBlock
        {
            Text = Path.GetFileName(archivePath),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 14),
        };
        archivZeile.Classes.Add("muted");

        _name = new TextBox { Text = nameSuggestion, PlaceholderText = Strings.T("dialog.new_game_name") };
        _name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
        _name.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdateVorschau();
        };

        _pfadVorschau = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14),
        };
        _pfadVorschau.Classes.Add("muted");

        _thread = new TextBox
        {
            PlaceholderText = Strings.T("dialog.new_game_thread_hint"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        _thread.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };

        var okBtn = new Button { Content = Strings.T("dialog.new_game_create"), IsDefault = true };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) => Accept();

        var cancelBtn = new Button { Content = Strings.T("btn.cancel"), IsCancel = true };
        cancelBtn.Classes.Add("ghost");
        cancelBtn.Click += (_, _) => Cancel();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancelBtn, okBtn },
        };

        var card = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Children =
                {
                    header, archivZeile,
                    Label(Strings.T("dialog.new_game_name")), _name, _pfadVorschau,
                    Label(Strings.T("dialog.new_game_thread")), _thread,
                    buttons,
                },
            },
        };
        card.Classes.Add("card");
        Content = card;

        UpdateVorschau();
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    private static TextBlock Label(string text)
    {
        var tb = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 4) };
        tb.Classes.Add("muted");
        return tb;
    }

    /// <summary>Zeigt den Ordner, der gleich entsteht — mit dem bereinigten
    /// Namen, nicht mit der Roheingabe. Ein Doppelpunkt oder Schrägstrich im
    /// Titel wird zu einem Leerzeichen, und das soll man vorher sehen.</summary>
    private void UpdateVorschau()
    {
        var safe = GameInstallService.SanitizeFolderName(_name.Text ?? "");
        _pfadVorschau.Text = safe.Length == 0
            ? Strings.T("dialog.new_game_need_name")
            : Path.Combine(_gamesRoot, safe);
    }

    private void Accept()
    {
        var name = GameInstallService.SanitizeFolderName(_name.Text ?? "");
        if (name.Length == 0) { _name.Focus(); return; }
        var url = _thread.Text?.Trim();
        _result = new NewGameRequest(_archivePath, name,
            string.IsNullOrWhiteSpace(url) ? null : url);
        Close();
    }

    private void Cancel() { _result = null; Close(); }

    /// <summary>Öffnet den Dialog modal am MainWindow. Null bei Abbruch.</summary>
    public static async Task<NewGameRequest?> PromptAsync(
        string archivePath, string gamesRoot, string nameSuggestion)
    {
        var dialog = new NewGameDialog(archivePath, gamesRoot, nameSuggestion);
        var owner = Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk ? desk.MainWindow : null;
        if (owner is null) { dialog.Show(); return null; }
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}
