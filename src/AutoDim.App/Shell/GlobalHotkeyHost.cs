using System.Windows.Forms;
using System.Windows.Interop;
using AutoDim.App.Interop;

namespace AutoDim.App.Shell;

public sealed class GlobalHotkeyHost : IDisposable
{
    private const int HotkeyId = 0x4144; // "AD"
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _messageWindow;
    private (uint Modifiers, uint VirtualKey)? _activeHotkey;
    private bool _registered;
    private bool _disposed;

    public event Action? HotkeyPressed;

    public GlobalHotkeyHost()
    {
        var parameters = new HwndSourceParameters("AutoDimHotkeyHost")
        {
            ParentWindow = new nint(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0
        };
        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    public bool TryRegister(string hotkeyText)
    {
        if (_disposed) return false;

        if (!TryParseHotkey(hotkeyText, out uint modifiers, out uint vk))
        {
            return false;
        }

        var previous = _activeHotkey;
        Unregister();

        if (NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers | MOD_NOREPEAT, vk))
        {
            _registered = true;
            _activeHotkey = (modifiers, vk);
            return true;
        }

        if (previous is { } prev &&
            NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, prev.Modifiers | MOD_NOREPEAT, prev.VirtualKey))
        {
            _registered = true;
            _activeHotkey = prev;
        }

        return false;
    }

    public void Unregister()
    {
        if (_registered && !_disposed)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HotkeyId);
            _registered = false;
            _activeHotkey = null;
        }
    }

    public static bool TryParseHotkey(string hotkeyText, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(hotkeyText)) return false;

        string[] parts = hotkeyText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (string part in parts[..^1])
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= MOD_ALT;
                    break;
                case "SHIFT":
                    modifiers |= MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        string keyPart = parts[^1];
        if (keyPart.Length == 1 && char.IsAsciiLetterOrDigit(keyPart[0]))
        {
            virtualKey = char.ToUpperInvariant(keyPart[0]);
            return modifiers != 0;
        }

        if (!int.TryParse(keyPart, out _) &&
            Enum.TryParse<Keys>(keyPart, ignoreCase: true, out var parsedKey) &&
            Enum.IsDefined(parsedKey) &&
            parsedKey is > Keys.None and < Keys.KeyCode)
        {
            virtualKey = (uint)parsedKey;
            return modifiers != 0;
        }

        return false;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _disposed = true;
        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }
}
