using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Per-Ren'Py-Spiel Übersichts-View v0.5+ (Design-Änderung vom User):
/// Titel groß zentriert · Cover darunter zentriert · Beschreibung
/// (KI-übersetzt in System-Locale) · Genre-Chips. Actions + Thread-URL
/// sind in den Einstellungen-Tab (⚙) gewandert — diese View ist reine
/// Anzeige, wie eine Steam-Detail-Seite.</summary>
public sealed class RenPyGameView : UserControl
{
    public RenPyGameView()
    {
        // --- Titel groß zentriert ---
        var title = new TextBlock
        {
            FontSize = 32,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.DisplayName)));

        // Update-Badge kleiner unter dem Titel
        var updateBadge = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteGoldBrush"),
        };
        var updateBadgeText = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Black,
        };
        updateBadgeText.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.UpdateBadgeText)));
        updateBadge.Child = updateBadgeText;
        updateBadge.Bind(Border.IsVisibleProperty, new Binding(nameof(RenPyGameViewModel.HasUpdate)));

        var versionInfo = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        versionInfo.Classes.Add("muted");
        versionInfo.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.VersionInfo)));

        var subPath = new TextBlock
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        };
        subPath.Classes.Add("secondary");
        subPath.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.SubPathText)));

        // --- Cover zentriert ---
        var coverFrame = new Border
        {
            Width = 400, Height = 550,
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 20),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
        };
        var coverPanel = new Panel();
        var coverFallback = new TextBlock
        {
            Text = "🎮", FontSize = 96,
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

        // --- Beschreibung (KI-übersetzt oder Original) ---
        var descHeader = new TextBlock
        {
            Text = "Beschreibung",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4),
        };
        descHeader.Classes.Add("section-label");

        var descText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 800,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
        };
        descText.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.DescriptionText)));

        var translationHint = new TextBlock
        {
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
        };
        translationHint.Classes.Add("secondary");
        translationHint.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.TranslationHint)));

        // --- Genre-Chips ---
        var genreHeader = new TextBlock
        {
            Text = "Genre",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 6),
        };
        genreHeader.Classes.Add("section-label");

        var genrePanel = new ItemsControl
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 800,
        };
        genrePanel.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(RenPyGameViewModel.Genres)));
        genrePanel.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemSpacing = 6,
            LineSpacing = 6,
        });
        genrePanel.ItemTemplate = new FuncDataTemplate<string>((tag, _) =>
        {
            if (tag is null) return null;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3),
                [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
            };
            var chipText = new TextBlock
            {
                Text = tag,
                FontSize = 11,
            };
            chip.Child = chipText;
            return chip;
        }, true);

        // --- No-Thread-Hint (nur wenn kein Thread-URL gesetzt) ---
        var noThreadHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 700,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
            FontStyle = FontStyle.Italic,
        };
        noThreadHint.Classes.Add("muted");
        noThreadHint.Bind(TextBlock.TextProperty, new Binding(nameof(RenPyGameViewModel.NoThreadHint)));
        noThreadHint.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(RenPyGameViewModel.HasThread))
        { Converter = new Avalonia.Data.Converters.FuncValueConverter<bool, bool>(v => !v) });

        // --- Layout: zentrierter StackPanel im ScrollViewer ---
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(24, 32, 24, 24),
            Children =
            {
                title, updateBadge, versionInfo, subPath,
                coverFrame,
                descHeader, descText, translationHint,
                genreHeader, genrePanel,
                noThreadHint,
            },
        };

        Content = new ScrollViewer { Content = stack };
    }
}
