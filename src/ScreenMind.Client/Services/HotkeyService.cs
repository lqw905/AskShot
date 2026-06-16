using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenMind.Client.Services;

/// <summary>
/// Global hotkey registration using RegisterHotKey Win32 API.
/// Attaches to a WPF Window's message pump via HwndSource.
/// </summary>
public class HotkeyService : IDisposable
{
    private HwndSource? _hwndSource;
    private readonly Dictionary<int, (uint Modifiers, uint Key, Action Callback)> _hotkeys = new();
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>
    /// Attach to a WPF window to receive window messages.
    /// </summary>
    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var handle = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource!.AddHook(WndProc);
    }

    /// <summary>
    /// Parse a hotkey string like "Ctrl+Shift+A" and register it.
    /// </summary>
    public int Register(string hotkey, Action callback)
    {
        var (modifiers, key) = ParseHotkey(hotkey);
        var id = _nextId++;

        _hotkeys[id] = (modifiers, key, callback);
        NativeMethods.RegisterHotKey(
            _hwndSource!.Handle, id, modifiers, key);

        return id;
    }

    /// <summary>
    /// Unregister a previously registered hotkey.
    /// </summary>
    public void Unregister(int id)
    {
        if (_hotkeys.Remove(id))
        {
            NativeMethods.UnregisterHotKey(_hwndSource!.Handle, id);
        }
    }

    /// <summary>
    /// Unregister all hotkeys.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var id in _hotkeys.Keys.ToList())
            Unregister(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_hotkeys.TryGetValue(id, out var entry))
            {
                entry.Callback();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static (uint modifiers, uint key) ParseHotkey(string hotkey)
    {
        uint modifiers = NativeMethods.MOD_NOREPEAT;
        var parts = hotkey.Split('+').Select(p => p.Trim().ToUpperInvariant()).ToArray();
        uint key = 0;

        foreach (var part in parts)
        {
            switch (part)
            {
                case "CTRL": modifiers |= NativeMethods.MOD_CONTROL; break;
                case "ALT": modifiers |= NativeMethods.MOD_ALT; break;
                case "SHIFT": modifiers |= NativeMethods.MOD_SHIFT; break;
                case "WIN": modifiers |= NativeMethods.MOD_WIN; break;
                default:
                    // Virtual key code for letter/number
                    if (part.Length == 1 && part[0] >= 'A' && part[0] <= 'Z')
                        key = (uint)(part[0]);
                    else if (part.Length == 1 && part[0] >= '0' && part[0] <= '9')
                        key = (uint)(part[0]);
                    else if (part.StartsWith("F") && int.TryParse(part[1..], out var fn))
                        key = (uint)(0x70 + fn - 1); // VK_F1 = 0x70
                    break;
            }
        }

        if (key == 0)
            throw new ArgumentException($"Cannot parse hotkey: {hotkey}");

        return (modifiers, key);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
    }
}
