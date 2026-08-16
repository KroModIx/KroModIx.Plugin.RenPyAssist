using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.RenPyAssist.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace KroModIx.Plugin.RenPyAssist.Views;

/// <summary>Dialog um aus einem Original-Cover einen 2:3-Ausschnitt (Steam-
/// Library-Portrait-Aspect) für die Sidebar-Kachel zu wählen.
///
/// <para>Bedienung: das Auswahl-Rechteck ist per Maus verschiebbar
/// (PointerPressed + Drag). Zoom-Slider bestimmt die Größe. „💾 Speichern"
/// schneidet das Original per ImageSharp und speichert als PNG. Callback
/// <see cref="OnCropSaved"/> bekommt den fertigen Pfad.</para></summary>
public sealed class CoverCropDialog : Window
{
    private const double TargetAspectRatio = 2.0 / 3.0; // 600×900 Portrait
    private const int OutputWidth = 600;
    private const int OutputHeight = 900;

    private readonly string _sourceImagePath;
    private readonly string _outputPath;
    private readonly IHostServices _host;
    public Action<string>? OnCropSaved { get; set; }

    private readonly Avalonia.Controls.Image _preview;
    private readonly Avalonia.Controls.Shapes.Rectangle _selection;
    private readonly Slider _zoomSlider;
    private readonly TextBlock _statusText;

    private double _imgW, _imgH; // original image px
    private double _dispW, _dispH; // displayed image px in Canvas
    private double _dispOffsetX, _dispOffsetY;

    // Selection state (in DISPLAYED coordinates)
    private double _selW = 200, _selH = 300;
    private double _selX = 50, _selY = 50;
    private bool _dragging;
    private Avalonia.Point _dragStart;
    private Avalonia.Point _selStartPos;

    public CoverCropDialog(string sourceImagePath, string outputPath, IHostServices host)
    {
        _sourceImagePath = sourceImagePath;
        _outputPath = outputPath;
        _host = host;

        Title = Strings.T("crop.title");
        Width = 900;
        Height = 700;
        MinWidth = 700; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1a, 0x1a, 0x1e));

        _preview = new Avalonia.Controls.Image { Stretch = Stretch.Uniform };
        _selection = new Avalonia.Controls.Shapes.Rectangle
        {
            Stroke = Brushes.Gold,
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x30, 0xff, 0xd7, 0x00)),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            IsHitTestVisible = true,
        };

        var canvas = new Canvas { Background = Brushes.Transparent };
        canvas.Children.Add(_preview);
        canvas.Children.Add(_selection);

        canvas.SizeChanged += (_, _) => LayoutContent(canvas);
        _selection.PointerPressed += OnSelectionPressed;
        _selection.PointerMoved += OnSelectionMoved;
        _selection.PointerReleased += (_, _) => _dragging = false;

        _zoomSlider = new Slider
        {
            Minimum = 100, Maximum = 600, Value = 300,
            Width = 260,
            TickFrequency = 20,
            IsSnapToTickEnabled = false,
        };
        _zoomSlider.ValueChanged += (_, e) =>
        {
            var cx = _selX + _selW / 2;
            var cy = _selY + _selH / 2;
            _selH = e.NewValue;
            _selW = _selH * TargetAspectRatio;
            _selX = cx - _selW / 2;
            _selY = cy - _selH / 2;
            ClampSelection();
            UpdateSelectionCanvas();
            UpdateStatus();
        };

        var saveBtn = new Button { Content = Strings.T("btn.crop_save"), Padding = new Thickness(14, 6) };
        saveBtn.Click += (_, _) => SaveCrop();

        var cancelBtn = new Button { Content = Strings.T("btn.cancel"), Padding = new Thickness(14, 6) };
        cancelBtn.Click += (_, _) => Close();

        _statusText = new TextBlock { Foreground = Brushes.LightGray, FontSize = 11 };

        var topInfo = new TextBlock
        {
            Text = Strings.T("crop.info"),
            Foreground = Brushes.LightGray,
            Margin = new Thickness(10, 10, 10, 10),
            TextWrapping = TextWrapping.Wrap,
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(10),
        };
        var zoomStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        zoomStack.Children.Add(new TextBlock { Text = Strings.T("crop.zoom_label"), Foreground = Brushes.LightGray,
            VerticalAlignment = VerticalAlignment.Center });
        zoomStack.Children.Add(_zoomSlider);
        Grid.SetColumn(zoomStack, 0);
        Grid.SetColumn(_statusText, 1);
        _statusText.Margin = new Thickness(14, 0);
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(cancelBtn, 2);
        cancelBtn.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(saveBtn, 3);
        footer.Children.Add(zoomStack);
        footer.Children.Add(_statusText);
        footer.Children.Add(cancelBtn);
        footer.Children.Add(saveBtn);

        var root = new DockPanel();
        DockPanel.SetDock(topInfo, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(topInfo);
        root.Children.Add(footer);
        root.Children.Add(canvas);
        Content = root;

        LoadImage();
    }

    private async void LoadImage()
    {
        try
        {
            // v0.15.0: zentraler Host-Baukasten IHostServices.Images
            // (Contracts v1.18+) — deckt auch WebP/AVIF/DDS ab, falls das
            // Cover in einem exotischen Format vorliegt.
            var bmp = await _host.Images.DecodeFileAsync(_sourceImagePath);
            if (bmp is null) throw new InvalidOperationException("decode failed");
            _preview.Source = bmp;
            _imgW = bmp.PixelSize.Width;
            _imgH = bmp.PixelSize.Height;
        }
        catch (Exception ex)
        {
            _statusText.Text = string.Format(Strings.T("crop.status_load_fail"), ex.Message);
        }
    }

    private void LayoutContent(Canvas canvas)
    {
        if (_imgW == 0 || _imgH == 0) return;
        var cw = canvas.Bounds.Width;
        var ch = canvas.Bounds.Height;
        // Fit image into canvas (Uniform)
        var scale = Math.Min(cw / _imgW, ch / _imgH);
        _dispW = _imgW * scale;
        _dispH = _imgH * scale;
        _dispOffsetX = (cw - _dispW) / 2;
        _dispOffsetY = (ch - _dispH) / 2;
        _preview.Width = _dispW;
        _preview.Height = _dispH;
        Canvas.SetLeft(_preview, _dispOffsetX);
        Canvas.SetTop(_preview, _dispOffsetY);

        // Init selection: centered, half image height
        _selH = _dispH * 0.7;
        _selW = _selH * TargetAspectRatio;
        _selX = _dispOffsetX + (_dispW - _selW) / 2;
        _selY = _dispOffsetY + (_dispH - _selH) / 2;
        _zoomSlider.Minimum = _dispH * 0.2;
        _zoomSlider.Maximum = _dispH;
        _zoomSlider.Value = _selH;
        UpdateSelectionCanvas();
        UpdateStatus();
    }

    private void UpdateSelectionCanvas()
    {
        _selection.Width = _selW;
        _selection.Height = _selH;
        Canvas.SetLeft(_selection, _selX);
        Canvas.SetTop(_selection, _selY);
    }

    private void ClampSelection()
    {
        if (_selX < _dispOffsetX) _selX = _dispOffsetX;
        if (_selY < _dispOffsetY) _selY = _dispOffsetY;
        if (_selX + _selW > _dispOffsetX + _dispW) _selX = _dispOffsetX + _dispW - _selW;
        if (_selY + _selH > _dispOffsetY + _dispH) _selY = _dispOffsetY + _dispH - _selH;
    }

    private void OnSelectionPressed(object? _, PointerPressedEventArgs e)
    {
        _dragging = true;
        _dragStart = e.GetPosition((Visual)_selection.Parent!);
        _selStartPos = new Avalonia.Point(_selX, _selY);
        e.Pointer.Capture(_selection);
    }

    private void OnSelectionMoved(object? _, PointerEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition((Visual)_selection.Parent!);
        _selX = _selStartPos.X + (pos.X - _dragStart.X);
        _selY = _selStartPos.Y + (pos.Y - _dragStart.Y);
        ClampSelection();
        UpdateSelectionCanvas();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_dispW == 0) return;
        var scale = _imgW / _dispW;
        int cropX = (int)((_selX - _dispOffsetX) * scale);
        int cropY = (int)((_selY - _dispOffsetY) * scale);
        int cropW = (int)(_selW * scale);
        int cropH = (int)(_selH * scale);
        _statusText.Text = string.Format(Strings.T("crop.status_summary"),
            cropW, cropH, cropX, cropY, (int)_imgW, (int)_imgH);
    }

    private void SaveCrop()
    {
        try
        {
            var scale = _imgW / _dispW;
            int cropX = Math.Max(0, (int)((_selX - _dispOffsetX) * scale));
            int cropY = Math.Max(0, (int)((_selY - _dispOffsetY) * scale));
            int cropW = Math.Min((int)_imgW - cropX, (int)(_selW * scale));
            int cropH = Math.Min((int)_imgH - cropY, (int)(_selH * scale));

            using var img = SixLabors.ImageSharp.Image.Load(_sourceImagePath);
            img.Mutate(x => x
                .Crop(new SixLabors.ImageSharp.Rectangle(cropX, cropY, cropW, cropH))
                .Resize(OutputWidth, OutputHeight));

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_outputPath)!);
            using (var fs = File.Create(_outputPath))
                img.Save(fs, new PngEncoder());

            OnCropSaved?.Invoke(_outputPath);
            Close();
        }
        catch (Exception ex)
        {
            _statusText.Text = string.Format(Strings.T("crop.status_save_fail"), ex.Message);
        }
    }
}
