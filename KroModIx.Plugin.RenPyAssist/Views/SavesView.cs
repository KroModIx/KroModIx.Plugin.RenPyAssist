using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Saves-Tab (v0.4+): links Save-Liste, oben Screenshot + Metadata,
/// unten Variable-Editor (Name/Type/Value als Grid mit Inline-TextBox).</summary>
public sealed class SavesView : UserControl
{
    public SavesView()
    {
        // --- Toolbar ---
        var refreshBtn = new Button { Content = Strings.T("btn.refresh_short") };
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(SavesViewModel.ScanCommand)));
        var saveBtn = new Button { Content = Strings.T("btn.save_changes") };
        saveBtn.Classes.Add("accent");
        saveBtn.Bind(Button.CommandProperty, new Binding(nameof(SavesViewModel.SaveEditsCommand)));
        var folderBtn = new Button { Content = Strings.T("btn.open_saves_folder") };
        folderBtn.Classes.Add("ghost");
        folderBtn.Bind(Button.CommandProperty, new Binding(nameof(SavesViewModel.OpenSavesFolderCommand)));
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { refreshBtn, saveBtn, folderBtn },
        };

        var status = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(SavesViewModel.StatusText)));

        // --- Left: Save-Liste ---
        var saves = new ListBox
        {
            Width = 240,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        saves.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(SavesViewModel.Saves)));
        saves.Bind(ListBox.SelectedItemProperty, new Binding(nameof(SavesViewModel.SelectedSave))
        { Mode = BindingMode.TwoWay });
        saves.ItemTemplate = new FuncDataTemplate<SaveRow>((row, _) =>
        {
            if (row is null) return null;
            var name = new TextBlock { FontWeight = FontWeight.SemiBold };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(SaveRow.FileName)));
            var mod = new TextBlock { FontSize = 11 };
            mod.Classes.Add("muted");
            mod.Bind(TextBlock.TextProperty, new Binding(nameof(SaveRow.ModifiedText)));
            return new StackPanel
            {
                Margin = new Thickness(6, 4),
                Children = { name, mod },
            };
        }, true);

        // --- Right-Top: Screenshot + Metadata ---
        var screenshot = new Image
        {
            MaxHeight = 250,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        screenshot.Bind(Image.SourceProperty, new Binding(nameof(SavesViewModel.Screenshot)));
        screenshot.Bind(Image.IsVisibleProperty, new Binding(nameof(SavesViewModel.Screenshot))
        { Converter = Avalonia.Data.Converters.ObjectConverters.IsNotNull });

        var metadata = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        metadata.Classes.Add("muted");
        metadata.Bind(TextBlock.TextProperty, new Binding(nameof(SavesViewModel.MetadataText)));

        var search = new TextBox
        {
            PlaceholderText = Strings.T("placeholder.saves_filter"),
            Margin = new Thickness(0, 8, 0, 4),
        };
        search.Bind(TextBox.TextProperty, new Binding(nameof(SavesViewModel.Search))
        { Mode = BindingMode.TwoWay });

        // --- Variable-Grid ---
        var vars = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        vars.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(SavesViewModel.Variables)));
        vars.ItemTemplate = new FuncDataTemplate<VariableRow>((row, _) =>
        {
            if (row is null) return null;
            var name = new TextBlock
            {
                FontFamily = new FontFamily("monospace"),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            name.Bind(TextBlock.TextProperty, new Binding(nameof(VariableRow.Name)));
            var type = new TextBlock
            {
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            type.Classes.Add("muted");
            type.Bind(TextBlock.TextProperty, new Binding(nameof(VariableRow.TypeName)));
            var editor = new TextBox
            {
                FontFamily = new FontFamily("monospace"),
                FontSize = 11,
            };
            editor.Bind(TextBox.TextProperty, new Binding(nameof(VariableRow.EditedValue))
            { Mode = BindingMode.TwoWay });
            var dirtyDot = new TextBlock
            {
                Text = "●",
                FontSize = 14,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("KrosteGoldBrush"),
            };
            dirtyDot.Bind(TextBlock.IsVisibleProperty, new Binding(nameof(VariableRow.HasUnsavedChanges)));

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("200,80,*,Auto"),
                Margin = new Thickness(4, 2),
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(type, 1);
            Grid.SetColumn(editor, 2);
            Grid.SetColumn(dirtyDot, 3);
            grid.Children.Add(name);
            grid.Children.Add(type);
            grid.Children.Add(editor);
            grid.Children.Add(dirtyDot);
            return grid;
        }, true);

        // v0.11.1: Screenshot-Timeline unten — chronologische Bilderleiste
        // (aelteste links → neuste rechts). Horizontaler ScrollViewer,
        // ItemsControl mit Thumbnails, Klick selektiert den Save.
        var timelineItems = new ItemsControl();
        timelineItems.Bind(ItemsControl.ItemsSourceProperty,
            new Binding(nameof(SavesViewModel.Timeline)));
        timelineItems.ItemsPanel = new FuncTemplate<Panel?>(() =>
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 });
        timelineItems.ItemTemplate = new FuncDataTemplate<TimelineEntry>((entry, _) =>
        {
            if (entry is null) return null;
            var thumb = new Image
            {
                Source = entry.Thumbnail,
                Width = 128, Height = 72,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 2, 0, 2),
            };
            var slot = new TextBlock
            {
                Text = entry.SlotName,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 128,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            slot.Classes.Add("muted");
            var time = new TextBlock
            {
                Text = entry.TimeText,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            time.Classes.Add("muted");
            var stack = new StackPanel
            {
                Children = { thumb, slot, time },
                Margin = new Thickness(4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            var wrapper = new Border
            {
                Padding = new Thickness(4),
                Child = stack,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
            };
            // Klick → SelectFromTimelineCommand mit dem Entry als Parameter.
            wrapper.PointerPressed += (_, _) =>
            {
                if (wrapper.DataContext is TimelineEntry e &&
                    (wrapper.FindAncestorOfType<ItemsControl>()?.DataContext is SavesViewModel vm))
                    vm.SelectFromTimelineCommand.Execute(e);
            };
            return wrapper;
        }, true);

        var timelineScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = timelineItems,
            MaxHeight = 130,
            Margin = new Thickness(0, 8, 0, 0),
        };
        timelineScroll.Bind(ScrollViewer.IsVisibleProperty,
            new Binding(nameof(SavesViewModel.Timeline) + ".Count")
            {
                Converter = new Avalonia.Data.Converters.FuncValueConverter<int, bool>(n => n > 0),
            });

        var right = new DockPanel
        {
            Children =
            {
                WithDock(screenshot, Dock.Top),
                WithDock(metadata, Dock.Top),
                WithDock(search, Dock.Top),
                WithDock(timelineScroll, Dock.Bottom),
                vars,
            },
        };

        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        Grid.SetColumn(saves, 0);
        Grid.SetColumn(right, 1);
        right.Margin = new Thickness(12, 0, 0, 0);
        mainGrid.Children.Add(saves);
        mainGrid.Children.Add(right);

        Content = new DockPanel
        {
            Margin = new Thickness(16, 12),
            Children =
            {
                WithDock(toolbar, Dock.Top),
                WithDock(status, Dock.Top),
                mainGrid,
            },
        };
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}
