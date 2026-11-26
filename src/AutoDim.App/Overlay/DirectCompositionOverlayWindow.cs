using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using AutoDim.App.Interop;
using AutoDim.Core.Engine;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace AutoDim.App.Overlay;

/// <summary>
/// Per-monitor topmost click-through DirectComposition window rendering disjoint dim rectangles.
/// </summary>
public sealed class DirectCompositionOverlayWindow : IDisposable
{
    private readonly GpuOverlayDevice _gpu;
    private readonly nint _hwnd;

    private IDCompositionTarget _dcompTarget = null!;
    private IDCompositionVisual _dcompVisual = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID2D1Bitmap1? _targetBitmap;
    private ID2D1SolidColorBrush? _brush;

    private IntRect _bounds;
    private bool _excludeFromCapture;
    private bool _disposed;

    private List<DimDrawRect> _lastRects = [];
    private List<DimCornerSpandrel> _lastCorners = [];
    private readonly Dictionary<(CornerKind Kind, float Radius), ID2D1PathGeometry> _cornerGeoCache = new();
    private uint _lastColorRgb = uint.MaxValue;
    private bool _hasRenderedOnce;

    public nint Handle => _hwnd;

    public DirectCompositionOverlayWindow(GpuOverlayDevice gpu, IntRect monitorBounds, bool excludeFromCapture)
    {
        _gpu = gpu;
        _bounds = monitorBounds;
        _excludeFromCapture = excludeFromCapture;

        uint exStyle =
            NativeMethods.WS_EX_NOREDIRECTIONBITMAP |
            NativeMethods.WS_EX_LAYERED |
            NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_TOPMOST |
            NativeMethods.WS_EX_TOOLWINDOW |
            NativeMethods.WS_EX_NOACTIVATE;

        _hwnd = NativeMethods.CreateWindowExW(
            exStyle,
            "Static",
            $"AutoDimOverlay_{monitorBounds.Left}_{monitorBounds.Top}",
            NativeMethods.WS_POPUP,
            monitorBounds.Left,
            monitorBounds.Top,
            Math.Max(1, monitorBounds.Width),
            Math.Max(1, monitorBounds.Height),
            0,
            0,
            0,
            0);

        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create native overlay window.");
        }

        try
        {
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);

            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                monitorBounds.Left,
                monitorBounds.Top,
                Math.Max(1, monitorBounds.Width),
                Math.Max(1, monitorBounds.Height),
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

            ApplyCaptureExclusion(excludeFromCapture);
            CreateCompositionResources();
        }
        catch
        {
            ReleaseCompositionResources();
            NativeMethods.DestroyWindow(_hwnd);
            throw;
        }
    }

    public void Resize(IntRect monitorBounds)
    {
        if (_disposed || _bounds == monitorBounds) return;
        _bounds = monitorBounds;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            monitorBounds.Left,
            monitorBounds.Top,
            Math.Max(1, monitorBounds.Width),
            Math.Max(1, monitorBounds.Height),
            NativeMethods.SWP_NOACTIVATE);

        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _gpu.D2DContext.Target = null;

        _swapChain.ResizeBuffers(
            2,
            (uint)Math.Max(1, monitorBounds.Width),
            (uint)Math.Max(1, monitorBounds.Height),
            Format.B8G8R8A8_UNorm,
            SwapChainFlags.None);

        BindSwapChainTarget();
        _hasRenderedOnce = false;
    }

    public void EnsureTopmost(nint insertAfterHwnd = 0)
    {
        if (_disposed) return;
        nint target = insertAfterHwnd != 0 ? insertAfterHwnd : NativeMethods.HWND_TOPMOST;
        if (!NativeMethods.SetWindowPos(
            _hwnd,
            target,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE) && insertAfterHwnd != 0)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    public void ReleaseDeviceResources()
    {
        if (_disposed) return;
        ReleaseCompositionResources();
        _hasRenderedOnce = false;
    }

    public void RecreateDeviceResources()
    {
        if (_disposed) return;
        ReleaseCompositionResources();
        CreateCompositionResources();
        _hasRenderedOnce = false;
    }

    public bool Render(MonitorDrawPlan plan, uint rgbColor, bool excludeFromCapture)
    {
        if (_disposed) return true;

        if (_excludeFromCapture != excludeFromCapture)
        {
            _excludeFromCapture = excludeFromCapture;
            ApplyCaptureExclusion(excludeFromCapture);
        }

        if (_hasRenderedOnce &&
            _lastColorRgb == rgbColor &&
            SequenceEquals(_lastRects, plan.Rects) &&
            SequenceEquals(_lastCorners, plan.Corners))
        {
            return true;
        }

        if (_targetBitmap is null)
        {
            BindSwapChainTarget();
        }

        var ctx = _gpu.D2DContext;
        ctx.Target = _targetBitmap;
        _brush ??= ctx.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 1f));

        float r = ((rgbColor >> 16) & 0xFF) / 255f;
        float g = ((rgbColor >> 8) & 0xFF) / 255f;
        float b = (rgbColor & 0xFF) / 255f;

        ctx.BeginDraw();
        ctx.Clear(new Color4(0f, 0f, 0f, 0f));

        foreach (var drawRect in plan.Rects)
        {
            if (drawRect.Alpha <= 0.0005f) continue;

            _brush.Color = new Color4(r, g, b, Math.Clamp(drawRect.Alpha, 0f, 0.99f));
            float left = drawRect.Bounds.Left - _bounds.Left;
            float top = drawRect.Bounds.Top - _bounds.Top;
            float right = drawRect.Bounds.Right - _bounds.Left;
            float bottom = drawRect.Bounds.Bottom - _bounds.Top;
            ctx.FillRectangle(new Vortice.RawRectF(left, top, right, bottom), _brush);
        }

        if (plan.Corners.Count > 0)
        {
            for (int i = 0; i < plan.Corners.Count; i++)
            {
                var corner = plan.Corners[i];
                if (corner.Alpha <= 0.0005f) continue;
                _brush.Color = new Color4(r, g, b, Math.Clamp(corner.Alpha, 0f, 0.99f));
                var geo = GetOrCreateCornerGeometry(corner.Kind, corner.Radius);
                ctx.Transform = Matrix3x2.CreateTranslation(corner.CornerBounds.Left - _bounds.Left, corner.CornerBounds.Top - _bounds.Top);
                ctx.FillGeometry(geo, _brush);
            }
            ctx.Transform = Matrix3x2.Identity;
        }

        var endResult = ctx.EndDraw();
        ctx.Target = null;
        if (endResult.Failure)
        {
            return false;
        }

        var presentResult = _swapChain.Present(0, PresentFlags.None);
        if (presentResult.Failure)
        {
            return false;
        }

        _gpu.DCompDevice.Commit();

        _lastColorRgb = rgbColor;
        _lastRects.Clear();
        _lastRects.AddRange(plan.Rects);
        _lastCorners.Clear();
        _lastCorners.AddRange(plan.Corners);
        _hasRenderedOnce = true;
        return true;
    }

    private ID2D1PathGeometry GetOrCreateCornerGeometry(CornerKind kind, float rad)
    {
        var key = (kind, rad);
        if (_cornerGeoCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var arcSize = new Vortice.Mathematics.Size(rad, rad);
        var geo = _gpu.D2DFactory.CreatePathGeometry();
        using var sink = geo.Open();
        switch (kind)
        {
            case CornerKind.TopLeft:
                sink.BeginFigure(new Vector2(0f, 0f), FigureBegin.Filled);
                sink.AddLine(new Vector2(rad, 0f));
                sink.AddArc(new ArcSegment(new Vector2(0f, rad), arcSize, 0f, SweepDirection.CounterClockwise, ArcSize.Small));
                break;
            case CornerKind.TopRight:
                sink.BeginFigure(new Vector2(rad, 0f), FigureBegin.Filled);
                sink.AddLine(new Vector2(rad, rad));
                sink.AddArc(new ArcSegment(new Vector2(0f, 0f), arcSize, 0f, SweepDirection.CounterClockwise, ArcSize.Small));
                break;
            case CornerKind.BottomRight:
                sink.BeginFigure(new Vector2(rad, rad), FigureBegin.Filled);
                sink.AddLine(new Vector2(0f, rad));
                sink.AddArc(new ArcSegment(new Vector2(rad, 0f), arcSize, 0f, SweepDirection.CounterClockwise, ArcSize.Small));
                break;
            case CornerKind.BottomLeft:
                sink.BeginFigure(new Vector2(0f, rad), FigureBegin.Filled);
                sink.AddLine(new Vector2(0f, 0f));
                sink.AddArc(new ArcSegment(new Vector2(rad, rad), arcSize, 0f, SweepDirection.CounterClockwise, ArcSize.Small));
                break;
        }
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        _cornerGeoCache[key] = geo;
        return geo;
    }

    private void CreateCompositionResources()
    {
        var desc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, _bounds.Width),
            Height = (uint)Math.Max(1, _bounds.Height),
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Premultiplied
        };

        _swapChain = _gpu.DxgiFactory.CreateSwapChainForComposition(
            _gpu.D3DDevice,
            desc);

        _gpu.DCompDevice.CreateTargetForHwnd(_hwnd, true, out _dcompTarget);
        _dcompVisual = _gpu.DCompDevice.CreateVisual();
        _dcompVisual.SetContent(_swapChain);
        _dcompTarget.SetRoot(_dcompVisual);
        _gpu.DCompDevice.Commit();

        BindSwapChainTarget();
    }

    private void BindSwapChainTarget()
    {
        using var surface = _swapChain.GetBuffer<IDXGISurface>(0);
        var props = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);

        _targetBitmap?.Dispose();
        _targetBitmap = _gpu.D2DContext.CreateBitmapFromDxgiSurface(surface, props);
    }

    private void ApplyCaptureExclusion(bool exclude)
    {
        NativeMethods.SetWindowDisplayAffinity(
            _hwnd,
            exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
    }

    private static bool SequenceEquals<T>(List<T> a, IReadOnlyList<T> b) where T : IEquatable<T>
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false;
        }
        return true;
    }

    private void ReleaseCompositionResources()
    {
        foreach (var geo in _cornerGeoCache.Values)
        {
            geo.Dispose();
        }
        _cornerGeoCache.Clear();
        _brush?.Dispose();
        _brush = null;
        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _dcompVisual?.Dispose();
        _dcompVisual = null!;
        _dcompTarget?.Dispose();
        _dcompTarget = null!;
        _swapChain?.Dispose();
        _swapChain = null!;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseCompositionResources();
        NativeMethods.DestroyWindow(_hwnd);
    }
}
