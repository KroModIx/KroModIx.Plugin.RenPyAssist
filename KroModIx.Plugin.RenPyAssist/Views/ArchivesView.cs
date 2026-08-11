using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Archives-Tab (v0.4+): links Archiv-Liste, mittig Entry-Baum,
/// rechts Preview-Panel (Bild/Text/Video-Frame). Actions: Extract, Extract-All,
/// Extern öffnen.</summary>
public sealed class ArchivesView : UserControl
{
    public ArchivesView()
    {
        // --- Toolbar oben ---
        var scanBtn = new Button { Content = "🔄  Neu scannen" };
        scanBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ScanCommand)));

        var extractBtn = new Button { Content = "⬇  Datei entpacken" };
        extractBtn.Classes.Add("accent");
        extractBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ExtractSelectedCommand)));

        var extractAllBtn = new Button { Content = "⬇⬇  Alles entpacken" };
        extractAllBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ExtractAllCommand)));

        // v0.9: Inline-Video-Playback via ffmpeg-MJPEG-Stream.
        // Sichtbar wenn Video + ffmpeg da. Label toggelt Play/Stopp.
        var inlinePlayBtn = new Button();
        inlinePlayBtn.Classes.Add("accent");
        inlinePlayBtn.Bind(Button.ContentProperty, new Binding(nameof(ArchivesViewModel.InlinePlayButtonLabel)));
        inlinePlayBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.ToggleInlinePlaybackCommand)));
        inlinePlayBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.CanInlinePlay)));

        var externBtn = new Button { Content = "⤴  Extern öffnen" };
        externBtn.Classes.Add("ghost");
        externBtn.Bind(Button.CommandProperty, new Binding(nameof(ArchivesViewModel.OpenExternalCommand)));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { scanBtn, extractBtn, extractAllBtn, inlinePlayBtn, externBtn },
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

        // --- Right: Preview ---
        var previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            MaxHeight = 600,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        previewImage.Bind(Image.SourceProperty, new Binding(nameof(ArchivesViewModel.PreviewImage)));
        previewImage.Bind(Image.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.PreviewImage))
        { Converter = Avalonia.Data.Converters.ObjectConverters.IsNotNull });
        // Video-Thumbnail-Klick öffnet extern (System-Default-Player).
        previewImage.PointerPressed += (_, _) =>
        {
            if (DataContext is ArchivesViewModel vm && vm.CanPlayExternal)
                vm.OpenExternalCommand.Execute(null);
        };
        ToolTip.SetTip(previewImage, "Klick öffnet Video im System-Default-Player");

        var previewText = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 11,
            MaxHeight = 600,
        };
        previewText.Bind(TextBox.TextProperty, new Binding(nameof(ArchivesViewModel.PreviewText)));
        previewText.Bind(TextBox.IsVisibleProperty, new Binding(nameof(ArchivesViewModel.PreviewText))
        { Converter = Avalonia.Data.Converters.ObjectConverters.IsNotNull });

        var previewInfo = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        previewInfo.Classes.Add("muted");
        previewInfo.Bind(TextBlock.TextProperty, new Binding(nameof(ArchivesViewModel.PreviewInfo)));

        var previewCol = new StackPanel
        {
            Children = { previewInfo, previewImage, previewText },
        };

        // --- Layout: 3-column ---
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,2*"),
        };
        Grid.SetColumn(archives, 0);
        Grid.SetColumn(entries, 1);
        var previewScroll = new ScrollViewer { Content = previewCol, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(previewScroll, 2);
        grid.Children.Add(archives);
        grid.Children.Add(entries);
        grid.Children.Add(previewScroll);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                grid,
            },
        };
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
