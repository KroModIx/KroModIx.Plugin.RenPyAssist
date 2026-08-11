using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Einstellungen-Tab v0.5+: enthält Spiel-spezifisch (Thread-URL,
/// Play/Update-Actions) UND plugin-global (f95zone-Login, Downloads-Watch,
/// Poll-Intervall). Alles was in v0.4 auf der Detail-View war ist hier
/// zentral zusammengefasst.</summary>
public sealed class GameSettingsView : UserControl
{
    public GameSettingsView()
    {
        // === Sektion 1: Spiel-spezifisch ===
        var gameTitle = new TextBlock
        {
            FontSize = 18, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        gameTitle.Bind(TextBlock.TextProperty, new Binding(nameof(GameSettingsViewModel.DisplayName)));

        var threadLabel = new TextBlock { Text = "f95zone-Thread-URL", Margin = new Thickness(0, 8, 0, 4) };
        threadLabel.Classes.Add("section-label");
        var threadHelp = new TextBlock
        {
            Text = "Link zum f95zone-Thread. Wenn gesetzt: Version-Checks, Cover, " +
                   "Beschreibung und Genre werden automatisch geladen.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        threadHelp.Classes.Add("secondary");
        var threadBox = new TextBox { PlaceholderText = "https://f95zone.to/threads/…" };
        threadBox.Bind(TextBox.TextProperty, new Binding(nameof(GameSettingsViewModel.ThreadUrlDraft))
        { Mode = BindingMode.TwoWay });
        var saveThreadBtn = new Button { Content = "💾  Thread speichern & prüfen",
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left };
        saveThreadBtn.Classes.Add("accent");
        saveThreadBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.SaveThreadUrlCommand)));
        var lastChecked = new TextBlock { FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
        lastChecked.Classes.Add("secondary");
        lastChecked.Bind(TextBlock.TextProperty, new Binding(nameof(GameSettingsViewModel.LastCheckedText)));

        var actionLabel = new TextBlock { Text = "Aktionen", Margin = new Thickness(0, 20, 0, 4) };
        actionLabel.Classes.Add("section-label");
        var playBtn = new Button { Content = "▶  Start" };
        playBtn.Classes.Add("accent");
        playBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.PlayCommand)));
        var updateBtn = new Button { Content = "⬆  Update installieren" };
        updateBtn.Classes.Add("accent");
        updateBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.InstallUpdateCommand)));
        var checkBtn = new Button { Content = "🔄  Prüfen" };
        checkBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.CheckNowCommand)));
        var folderBtn = new Button { Content = "📂  Ordner" };
        folderBtn.Classes.Add("ghost");
        folderBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.OpenFolderCommand)));
        var cropBtn = new Button { Content = "🖼  Sidebar-Ausschnitt wählen" };
        cropBtn.Classes.Add("ghost");
        cropBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.ChooseSidebarCropCommand)));
        ToolTip.SetTip(cropBtn,
            "Öffnet einen Dialog mit dem Original-Cover — verschiebe den 2:3-Rahmen " +
            "und speichere. Der Ausschnitt landet als Sidebar-Kachel.");
        var actionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { playBtn, updateBtn, checkBtn, folderBtn, cropBtn },
        };

        var gameStatus = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        gameStatus.Classes.Add("muted");
        gameStatus.Bind(TextBlock.TextProperty, new Binding(nameof(GameSettingsViewModel.GameStatus)));

        var containerHint = new TextBlock
        {
            FontSize = 10, Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        containerHint.Classes.Add("secondary");
        containerHint.Bind(TextBlock.TextProperty, new Binding(nameof(GameSettingsViewModel.ContainerPathText)));

        // === Sektion 2: Plugin-global ===
        var globalHeader = new TextBlock
        {
            Text = "Plugin-Einstellungen (global)",
            FontSize = 16, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 36, 0, 12),
        };

        var dlLabel = new TextBlock { Text = "Downloads-Watch-Ordner", Margin = new Thickness(0, 0, 0, 4) };
        dlLabel.Classes.Add("section-label");
        var dlBox = new TextBox { Width = 500, HorizontalAlignment = HorizontalAlignment.Left };
        dlBox.Bind(TextBox.TextProperty, new Binding(nameof(GameSettingsViewModel.DownloadsDir))
        { Mode = BindingMode.TwoWay });
        var pickDlBtn = new Button { Content = "📂  Wählen", Margin = new Thickness(8, 0, 0, 0) };
        pickDlBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.PickDownloadsCommand)));
        var dlRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { dlBox, pickDlBtn } };

        var intervalLabel = new TextBlock { Text = "Update-Check-Intervall (Minuten)",
            Margin = new Thickness(0, 16, 0, 4) };
        intervalLabel.Classes.Add("section-label");
        var intervalBox = new NumericUpDown
        {
            Minimum = 15, Maximum = 1440, FormatString = "0",
            Width = 120, HorizontalAlignment = HorizontalAlignment.Left,
        };
        intervalBox.Bind(NumericUpDown.ValueProperty, new Binding(nameof(GameSettingsViewModel.IntervalMinutes))
        { Mode = BindingMode.TwoWay });

        var saveGlobalBtn = new Button { Content = "💾  Global-Einstellungen speichern",
            Margin = new Thickness(0, 12, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left };
        saveGlobalBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.SaveGlobalSettingsCommand)));
        var globalStatus = new TextBlock();
        globalStatus.Classes.Add("muted");
        globalStatus.Bind(TextBlock.TextProperty, new Binding(nameof(GameSettingsViewModel.GlobalStatus)));

        // f95zone-Login
        var loginHeader = new TextBlock { Text = "f95zone-Login",
            FontSize = 14, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 24, 0, 8) };
        var loginHelp = new TextBlock
        {
            Text = "Login ist optional aber empfohlen für Cover-Downloads. Passwort wird " +
                   "NIE gespeichert — nur Session-Cookies verschlüsselt via Host-Secrets.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        loginHelp.Classes.Add("secondary");
        var userBox = new TextBox
        {
            Width = 400, HorizontalAlignment = HorizontalAlignment.Left,
            PlaceholderText = "Username / Email",
        };
        userBox.Bind(TextBox.TextProperty, new Binding(nameof(GameSettingsViewModel.F95Username))
        { Mode = BindingMode.TwoWay });
        var pwBox = new TextBox
        {
            Width = 400, HorizontalAlignment = HorizontalAlignment.Left,
            PasswordChar = '•', Margin = new Thickness(0, 6, 0, 0),
            PlaceholderText = "Passwort",
        };
        pwBox.Bind(TextBox.TextProperty, new Binding(nameof(GameSettingsViewModel.F95Password))
        { Mode = BindingMode.TwoWay });
        var loginBtn = new Button { Content = "🔐  Einloggen", Margin = new Thickness(0, 10, 0, 0) };
        loginBtn.Classes.Add("accent");
        loginBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.LoginCommand)));
        loginBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(GameSettingsViewModel.IsLoggingIn))
        { Converter = new FuncValueConverter<bool, bool>(v => !v) });
        var logoutBtn = new Button { Content = "🚪  Cookies löschen",
            Margin = new Thickness(8, 10, 0, 0) };
        logoutBtn.Classes.Add("ghost");
        logoutBtn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.LogoutCommand)));
        var loginRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { loginBtn, logoutBtn } };
        var loginStatus = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        loginStatus.Classes.Add("muted");
        loginStatus.Bind(TextBlock.TextProperty, new Binding(nameof(GameSettingsViewModel.LoginStatus)));
        var f95Btn = new Button { Content = "↗  f95zone.to öffnen", Margin = new Thickness(0, 12, 0, 0) };
        f95Btn.Classes.Add("ghost");
        f95Btn.Bind(Button.CommandProperty, new Binding(nameof(GameSettingsViewModel.OpenF95Command)));

        // Assembly
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20),
                MaxWidth = 900,
                Children =
                {
                    gameTitle,
                    threadLabel, threadHelp, threadBox, saveThreadBtn, lastChecked,
                    actionLabel, actionsRow, gameStatus, containerHint,
                    globalHeader,
                    dlLabel, dlRow,
                    intervalLabel, intervalBox,
                    saveGlobalBtn, globalStatus,
                    loginHeader, loginHelp,
                    userBox, pwBox, loginRow, loginStatus, f95Btn,
                },
            },
        };
    }
}
