using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Archives-Tab (v0.4+): links Archiv-Liste, mittig Entry-Baum,
/// rechts Preview-Panel (Bild/Text/Video-Frame). v0.9.1: Preview-Panel-
/// Layout an RenPack angeglichen — Play-Buttons als Dock=Bottom am
/// Preview-Panel (immer sichtbar wenn IsMedia), Placeholder-Icons für
/// Video-ohne-Bild + Audio-only, ffmpeg-Missing-Hint. Actions: Extract,
/// Extract-All.</summary>
public sealed class ArchivesView : UserControl
{
    public ArchivesView()
    {
        // --- Toolbar oben (Archiv-Aktionen, kein Preview-Playback mehr) ---
        var scanBtn = new Button { Content = Strings.T("btn.rescan") };
        scanBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ScanCommand)));

        var extractBtn = new Button { Content = Strings.T("btn.extract_selected") };
        extractBtn.Classes.Add("accent");
        extractBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ExtractSelectedCommand)));

        var extractAllBtn = new Button { Content = Strings.T("btn.extract_all") };
        extractAllBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ExtractAllCommand)));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { scanBtn, extractBtn, extractAllBtn },
        };

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ArchivesViewModel.StatusText)));

        // --- Left: Archives list ---
        var archives = new ListBox
        {
            Width = 260,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        archives.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(ArchivesViewModel.Archives)));
        archives.Bind(ListBox.SelectedItemProperty, new Binding(nameof(ArchivesViewModel.SelectedArchive))
        { Mode = BindingMode.TwoWay });
        archives.ItemTemplate = new FuncDataTemplate<ArchiveRow>((row, _) =>
        {
            if (row is null) return null;
            var name = new TextBlock { FontWeight = FontWeight.SemiBold };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(ArchiveRow.FileName)));
            var summary = new TextBlock { FontSize = 11 };
            summary.Classes.Add("muted");
            summary.Bind(TextBlock.TextProperty, new Binding(nameof(ArchiveRow.Summary)));
            return new StackPanel
            {
                Margin = new Thickness(6, 4),
                Children = { name, summary },
            };
        }, true);

        // --- Middle: Entries list ---
        var entries = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        entries.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(ArchivesViewModel.Entries)));
        entries.Bind(ListBox.SelectedItemProperty, new Binding(nameof(ArchivesViewModel.SelectedEntry))
        { Mode = BindingMode.TwoWay });
        entries.ItemTemplate = new FuncDataTemplate<EntryRow>((row, _) =>
        {
            if (row is null) return null;
            var path = new TextBlock
            {
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            path.Bind(TextBlock.TextProperty, new Binding(nameof(EntryRow.DisplayPath)));
            var size = new TextBlock { FontSize = 10 };
            size.Classes.Add("muted");
            size.Bind(TextBlock.TextProperty, new Binding(nameof(EntryRow.SizeText)));
            return new StackPanel
            {
                Margin = new Thickness(6, 2),
                Children = { path, size },
            };
        }, true);

        // --- Right: Preview panel (RenPack-Layout) ---
        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                BuildMainGrid(archives, entries),
            },
        };
    }

    private static Grid BuildMainGrid(Control archives, Control entries)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,2*"),
        };
        Grid.SetColumn(archives, 0);
        Grid.SetColumn(entries, 1);
        var previewPanel = BuildPreviewPanel();
        Grid.SetColumn(previewPanel, 2);
        grid.Children.Add(archives);
        grid.Children.Add(entries);
        grid.Children.Add(previewPanel);
        return grid;
    }

    private static Control BuildPreviewPanel()
    {
        // --- Header: Info-Zeile ---
        var previewInfo = new TextBlock
        {
            Margin = new Thickness(12, 8, 12, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        previewInfo.Classes.Add("muted");
        previewInfo.Bind(TextBlock.TextProperty, new Binding(nameof(ArchivesViewModel.PreviewInfo)));

        // --- Bottom: Play-Button-Panel (nur bei IsMedia sichtbar) ---
        // "▶ Inline abspielen" — sichtbar wenn CanInlinePlay && !IsPlayingInline
        var inlinePlayBtn = new Button { Content = Strings.T("btn.play_inline") };
        inlinePlayBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ToggleInlinePlaybackCommand)));
        ToolTip.SetTip(inlinePlayBtn, Strings.T("tooltip.inline_play"));
        // Sichtbar wenn Video + ffmpeg da UND grade nicht abspielend — sonst
        // zeigen wir den Pause-Btn als Geschwister. RenPack-Muster: separate
        // Buttons statt Toggle-Text, sauber-exklusiv per MultiBinding.
        var playVisibility = new MultiBinding
        {
            Converter = BoolConverters.And,
            Bindings =
            {
                new Binding(nameof(ArchivesViewModel.CanInlinePlay)),
                new Binding(nameof(ArchivesViewModel.IsPlayingInline)) { Converter = BoolConverters.Not },
            },
        };
        inlinePlayBtn.Bind(Button.IsVisibleProperty, playVisibility);

        // "⏸ Pause" — sichtbar wenn IsPlayingInline
        var pauseBtn = new Button { Content = Strings.T("btn.pause") };
        pauseBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ToggleInlinePlaybackCommand)));
        pauseBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.IsPlayingInline)));
        ToolTip.SetTip(pauseBtn, Strings.T("tooltip.inline_stop"));

        // "⤴ Extern öffnen" (accent, immer wenn IsMedia)
        var externBtn = new Button { Content = Strings.T("btn.open_external") };
        externBtn.Classes.Add("accent");
        externBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.OpenExternalCommand)));
        ToolTip.SetTip(externBtn, Strings.T("tooltip.open_external"));

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 12),
            Spacing = 8,
            Children = { inlinePlayBtn, pauseBtn, externBtn },
        };
        buttonPanel.Bind(StackPanel.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.IsMedia)));
        DockPanel.SetDock(buttonPanel, Dock.Bottom);

        // --- Center: Overlay-Grid (Text/Bild/Icons in einer Zelle) ---
        var previewText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 11,
            Margin = new Thickness(12, 0),
        };
        previewText.Bind(TextBox.TextProperty, new Binding(nameof(ArchivesViewModel.PreviewText)));
        previewText.Bind(TextBox.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.PreviewText))
        { Converter = ObjectConverters.IsNotNull });

        var previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12),
        };
        previewImage.Bind(Image.SourceProperty, new Binding(nameof(ArchivesViewModel.PreviewImage)));
        previewImage.Bind(Image.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.PreviewImage))
        { Converter = ObjectConverters.IsNotNull });

        // Video ohne Standbild → 🎬-Icon (visible wenn IsVideo && kein Bild
        // extrahiert wurde). Signalisiert: hier steckt ein Video, klick Play.
        var videoIcon = new TextBlock
        {
            Text = "🎬",
            FontSize = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        videoIcon.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("KrosteGoldBrush"));
        var videoIconVisibility = new MultiBinding
        {
            Converter = BoolConverters.And,
            Bindings =
            {
                new Binding(nameof(ArchivesViewModel.IsVideo)),
                new Binding(nameof(ArchivesViewModel.PreviewImage)) { Converter = ObjectConverters.IsNull },
            },
        };
        videoIcon.Bind(TextBlock.IsVisibleProperty, videoIconVisibility);

        // Audio-only → 🎵-Icon
        var audioIcon = new TextBlock
        {
            Text = "🎵",
            FontSize = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        audioIcon.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("KrosteGoldBrush"));
        audioIcon.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.IsAudioOnly)));

        // ffmpeg fehlt → Install-Hinweis (nur bei Video)
        var ffmpegHint = new TextBlock
        {
            Text = Strings.T("hint.ffmpeg_missing"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 420,
            Padding = new Thickness(24),
            TextAlignment = TextAlignment.Center,
        };
        ffmpegHint.Classes.Add("secondary");
        ffmpegHint.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.ShowFfmpegMissingHint)));

        // Grid ohne RowDefs/ColDefs = alle Kinder in einer Zelle (Overlay).
        // Z-Order = XAML-/Add-Reihenfolge (unten = erste, oben = letzte).
        var overlay = new Grid();
        overlay.Children.Add(videoIcon);
        overlay.Children.Add(audioIcon);
        overlay.Children.Add(ffmpegHint);
        overlay.Children.Add(previewText);
        overlay.Children.Add(previewImage);

        // Preview-Panel: DockPanel mit Header(Top) + Buttons(Bottom) + Overlay(Fill)
        var panel = new DockPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            LastChildFill = true,
        };
        DockPanel.SetDock(previewInfo, Dock.Top);
        panel.Children.Add(previewInfo);
        panel.Children.Add(buttonPanel);
        panel.Children.Add(overlay);

        // Card-Look mit Border
        var card = new Border
        {
            Child = panel,
            ClipToBounds = true,
            Margin = new Thickness(12, 0, 0, 0),
        };
        card.Classes.Add("card-flat");
        return card;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
