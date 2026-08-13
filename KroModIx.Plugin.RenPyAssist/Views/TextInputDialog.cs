using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using KroModIx.Plugin.RenPyAssist.Services;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Kompaktes Text-Input-Modal für Rename-Aktionen. Kroste-Look via
/// Host-Styles (Border.card, Button.accent/ghost, TextBlock.h2/.muted).
/// Der Dialog wird als Owner-Modal zum MainWindow geöffnet und liefert per
/// <see cref="PromptAsync"/> den eingegebenen Text zurück (null bei Abbruch,
/// leerer String zählt als Abbruch).</summary>
public sealed class TextInputDialog : Window
{
    private readonly TextBox _input;
    private string? _result;

    public TextInputDialog(string title, string message, string initialValue = "",
        string? acceptLabel = null, string? cancelLabel = null)
    {
        acceptLabel ??= Strings.T("btn.ok");
        cancelLabel ??= Strings.T("btn.cancel");
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        WindowDecorations = WindowDecorations.BorderOnly;

        var header = new TextBlock { Text = title };
        header.Classes.Add("h2");

        var msg = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
        };
        msg.Classes.Add("muted");

        _input = new TextBox
        {
            Text = initialValue,
            AcceptsReturn = false,
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };

        var okBtn = new Button { Content = acceptLabel, IsDefault = true };
        okBtn.Classes.Add("accent");
        okBtn.Click += (_, _) => Accept();

        var cancelBtn = new Button { Content = cancelLabel, IsCancel = true };
        cancelBtn.Classes.Add("ghost");
        cancelBtn.Click += (_, _) => Cancel();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { cancelBtn, okBtn },
        };

        var card = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel { Children = { header, msg, _input, buttons } },
        };
        card.Classes.Add("card");
        Content = card;

        Opened += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void Accept()
    {
        _result = _input.Text ?? "";
        Close();
    }

    private void Cancel()
    {
        _result = null;
        Close();
    }

    /// <summary>Öffnet den Dialog modal am MainWindow und liefert den Text
    /// zurück, oder null bei Abbruch (Escape/Cancel/leerer String).</summary>
    public static async Task<string?> PromptAsync(string title, string message,
        string initialValue = "", string? acceptLabel = null, string? cancelLabel = null)
    {
        var dialog = new TextInputDialog(title, message, initialValue, acceptLabel, cancelLabel);
        var owner = Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desk
            ? desk.MainWindow : null;
        if (owner is null) { dialog.Show(); return null; }
        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(dialog._result) ? null : dialog._result!.Trim();
    }
}
