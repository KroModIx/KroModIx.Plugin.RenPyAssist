using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Einstellungen-Tab: Root-Ordner, Downloads-Watch, Poll-Intervall
/// und f95zone-Login (Username/Passwort → Cookies).</summary>
public sealed class SettingsView : UserControl
{
    public SettingsView()
    {
        var title = new TextBlock { Text = "Ren'Py Assist — Einstellungen", Margin = new Thickness(0, 0, 0, 12) };
        title.Classes.Add("h1");

        // --- Root-Ordner ---
        var rootLabel = new TextBlock { Text = "Ren'Py-Root-Ordner:", Margin = new Thickness(0, 0, 0, 4) };
        rootLabel.Classes.Add("section-label");
        var rootHelp = new TextBlock
        {
            Text = "Verzeichnis mit deinen Ren'Py-Spielen (jedes Spiel als eigener Container-Unterordner).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        rootHelp.Classes.Add("secondary");
        var rootBox = new TextBox { Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
        rootBox.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.GamesRoot)) { Mode = BindingMode.TwoWay });
        var pickRootBtn = new Button { Content = "📂  Ordner wählen", Margin = new Thickness(8, 0, 0, 0) };
        pickRootBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.PickRootCommand)));
        var rootRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { rootBox, pickRootBtn } };

        // --- Downloads-Ordner ---
        var dlLabel = new TextBlock { Text = "Downloads-Watch-Ordner:", Margin = new Thickness(0, 20, 0, 4) };
        dlLabel.Classes.Add("section-label");
        var dlHelp = new TextBlock
        {
            Text = "Ordner der auf neu erschienene ZIPs überwacht wird (Default: ~/Downloads).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        dlHelp.Classes.Add("secondary");
        var dlBox = new TextBox { Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
        dlBox.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.DownloadsDir)) { Mode = BindingMode.TwoWay });
        var pickDlBtn = new Button { Content = "📂  Ordner wählen", Margin = new Thickness(8, 0, 0, 0) };
        pickDlBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.PickDownloadsCommand)));
        var dlRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { dlBox, pickDlBtn } };

        // --- Poll-Intervall ---
        var intervalLabel = new TextBlock { Text = "Update-Check-Intervall (Minuten):", Margin = new Thickness(0, 20, 0, 4) };
        intervalLabel.Classes.Add("section-label");
        var intervalHelp = new TextBlock
        {
            Text = "Wie oft der Worker f95zone-Threads auf neue Versionen pollt. " +
                "60 Min ist der empfohlene Wert (Rate-Limit-Rücksicht).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        intervalHelp.Classes.Add("secondary");
        var intervalBox = new NumericUpDown
        {
            Minimum = 15, Maximum = 1440, FormatString = "0",
            Width = 120, HorizontalAlignment = HorizontalAlignment.Left,
        };
        intervalBox.Bind(NumericUpDown.ValueProperty, new Binding(nameof(SettingsViewModel.IntervalMinutes))
        { Mode = BindingMode.TwoWay });

        var saveBtn = new Button
        {
            Content = "💾  Speichern",
            Margin = new Thickness(0, 20, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        saveBtn.Classes.Add("accent");
        saveBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.SaveCommand)));

        var statusTb = new TextBlock();
        statusTb.Classes.Add("muted");
        statusTb.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.StatusText)));

        // --- f95zone-Login ---
        var loginTitle = new TextBlock { Text = "f95zone-Login", Margin = new Thickness(0, 32, 0, 8) };
        loginTitle.Classes.Add("h2");
        var loginHelp = new TextBlock
        {
            Text = "Login ist optional, wird aber für Cover-Downloads und Thread-" +
                "Details empfohlen. Passwort wird nur zur Login-Anfrage benutzt und " +
                "nicht gespeichert — nur die Session-Cookies landen verschlüsselt auf " +
                "der Platte.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        loginHelp.Classes.Add("secondary");

        var userLabel = new TextBlock { Text = "Username / Email:", Margin = new Thickness(0, 0, 0, 4) };
        userLabel.Classes.Add("section-label");
        var userBox = new TextBox { Width = 400, HorizontalAlignment = HorizontalAlignment.Left };
        userBox.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.F95Username)) { Mode = BindingMode.TwoWay });

        var pwLabel = new TextBlock { Text = "Passwort:", Margin = new Thickness(0, 10, 0, 4) };
        pwLabel.Classes.Add("section-label");
        var pwBox = new TextBox
        {
            Width = 400, HorizontalAlignment = HorizontalAlignment.Left,
            PasswordChar = '•',
        };
        pwBox.Bind(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.F95Password)) { Mode = BindingMode.TwoWay });

        var loginBtn = new Button { Content = "🔐  Einloggen", Margin = new Thickness(0, 12, 0, 8) };
        loginBtn.Classes.Add("accent");
        loginBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.LoginCommand)));
        loginBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(SettingsViewModel.IsLoggingIn))
        { Converter = new FuncValueConverter<bool, bool>(v => !v) });

        var logoutBtn = new Button { Content = "🚪  Cookies löschen", Margin = new Thickness(8, 12, 0, 8) };
        logoutBtn.Classes.Add("ghost");
        logoutBtn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.LogoutCommand)));

        var loginRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { loginBtn, logoutBtn } };

        var loginStatus = new TextBlock();
        loginStatus.Classes.Add("muted");
        loginStatus.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsViewModel.LoginStatusText)));

        var f95Btn = new Button { Content = "↗  f95zone.to öffnen", Margin = new Thickness(0, 16, 0, 0) };
        f95Btn.Classes.Add("ghost");
        f95Btn.Bind(Button.CommandProperty, new Binding(nameof(SettingsViewModel.OpenF95Command)));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20, 16, 20, 14),
                Children =
                {
                    title,
                    rootLabel, rootHelp, rootRow,
                    dlLabel, dlHelp, dlRow,
                    intervalLabel, intervalHelp, intervalBox,
                    saveBtn, statusTb,
                    loginTitle, loginHelp,
                    userLabel, userBox,
                    pwLabel, pwBox,
                    loginRow, loginStatus,
                    f95Btn,
                },
            },
        };
    }
}
