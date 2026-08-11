using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Per-Ren'Py-Spiel-Detail-Ansicht (v0.3+): Cover groß links,
/// Titel + Version + Update-Badge + Sub-Path + Thread-URL-Editor rechts,
/// Actions-Zeile unten (Play, Update, Ordner).</summary>
public sealed class RenPyGameView : UserControl
{
    public RenPyGameView()
    {
        // --- Cover-Bereich (links) ---
        var coverFrame = new Border
        {
            Width = 220, Height = 300,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
        };
        var coverPanel = new Panel();
        var coverFallback = new TextBlock
        {
            Text = "🎮", FontSize = 72,
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
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(RenPyGameViewModel.Cover)));
        coverPanel.Children.Add(coverImage);
        coverFrame.Child = coverPanel;

        // --- Details rechts oben ---
        var title = new TextBlock
        {
            FontSize = 24, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.DisplayName)));

        var updateBadge = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteGoldBrush"),
        };
        var updateBadgeText = new TextBlock
        {
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Black,
        };
        updateBadgeText.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.UpdateBadgeText)));
        updateBadge.Child = updateBadgeText;
        updateBadge.Bind(Border.IsVisibleProperty, new Binding(nameof(RenPyGameViewModel.HasUpdate)));

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { title, updateBadge },
        };

        var subPath = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        subPath.Classes.Add("muted");
        subPath.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.SubPathText))
        { StringFormat = "Sub-Path: {0}" });

        var versionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var localTb = new TextBlock(); localTb.Classes.Add("muted");
        localTb.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.VersionText)));
        var sep = new TextBlock { Text = "·" }; sep.Classes.Add("muted");
        var remoteTb = new TextBlock(); remoteTb.Classes.Add("muted");
        remoteTb.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.RemoteText)));
        versionsRow.Children.Add(localTb); versionsRow.Children.Add(sep); versionsRow.Children.Add(remoteTb);

        var lastChecked = new TextBlock { FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        lastChecked.Classes.Add("secondary");
        lastChecked.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.LastCheckedText)));

        // --- Thread-URL-Editor ---
        var threadLabel = new TextBlock { Text = "f95zone-Thread:", Margin = new Thickness(0, 20, 0, 4) };
        threadLabel.Classes.Add("section-label");
        var threadBox = new TextBox
        {
            PlaceholderText = "https://f95zone.to/threads/…",
        };
        threadBox.Bind(TextBox.TextProperty, new Binding(nameof(RenPyGameViewModel.ThreadUrlDraft))
        { Mode = BindingMode.TwoWay });
        var saveThreadBtn = new Button { Content = "💾 Speichern", Margin = new Thickness(0, 6, 0, 0) };
        saveThreadBtn.Bind(Button.CommandProperty, new Binding(nameof(RenPyGameViewModel.SaveThreadUrlCommand)));

        // --- Actions-Zeile ---
        var playBtn = new Button { Content = "▶  Start" };
        playBtn.Classes.Add("accent");
        playBtn.Bind(Button.CommandProperty, new Binding(nameof(RenPyGameViewModel.PlayCommand)));

        var checkBtn = new Button { Content = "🔄  Prüfen" };
        checkBtn.Bind(Button.CommandProperty, new Binding(nameof(RenPyGameViewModel.CheckNowCommand)));

        var updateBtn = new Button { Content = "⬆  Update installieren" };
        updateBtn.Classes.Add("accent");
        updateBtn.Bind(Button.CommandProperty, new Binding(nameof(RenPyGameViewModel.InstallUpdateCommand)));

        var folderBtn = new Button { Content = "📂  Ordner" };
        folderBtn.Classes.Add("ghost");
        folderBtn.Bind(Button.CommandProperty, new Binding(nameof(RenPyGameViewModel.OpenFolderCommand)));

        var actionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 20, 0, 0),
            Children = { playBtn, updateBtn, checkBtn, folderBtn },
        };

        var status = new TextBlock { Margin = new Thickness(0, 12, 0, 0) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.StatusText)));

        var pathHint = new TextBlock
        {
            FontSize = 10, Margin = new Thickness(0, 20, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        pathHint.Classes.Add("secondary");
        pathHint.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.ContainerPath))
        { StringFormat = "Container: {0}" });

        // --- Layout ---
        var details = new StackPanel
        {
            Margin = new Thickness(20, 0, 0, 0),
            Children = { titleRow, subPath, versionsRow, lastChecked,
                threadLabel, threadBox, saveThreadBtn,
                actionsRow, status, pathHint },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(coverFrame, 0);
        Grid.SetColumn(details, 1);
        grid.Children.Add(coverFrame);
        grid.Children.Add(details);

        Content = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = grid,
            },
        };
    }
}
