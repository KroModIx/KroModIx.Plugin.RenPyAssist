using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using KroModIx.Plugin.RenPyAssist.Services.Modding;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Setup-Dialog für den Translate-Mod: Sprach-Auswahl + Info über
/// die zu übersetzenden Say-Statements. „Los" bestätigt, „Abbrechen" liefert null.
/// Die eigentliche KI-Batch-Übersetzung läuft danach in <see cref="ModsViewModel"/>
/// mit Progress-Anzeige.</summary>
public sealed class TranslateSetupDialog : Window
{
    public TargetLanguage? SelectedLanguage { get; private set; }

    public TranslateSetupDialog(int sayCount, int uniqueTexts)
    {
        Title = "Übersetzung einrichten";
        Width = 480; Height = 380;
        MinWidth = 400; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1a, 0x1a, 0x1e));

        var header = new TextBlock
        {
            Text = $"🌐  Ren'Py-Spiel übersetzen",
            FontSize = 18, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.WhiteSmoke,
            Margin = new Thickness(16, 12, 16, 8),
        };
        var stats = new TextBlock
        {
            Text = $"{sayCount} Dialog-Zeilen im Spiel · {uniqueTexts} eindeutige Texte " +
                   $"(nach Dedup) → wird via KI-Batch übersetzt (30 Texte/Batch).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            FontSize = 12,
            Margin = new Thickness(16, 0, 16, 8),
        };

        var timeEstimate = new TextBlock
        {
            Text = "Zeit-Schätzung: Ollama ~5-10 s/Batch, Cloud ~2-3 s/Batch. " +
                   "Bei 500 Says ≈ 20 Batches → 1-3 min.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            Margin = new Thickness(16, 0, 16, 12),
        };

        var langLabel = new TextBlock
        {
            Text = "Zielsprache",
            FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(16, 4, 16, 4),
        };

        var langBox = new ComboBox
        {
            Margin = new Thickness(16, 0, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var options = System.Enum.GetValues<TargetLanguage>()
            .Where(l => l != TargetLanguage.English) // nichts nach EN übersetzen
            .Select(l => new LangOption(l, $"{l.ToFlagEmoji()}  {l.ToNativeName()}"))
            .ToList();
        langBox.ItemsSource = options;
        langBox.ItemTemplate = new FuncDataTemplate<LangOption>((o, _) =>
        {
            if (o is null) return null;
            return new TextBlock { Text = o.Label, FontSize = 13, Padding = new Thickness(6, 3) };
        }, true);
        // Vorauswahl aus System-Locale
        var sysLocale = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        var preselect = sysLocale switch
        {
            "de" => TargetLanguage.German, "fr" => TargetLanguage.French, "es" => TargetLanguage.Spanish,
            "it" => TargetLanguage.Italian, "pt" => TargetLanguage.Portuguese, "pl" => TargetLanguage.Polish,
            "ru" => TargetLanguage.Russian, "cs" => TargetLanguage.Czech,
            "ja" => TargetLanguage.Japanese, "ko" => TargetLanguage.Korean,
            _ => TargetLanguage.German,
        };
        langBox.SelectedItem = options.FirstOrDefault(o => o.Value == preselect);

        var okBtn = new Button { Content = "▶  Los", Padding = new Thickness(20, 6) };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) =>
        {
            SelectedLanguage = (langBox.SelectedItem as LangOption)?.Value;
            Close();
        };
        var cancelBtn = new Button { Content = "Abbrechen", Padding = new Thickness(14, 6),
            Margin = new Thickness(8, 0, 0, 0) };
        cancelBtn.Click += (_, _) => { SelectedLanguage = null; Close(); };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 16),
            Children = { cancelBtn, okBtn },
        };

        Content = new DockPanel
        {
            Children =
            {
                WithDock(header, Dock.Top),
                WithDock(stats, Dock.Top),
                WithDock(timeEstimate, Dock.Top),
                WithDock(langLabel, Dock.Top),
                WithDock(langBox, Dock.Top),
                WithDock(footer, Dock.Bottom),
                new TextBlock(), // filler
            },
        };
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    private sealed record LangOption(TargetLanguage Value, string Label);
}
