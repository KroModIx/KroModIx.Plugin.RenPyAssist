using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KroModIx.Plugin.RenPyAssist.Services;
using KroModIx.Plugin.RenPyAssist.Services.Modding;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Modal-Dialog für den Rename-Mod: zeigt eine Liste aller im Spiel
/// erkannten Characters mit ihrem Original-Display-Namen und einer editierbaren
/// „Neuer Name"-Spalte. Nach „✓ Übernehmen" wird ein <see cref="RenameConfig"/>
/// mit den geänderten Mappings zurückgegeben; „Abbrechen" liefert null.</summary>
public sealed class RenameConfigDialog : Window
{
    public RenameConfig? Result { get; private set; }

    public RenameConfigDialog(IReadOnlyList<RpyCharacter> characters)
    {
        Title = Strings.T("rename.title");
        Width = 700; Height = 500;
        MinWidth = 500; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1a, 0x1a, 0x1e));

        var vm = new Vm(characters);

        var header = new TextBlock
        {
            Text = string.Format(Strings.T("rename.header"), characters.Count),
            FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.WhiteSmoke,
            Margin = new Thickness(16, 12, 16, 4),
        };
        var help = new TextBlock
        {
            Text = Strings.T("rename.help"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(16, 0, 16, 12),
            FontSize = 11,
        };

        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0),
        };
        list.ItemsSource = vm.Rows;
        list.ItemTemplate = new FuncDataTemplate<RenameRow>((row, _) =>
        {
            if (row is null) return null;
            var varName = new TextBlock
            {
                Text = row.VarName, FontFamily = new FontFamily("monospace"),
                FontSize = 11, Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var oldName = new TextBlock
            {
                Text = row.OldName, FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.WhiteSmoke,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var arrow = new TextBlock
            {
                Text = "→", Foreground = Brushes.Gold, FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var newBox = new TextBox
            {
                PlaceholderText = Strings.T("placeholder.new_name"),
                FontSize = 12,
            };
            newBox.Bind(TextBox.TextProperty, new Binding(nameof(RenameRow.NewName))
            { Mode = BindingMode.TwoWay });

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("120,180,40,*"),
                Margin = new Thickness(4, 4),
            };
            Grid.SetColumn(varName, 0);
            Grid.SetColumn(oldName, 1);
            Grid.SetColumn(arrow, 2);
            Grid.SetColumn(newBox, 3);
            grid.Children.Add(varName);
            grid.Children.Add(oldName);
            grid.Children.Add(arrow);
            grid.Children.Add(newBox);
            return grid;
        }, true);

        var okBtn = new Button
        {
            Content = Strings.T("btn.apply"),
            Padding = new Thickness(20, 6),
        };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) =>
        {
            var mappings = vm.Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.NewName)
                         && r.NewName!.Trim() != r.OldName)
                .ToDictionary(r => r.VarName, r => r.NewName!.Trim());
            Result = new RenameConfig(mappings);
            Close();
        };
        var cancelBtn = new Button { Content = Strings.T("btn.cancel"), Padding = new Thickness(14, 6),
            Margin = new Thickness(8, 0, 0, 0) };
        cancelBtn.Click += (_, _) => { Result = null; Close(); };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12),
            Children = { cancelBtn, okBtn },
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(help, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(help);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = list });
        Content = root;
    }

    private sealed class Vm
    {
        public ObservableCollection<RenameRow> Rows { get; }
        public Vm(IReadOnlyList<RpyCharacter> chars) =>
            Rows = new ObservableCollection<RenameRow>(
                chars.Select(c => new RenameRow(c.VarName, c.DisplayName)));
    }
}

public sealed partial class RenameRow : ObservableObject
{
    public string VarName { get; }
    public string OldName { get; }
    [ObservableProperty] private string? _newName;
    public RenameRow(string varName, string oldName)
    { VarName = varName; OldName = oldName; }
}
