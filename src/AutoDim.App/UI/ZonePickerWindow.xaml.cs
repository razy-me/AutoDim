using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AutoDim.App.Interop;
using AutoDim.Core.Config;
using AutoDim.Core.Engine;

namespace AutoDim.App.UI;

public partial class ZonePickerWindow : Window
{
    private const double SnapMarginDip = 10.0;

    private readonly MonitorSnapshot _monitor;
    private readonly Action<ZoneDefinition?> _onCompleted;
    private System.Windows.Point _startDip;
    private Rect _currentRectDip;
    private bool _isDragging;
    private bool _awaitingConfirm;

    public ZonePickerWindow(MonitorSnapshot monitor, int defaultIndex, Action<ZoneDefinition?> onCompleted)
    {
        _monitor = monitor;
        _onCompleted = onCompleted;
        InitializeComponent();

        ZoneNameBox.Text = $"Zone {defaultIndex}";
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        // Move to the target monitor first so any WM_DPICHANGED completes before sizing in physical pixels.
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _monitor.Bounds.Left,
            _monitor.Bounds.Top,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _monitor.Bounds.Left,
            _monitor.Bounds.Top,
            Math.Max(1, _monitor.Bounds.Width),
            Math.Max(1, _monitor.Bounds.Height),
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _onCompleted(null);
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        _onCompleted(null);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_awaitingConfirm) return;
        _isDragging = true;
        _startDip = SnapToEdges(e.GetPosition(RootCanvas));
        SelectionRect.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        CaptureMouse();
        UpdateSelectionVisual(_startDip);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;
        System.Windows.Point current = SnapToEdges(e.GetPosition(RootCanvas));
        UpdateSelectionVisual(current);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        System.Windows.Point current = SnapToEdges(e.GetPosition(RootCanvas));
        UpdateSelectionVisual(current);

        if (_currentRectDip.Width < 16 || _currentRectDip.Height < 16)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        _awaitingConfirm = true;
        Cursor = System.Windows.Input.Cursors.Arrow;
        double panelLeft = Math.Clamp(_currentRectDip.Left + 8, 12, Math.Max(12, RootCanvas.ActualWidth - 285));
        double panelTop = Math.Clamp(_currentRectDip.Top + 8, 12, Math.Max(12, RootCanvas.ActualHeight - 195));
        Canvas.SetLeft(ConfirmPanel, panelLeft);
        Canvas.SetTop(ConfirmPanel, panelTop);
        ConfirmPanel.Visibility = Visibility.Visible;
        ZoneNameBox.Focus();
        ZoneNameBox.SelectAll();
    }

    private void OnSaveSelectionClick(object sender, RoutedEventArgs e)
    {
        double w = Math.Max(1, RootCanvas.ActualWidth);
        double h = Math.Max(1, RootCanvas.ActualHeight);

        var norm = new NormalizedRect(
            _currentRectDip.Left / w,
            _currentRectDip.Top / h,
            _currentRectDip.Width / w,
            _currentRectDip.Height / h).Normalized();

        var zone = new ZoneDefinition(
            Guid.NewGuid(),
            ZoneNameBox.Text,
            _monitor.DeviceId,
            norm,
            ProcessNameFilter: string.IsNullOrWhiteSpace(ProcessFilterBox.Text) ? null : ProcessFilterBox.Text.Trim(),
            OpacityOverride: null,
            Enabled: true).Validated();

        _onCompleted(zone);
    }

    private void OnCancelSelectionClick(object sender, RoutedEventArgs e)
    {
        _onCompleted(null);
    }

    private System.Windows.Point SnapToEdges(System.Windows.Point pt)
    {
        double maxW = RootCanvas.ActualWidth;
        double maxH = RootCanvas.ActualHeight;
        double x = pt.X <= SnapMarginDip ? 0 : (pt.X >= maxW - SnapMarginDip ? maxW : Math.Clamp(pt.X, 0, maxW));
        double y = pt.Y <= SnapMarginDip ? 0 : (pt.Y >= maxH - SnapMarginDip ? maxH : Math.Clamp(pt.Y, 0, maxH));
        return new System.Windows.Point(x, y);
    }

    private void UpdateSelectionVisual(System.Windows.Point endDip)
    {
        double left = Math.Min(_startDip.X, endDip.X);
        double top = Math.Min(_startDip.Y, endDip.Y);
        double width = Math.Abs(endDip.X - _startDip.X);
        double height = Math.Abs(endDip.Y - _startDip.Y);
        _currentRectDip = new Rect(left, top, width, height);

        Canvas.SetLeft(SelectionRect, left);
        Canvas.SetTop(SelectionRect, top);
        SelectionRect.Width = width;
        SelectionRect.Height = height;

        var dpi = VisualTreeHelper.GetDpi(this);
        int pxW = (int)Math.Round(width * dpi.DpiScaleX);
        int pxH = (int)Math.Round(height * dpi.DpiScaleY);
        SizeBadgeText.Text = $"{pxW} × {pxH} px";

        double badgeTop = top >= 30 ? top - 28 : top + height + 6;
        Canvas.SetLeft(SizeBadge, left);
        Canvas.SetTop(SizeBadge, Math.Min(badgeTop, Math.Max(0, RootCanvas.ActualHeight - 28)));
    }
}
