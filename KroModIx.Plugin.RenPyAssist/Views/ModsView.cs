using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Mods-Tab (v0.6.0): Mod-Typ als Radio-Buttons, Bauen-Button,
/// Progress-Bar, Uninstall-Button, Manifest-Status.</summary>
public sealed class ModsView : UserControl
{
    public ModsView()
    {
        var title = new TextBlock
        {
            Text = "KrosteMod-Pipeline",
            FontSize = 18, FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var subtitle = new TextBlock
        {
            Text = "Portiert aus RenPack (Kroste-Original). Wählt einen Typ, klick 'Bauen' — " +
                   "das Plugin dekompiliert alle .rpyc, analysiert die Skripte, generiert den " +
                   "Mod und deployt ihn ins game/-Verzeichnis. Original-.rpyc werden als " +
                   ".krostemod-bak gesichert; Uninstall stellt sie wieder her.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };
        subtitle.Classes.Add("secondary");

        // Manifest-Status
        var manifestBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
        };
        var manifestText = new TextBlock { FontSize = 11 };
        manifestText.Bind(TextBlock.TextProperty, new Binding(nameof(ModsViewModel.ManifestInfo)));
        manifestBadge.Child = manifestText;

        // Mod-Typ-Liste
        var typesLabel = new TextBlock { Text = "Mod-Typ wählen", Margin = new Thickness(0, 8, 0, 6) };
        typesLabel.Classes.Add("section-label");
        var typesList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        typesList.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(ModsViewModel.Types)));
        typesList.Bind(ListBox.SelectedItemProperty, new Binding(nameof(ModsViewModel.SelectedType))
        { Mode = BindingMode.TwoWay });
        typesList.ItemTemplate = new FuncDataTemplate<ModTypeOption>((opt, _) =>
        {
            if (opt is null) return null;
            var icon = new TextBlock
            {
                Text = opt.Icon,
                FontSize = 22,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var name = new TextBlock
            {
                Text = opt.DisplayName,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
            };
            var desc = new TextBlock
            {
                Text = opt.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            };
            desc.Classes.Add("secondary");
            var textStack = new StackPanel { Children = { name, desc } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(icon);
            grid.Children.Add(textStack);
            return new Border { Padding = new Thickness(8, 6), Child = grid };
        }, true);

        // Actions
        var buildBtn = new Button { Content = "▶  Bauen", Padding = new Thickness(20, 6) };
        buildBtn.Classes.Add("accent");
        buildBtn.Bind(Button.CommandProperty, new Binding(nameof(ModsViewModel.BuildCommand)));
        buildBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(ModsViewModel.IsBusy))
        { Converter = new FuncValueConverter<bool, bool>(v => !v) });

        var uninstallBtn = new Button { Content = "🗑  Deinstallieren", Padding = new Thickness(14, 6),
            Margin = new Thickness(8, 0, 0, 0) };
        uninstallBtn.Classes.Add("danger");
        uninstallBtn.Bind(Button.CommandProperty, new Binding(nameof(ModsViewModel.UninstallCommand)));
        uninstallBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(ModsViewModel.HasManifest)));

        var actionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 12),
            Children = { buildBtn, uninstallBtn },
        };

        // Progress
        var progressBar = new ProgressBar
        {
            Height = 8, IsIndeterminate = false,
            Margin = new Thickness(0, 6, 0, 4),
        };
        progressBar.Bind(ProgressBar.ValueProperty, new Binding(nameof(ModsViewModel.ProgressCurrent)));
        progressBar.Bind(ProgressBar.MaximumProperty, new Binding(nameof(ModsViewModel.ProgressTotal)));
        progressBar.Bind(ProgressBar.IsVisibleProperty, new Binding(nameof(ModsViewModel.IsBusy)));

        var progressFile = new TextBlock { FontSize = 10 };
        progressFile.Classes.Add("secondary");
        progressFile.Bind(TextBlock.TextProperty, new Binding(nameof(ModsViewModel.ProgressFile)));
        progressFile.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(ModsViewModel.IsBusy)));

        var status = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ModsViewModel.StatusText)));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20),
                MaxWidth = 900,
                Children =
                {
                    title, subtitle, manifestBadge,
                    typesLabel, typesList,
                    actionsRow, progressBar, progressFile, status,
                },
            },
        };
    }
}
