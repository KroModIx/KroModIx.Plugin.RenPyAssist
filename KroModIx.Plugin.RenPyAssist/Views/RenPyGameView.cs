using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KroModIx.Plugin.RenPyAssist.Services;

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

        // --- Thread-Button (v0.17): Ein-Klick-Weg in den f95zone-Thread.
        //     Sitzt bewusst weit oben (direkt unter Titel/Version), weil das
        //     der haeufigste Grund ist die Uebersicht ueberhaupt zu oeffnen.
        //     Ohne verknuepften Thread ausgeblendet — dort greift der
        //     NoThreadHint unten. ---
        var openThreadBtn = new Button
        {
            Content = Strings.T("btn.open_thread"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        };
        openThreadBtn.Classes.Add("accent");
        openThreadBtn.Bind(Button.CommandProperty, new Binding(nameof(RenPyGameViewModel.OpenThreadCommand)));
        openThreadBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(RenPyGameViewModel.HasThread)));
        var openThreadTip = new ToolTip();
        openThreadTip.Bind(ContentControl.ContentProperty, new Binding(nameof(RenPyGameViewModel.ThreadUrl)));
        ToolTip.SetTip(openThreadBtn, openThreadTip);

        // --- Cover zentriert (Uniform = kein Crop, komplettes Bild wird
        //     angezeigt; MaxWidth 700, MaxHeight 700 begrenzt sehr breite
        //     Landscape-Cover damit die Seite nicht zu weit wird) ---
        // v0.11: für animierte GIF-Cover ein GifImage-Widget (Avalonia.Labs.
        // Gif), das autoplay-loopt. Statisches Bitmap bleibt Fallback für
        // Nicht-GIF-Cover — der Split via HasAnimatedCover-Binding.
        var coverImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 700,
            MaxHeight = 700,
            Margin = new Thickness(0, 0, 0, 20),
        };
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(RenPyGameViewModel.Cover)));
        coverImage.Bind(Image.IsVisibleProperty, new Binding(nameof(RenPyGameViewModel.HasAnimatedCover))
        { Converter = new Avalonia.Data.Converters.FuncValueConverter<bool, bool>(v => !v) });

        var animatedCover = new Avalonia.Labs.Gif.GifImage
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 700,
            MaxHeight = 700,
            Margin = new Thickness(0, 0, 0, 20),
            IterationCount = Avalonia.Animation.IterationCount.Infinite,
        };
        animatedCover.Bind(Avalonia.Labs.Gif.GifImage.SourceProperty,
            new Binding(nameof(RenPyGameViewModel.AnimatedCoverSource)));
        animatedCover.Bind(Control.IsVisibleProperty,
            new Binding(nameof(RenPyGameViewModel.HasAnimatedCover)));

        var coverFallback = new TextBlock
        {
            Text = "🎮", FontSize = 96,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 40, 0, 40),
        };
        coverFallback.Classes.Add("muted");
        coverFallback.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(RenPyGameViewModel.Cover))
        { Converter = new Avalonia.Data.Converters.FuncValueConverter<Bitmap?, bool>(v => v is null) });

        // --- Beschreibung (KI-übersetzt oder Original) ---
        var descHeader = new TextBlock
        {
            Text = Strings.T("section.description"),
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
            Text = Strings.T("section.genre"),
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
                title, updateBadge, versionInfo, subPath, openThreadBtn,
                coverFallback, coverImage, animatedCover,
                descHeader, descText, translationHint,
                genreHeader, genrePanel,
                noThreadHint,
            },
        };

        Content = new ScrollViewer { Content = stack };
    }
}
