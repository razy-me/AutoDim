using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using static Vortice.Direct2D1.D2D1;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DirectComposition.DComp;

namespace AutoDim.App.Overlay;

/// <summary>
/// Shared D3D11 / DXGI / Direct2D1 / DirectComposition device resources across all monitor overlays.
/// </summary>
public sealed class GpuOverlayDevice : IDisposable
{
    public ID3D11Device D3DDevice { get; private set; } = null!;
    public IDXGIDevice DxgiDevice { get; private set; } = null!;
    public IDXGIFactory2 DxgiFactory { get; private set; } = null!;
    public ID2D1Factory1 D2DFactory { get; private set; } = null!;
    public ID2D1Device D2DDevice { get; private set; } = null!;
    public ID2D1DeviceContext D2DContext { get; private set; } = null!;
    public IDCompositionDevice DCompDevice { get; private set; } = null!;

    private bool _disposed;

    public GpuOverlayDevice()
    {
        Initialize();
    }

    public void Recreate()
    {
        ReleaseResources();
        Initialize();
    }

    private void Initialize()
    {
        ID3D11Device? d3d = null;
        try
        {
            d3d = D3D11CreateDevice(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport);
        }
        catch
        {
            d3d = D3D11CreateDevice(
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport);
        }

        D3DDevice = d3d!;
        DxgiDevice = D3DDevice.QueryInterface<IDXGIDevice>();
        using var adapter = DxgiDevice.GetAdapter();
        DxgiFactory = adapter.GetParent<IDXGIFactory2>();

        D2DFactory = D2D1CreateFactory<ID2D1Factory1>();
        D2DDevice = D2DFactory.CreateDevice(DxgiDevice);
        D2DContext = D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
        DCompDevice = DCompositionCreateDevice<IDCompositionDevice>(DxgiDevice);
    }

    private void ReleaseResources()
    {
        DCompDevice?.Dispose();
        DCompDevice = null!;
        D2DContext?.Dispose();
        D2DContext = null!;
        D2DDevice?.Dispose();
        D2DDevice = null!;
        D2DFactory?.Dispose();
        D2DFactory = null!;
        DxgiFactory?.Dispose();
        DxgiFactory = null!;
        DxgiDevice?.Dispose();
        DxgiDevice = null!;
        D3DDevice?.Dispose();
        D3DDevice = null!;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseResources();
    }
}
