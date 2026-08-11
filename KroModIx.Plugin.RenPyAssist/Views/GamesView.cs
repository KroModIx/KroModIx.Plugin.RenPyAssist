using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Games-Tab: Card-Liste aller registrierten Ren'Py-Spiele mit
/// Cover, Version, Update-Badge und Row-Actions (Play, Update, Set-Thread-URL,
/// Ordner öffnen, Remove).</summary>
public sealed class GamesView : UserControl
{
    public GamesView()
    {
        var rescanBtn = new Button { Content = "🔄  Rescan" };
        rescanBtn.Bind(Button.CommandProperty, new Binding(nameof(GamesViewModel.RescanCommand)));
        ToolTip.SetTip(rescanBtn,
            "Scannt den Root-Ordner erneut nach Ren'Py-Spielen. Neue Container werden " +
            "hinzugefügt, verschwundene entfernt. Metadaten (Thread-URL, Version) " +
            "bestehender Einträge bleiben erhalten.");

        var checkBtn = new Button { Content = "⬇  Updates prüfen" };
        checkBtn.Classes.Add("accent");
        checkBtn.Bind(Button.CommandProperty, new Binding(nameof(GamesViewModel.CheckNowCommand)));
        ToolTip.SetTip(checkBtn,
            "Pollt alle f95zone-Threads sofort (Rate-Limit 1 s/Thread).");

        var searchBox = new TextBox { PlaceholderText = "Spiele filtern …" };
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(GamesViewModel.SearchText))
        { Mode = BindingMode.TwoWay });

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetColumn(rescanBtn, 0);
        Grid.SetColumn(checkBtn, 1);
        Grid.SetColumn(searchBox, 2);
        rescanBtn.Margin = new Thickness(0, 0, 6, 0);
        checkBtn.Margin = new Thickness(0, 0, 12, 0);
        toolbar.Children.Add(rescanBtn);
        toolbar.Children.Add(checkBtn);
        toolbar.Children.Add(searchBox);

        var status = new TextBlock { Margin = new Thickness(0, 10, 0, 0) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(GamesViewModel.StatusText)));

        var list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        list.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(GamesViewModel.Rows)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(GamesViewModel.Selected))
        { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<GameRow>((row, _) => row is null ? null : BuildRowTemplate(), true);

        Content = new DockPanel
        {
            Margin = new Thickness(20, 16, 20, 14),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Bottom),
                list,
            },
        };
    }

    private static Control BuildRowTemplate()
    {
        var coverFrame = new Border
        {
            Width = 120, Height = 160,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
        };
        var coverPanel = new Panel();
        var coverFallback = new TextBlock
        {
            Text = "🎮", FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        coverFallback.Classes.Add("muted");
        coverPanel.Children.Add(coverFallback);
        var coverImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(GameRow.Cover)));
        coverPanel.Children.Add(coverImage);
        coverFrame.Child = coverPanel;

        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(GameRow.DisplayName)));

        var updateBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteGoldBrush"),
        };
        var updateBadgeText = new TextBlock
        {
            FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Black,
        };
        updateBadgeText.Bind(TextBlock.TextProperty, new Binding(nameof(GameRow.UpdateBadgeText)));
        updateBadge.Child = updateBadgeText;
        updateBadge.Bind(Border.IsVisibleProperty, new Binding(nameof(GameRow.HasUpdate)));

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { title, updateBadge } };

        var subPath = new TextBlock { FontSize = 11 };
        subPath.Classes.Add("muted");
        subPath.Bind(TextBlock.TextProperty, new Binding(nameof(GameRow.SubPathText)));

        var versions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var localTb = new TextBlock(); localTb.Classes.Add("muted");
        localTb.Bind(TextBlock.TextProperty, new Binding(nameof(GameRow.VersionText)));
        var sep = new TextBlock { Text = "·" }; sep.Classes.Add("muted");
        var remoteTb = new TextBlock(); remoteTb.Classes.Add("muted");
        remoteTb.Bind(TextBlock.TextProperty, new Binding(nameof(GameRow.RemoteText)));
        versions.Children.Add(localTb); versions.Children.Add(sep); versions.Children.Add(remoteTb);

        // Inline-Editor für die f95zone-Thread-URL — statt Clipboard-Hack
        // oder Extra-Dialog schreibt der User direkt in die Row.
        var threadBox = new TextBox
        {
            FontSize = 11,
            PlaceholderText = "https://f95zone.to/threads/…",
            Margin = new Thickness(0, 6, 6, 0),
        };
        threadBox.Bind(TextBox.TextProperty, new Binding(nameof(GameRow.ThreadUrlDraft))
        { Mode = BindingMode.TwoWay });
        var saveThreadBtn = new Button
        {
            Content = "💾",
            Margin = new Thickness(0, 6, 0, 0),
        };
        BindRowCommand(saveThreadBtn, nameof(GamesViewModel.SetThreadUrlCommand));
        ToolTip.SetTip(saveThreadBtn, "Thread-URL speichern und sofort prüfen.");
        var threadRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        Grid.SetColumn(threadBox, 0);
        Grid.SetColumn(saveThreadBtn, 1);
        threadRow.Children.Add(threadBox);
        threadRow.Children.Add(saveThreadBtn);

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { titleRow, subPath, versions, threadRow },
        };

        var playBtn = new Button { Content = "▶  Start" };
        playBtn.Classes.Add("accent");
        BindRowCommand(playBtn, nameof(GamesViewModel.PlayCommand));

        var updateBtn = new Button { Content = "⬆  Update installieren" };
        updateBtn.Classes.Add("accent");
        BindRowCommand(updateBtn, nameof(GamesViewModel.InstallUpdateCommand));
        updateBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(GameRow.HasUpdate)));

        var openBtn = new Button { Content = "📂  Ordner" };
        openBtn.Classes.Add("ghost");
        BindRowCommand(openBtn, nameof(GamesViewModel.OpenFolderCommand));

        var removeBtn = new Button { Content = "🗑  Entfernen" };
        removeBtn.Classes.Add("danger");
        BindRowCommand(removeBtn, nameof(GamesViewModel.RemoveCommand));

        var actions = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { playBtn, updateBtn, openBtn, removeBtn },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(coverFrame, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(coverFrame);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);

        var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = grid };
        card.Classes.Add("card");
        return card;
    }

    private static void BindRowCommand(Button btn, string commandName)
    {
        btn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + commandName,
        });
        btn.Bind(Button.CommandParameterProperty, new Binding("."));
    }

    private static Control WithDock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }
}
