# Fraction P3 Review — Shell, WPF UI & App Entry

### [MAJOR] [OPEN] P3-01 `GlobalHotkeyHost.TryRegister` destroys the active hotkey before validating or registering the replacement
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Shell/GlobalHotkeyHost.cs:34-46`
- **Source**: `r-review` (2026-11-26T07:51:56Z)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User has a working hotkey (`Ctrl+Alt+D`), opens Settings, enters an invalid string (`"Foo"`) or an already-occupied hotkey (`"Ctrl+Shift+Esc"`), and clicks **Übernehmen** (`SettingsWindow.OnApplyHotkeyClick`). `TryRegister` calls `Unregister()` on line 37 before `TryParseHotkey` / `RegisterHotKey`, sees the failure, and returns `false`. `SettingsWindow` displays `"Ungültig / belegt"` and leaves `config.ToggleHotkey` as `"Ctrl+Alt+D"`, yet `Ctrl+Alt+D` is now unregistered in Win32 and stops working for the rest of the session.
- **Why**: `Unregister()` is invoked unconditionally at the start of `TryRegister` instead of only after parsing succeeds (and rolling back or registering the new hotkey first / restoring the previous hotkey on failure).
- **Fix**:
```diff
--- a/src/AutoDim.App/Shell/GlobalHotkeyHost.cs
+++ b/src/AutoDim.App/Shell/GlobalHotkeyHost.cs
@@ -15,6 +15,7 @@ public sealed class GlobalHotkeyHost : IDisposable
     private readonly HwndSource _messageWindow;
     private bool _registered;
+    private (uint Modifiers, uint VirtualKey)? _activeHotkey;
     private bool _disposed;
...
     public bool TryRegister(string hotkeyText)
     {
         if (_disposed) return false;
-        Unregister();
-
         if (!TryParseHotkey(hotkeyText, out uint modifiers, out uint vk))
         {
             return false;
         }
 
-        _registered = NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers | MOD_NOREPEAT, vk);
-        return _registered;
+        var previous = _activeHotkey;
+        Unregister();
+
+        if (NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers | MOD_NOREPEAT, vk))
+        {
+            _registered = true;
+            _activeHotkey = (modifiers, vk);
+            return true;
+        }
+
+        if (previous is { } prev &&
+            NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, prev.Modifiers | MOD_NOREPEAT, prev.VirtualKey))
+        {
+            _registered = true;
+            _activeHotkey = prev;
+        }
+        return false;
     }
```

### [MAJOR] [OPEN] P3-02 Closing `ZonePickerWindow` via Alt+F4 permanently disables the picker and leaves AutoDim paused
- **Status**: OPEN
- **Where**: `src/AutoDim.App/App.xaml.cs:82-117`
- **Source**: `r-review` (2026-11-26T07:51:56Z)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User opens the zone picker (`LaunchZonePicker`), then presses `Alt+F4` (or closes the window from the OS/taskbar) instead of `ESC` or right-click. `ZonePickerWindow` closes without invoking `onCompleted`, so `_activePickers` still holds the closed window reference (and any secondary-monitor pickers remain open), `_activePickers.Count > 0` blocks all future `LaunchZonePicker()` calls, and `_coordinator.IsPaused` stays `true` instead of restoring `wasPaused`.
- **Why**: `LaunchZonePicker` only cleans up `_activePickers` and restores `_coordinator.IsPaused` inside the `onCompleted` callback, with no `Closed` event handler on the `ZonePickerWindow` instances to handle external/OS close (and no guard for `scene.Monitors.Count == 0`).
- **Fix**:
```diff
--- a/src/AutoDim.App/App.xaml.cs
+++ b/src/AutoDim.App/App.xaml.cs
@@ -84,23 +84,36 @@ public partial class App : System.Windows.Application
         if (_activePickers.Count > 0 || _tracker is null || _coordinator is null) return;
 
+        var scene = _tracker.CaptureScene(_currentConfig);
+        if (scene.Monitors.Count == 0) return;
+
         bool wasPaused = _coordinator.IsPaused;
         _coordinator.IsPaused = true;
-
-        var scene = _tracker.CaptureScene(_currentConfig);
         int defaultIdx = _currentConfig.Zones.Count + 1;
+        bool finalized = false;
+
+        void FinishPicker(ZoneDefinition? zone)
+        {
+            if (finalized) return;
+            finalized = true;
+            ClosePickers();
+            if (zone is not null)
+            {
+                var nextZones = _currentConfig.Zones.Append(zone).ToArray();
+                ApplyConfig(_currentConfig with { Zones = nextZones });
+                _settingsWindow?.RefreshFromConfig();
+            }
+            _coordinator.IsPaused = wasPaused;
+        }
 
         foreach (var mon in scene.Monitors)
         {
-            var picker = new ZonePickerWindow(mon, defaultIdx, zone =>
-            {
-                ClosePickers();
-                if (zone is not null)
-                {
-                    var nextZones = _currentConfig.Zones.Append(zone).ToArray();
-                    ApplyConfig(_currentConfig with { Zones = nextZones });
-                    _settingsWindow?.RefreshFromConfig();
-                }
-                _coordinator.IsPaused = wasPaused;
-            });
+            var picker = new ZonePickerWindow(mon, defaultIdx, FinishPicker);
+            picker.Closed += (_, _) => FinishPicker(null);
             _activePickers.Add(picker);
             picker.Show();
         }
```

### [MAJOR] [OPEN] P3-03 `SettingsWindow.OnResetDefaultsClick` does not reset Windows autostart or the registered global hotkey
- **Status**: OPEN
- **Where**: `src/AutoDim.App/UI/SettingsWindow.xaml.cs:178-182`
- **Source**: `r-review` (2026-11-26T07:51:56Z)
- **Confidence**: HIGH
- **Category**: bug
- **Scenario**: User enables **AutoDim mit Windows starten** and changes the pause hotkey to `Ctrl+Shift+P`, then clicks **Standardwerte** (`OnResetDefaultsClick`). `config.json` and the Settings UI revert to `StartWithWindows = false` and `ToggleHotkey = "Ctrl+Alt+D"`, but `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AutoDim` remains in the registry and `GlobalHotkeyHost` keeps `Ctrl+Shift+P` registered instead of `Ctrl+Alt+D`.
- **Why**: `OnResetDefaultsClick` only calls `_applyConfig(DimConfig.Default)` and `LoadFromConfig(...)`, bypassing `StartupManager.SetEnabled` and `_tryApplyHotkey`.
- **Fix**:
```diff
--- a/src/AutoDim.App/UI/SettingsWindow.xaml.cs
+++ b/src/AutoDim.App/UI/SettingsWindow.xaml.cs
@@ -178,6 +178,8 @@ public partial class SettingsWindow : Window
     private void OnResetDefaultsClick(object sender, RoutedEventArgs e)
     {
+        StartupManager.SetEnabled(DimConfig.Default.StartWithWindows);
+        _tryApplyHotkey(DimConfig.Default.ToggleHotkey);
         _applyConfig(DimConfig.Default);
         LoadFromConfig(_getConfig());
     }
```

### [MINOR] [OPEN] P3-04 `SettingsWindow.OnSettingChanged` writes the Windows Run registry key on every slider drag tick
- **Status**: OPEN
- **Where**: `src/AutoDim.App/UI/SettingsWindow.xaml.cs:95-119`
- **Source**: `r-review` (2026-11-26T07:51:56Z)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: User drags any opacity, delay, or fade slider in `SettingsWindow`. `ValueChanged` fires on every tick, and `OnSettingChanged` unconditionally calls `StartupManager.SetEnabled(updated.StartWithWindows)`, opening `HKCU\...\Run` with write access and writing or deleting the registry value on every tick even when `StartWithWindows` did not change.
- **Why**: `StartupManager.SetEnabled` is called inside the shared `OnSettingChanged` handler rather than only when `StartWithWindows` actually changes.
- **Fix**:
```diff
--- a/src/AutoDim.App/UI/SettingsWindow.xaml.cs
+++ b/src/AutoDim.App/UI/SettingsWindow.xaml.cs
@@ -97,7 +97,8 @@ public partial class SettingsWindow : Window
         if (_loading || !IsLoaded) return;
         UpdateLabels();
 
-        DimConfig updated = _getConfig() with
+        DimConfig current = _getConfig();
+        DimConfig updated = current with
         {
             InactiveWindowOpacity = InactiveOpacitySlider.Value / 100.0,
             TaskbarOpacity = TaskbarOpacitySlider.Value / 100.0,
@@ -114,7 +115,10 @@ public partial class SettingsWindow : Window
             StartWithWindows = AutostartCheck.IsChecked == true
         };
 
-        StartupManager.SetEnabled(updated.StartWithWindows);
+        if (updated.StartWithWindows != current.StartWithWindows)
+        {
+            StartupManager.SetEnabled(updated.StartWithWindows);
+        }
         _applyConfig(updated);
     }
```

### [MINOR] [OPEN] P3-05 `OnZoneToggleChanged` triggers redundant config saves when `ZonesList` containers bind after `LoadFromConfig`
- **Status**: OPEN
- **Where**: `src/AutoDim.App/UI/SettingsWindow.xaml.cs:41-81`, `src/AutoDim.App/UI/SettingsWindow.xaml.cs:161-167`
- **Source**: `r-review` (2026-11-26T07:51:56Z)
- **Confidence**: HIGH
- **Category**: performance
- **Scenario**: User has one or more enabled zones and opens Settings (or adds/removes an excluded process, which calls `LoadFromConfig`). `LoadFromConfig` resets `_loading = false` in `finally` before WPF's layout pass generates `ZonesList` item containers; when `IsChecked="{Binding Enabled}"` transitions from `false` to `true` during container binding, `Checked` fires `OnZoneToggleChanged`, triggering `_applyConfig` (disk write + scene refresh) for every enabled zone.
- **Why**: WPF `ItemsSource` container generation and data binding occur asynchronously on layout after `LoadFromConfig` has already cleared `_loading`, and `OnZoneToggleChanged` does not check whether `item.Enabled` actually differs from the stored zone's `Enabled` value.
- **Fix**:
```diff
--- a/src/AutoDim.App/UI/SettingsWindow.xaml.cs
+++ b/src/AutoDim.App/UI/SettingsWindow.xaml.cs
@@ -162,6 +162,8 @@ public partial class SettingsWindow : Window
     {
         if (_loading || sender is not System.Windows.Controls.CheckBox cb || cb.DataContext is not ZoneListItem item) return;
         DimConfig cfg = _getConfig();
+        var existing = cfg.Zones.FirstOrDefault(z => z.Id == item.Id);
+        if (existing is null || existing.Enabled == item.Enabled) return;
         var next = cfg.Zones.Select(z => z.Id == item.Id ? z with { Enabled = item.Enabled } : z).ToArray();
         _applyConfig(cfg with { Zones = next });
     }
```

### [MINOR] [OPEN] P3-06 `GlobalHotkeyHost.TryParseHotkey` accepts undefined numeric values and modifier flags as virtual keys
- **Status**: OPEN
- **Where**: `src/AutoDim.App/Shell/GlobalHotkeyHost.cs:96-100`
- **Source**: `r-review` (2026-11-26T07:51:56Z)
- **Confidence**: MEDIUM
- **Category**: robustness
- **Scenario**: User enters `"Ctrl+123"` or `"Ctrl+999999"` or `"Ctrl+Shift"` in the hotkey box. `Enum.TryParse<Keys>` returns `true` for arbitrary integer strings (mapping `"123"` to `VK_F12`) and for `Keys.Shift` (`0x10000`, a WinForms modifier bitmask rather than a Win32 virtual key), passing invalid/unexpected key codes to `RegisterHotKey`.
- **Why**: `Enum.TryParse<Keys>` parses any integer string regardless of whether it is a defined enum member, and `Keys` includes WinForms modifier masks (`Control`, `Alt`, `Shift`, `Modifiers`, `KeyCode`) that are not valid Win32 virtual key codes.
- **Fix**:
```diff
--- a/src/AutoDim.App/Shell/GlobalHotkeyHost.cs
+++ b/src/AutoDim.App/Shell/GlobalHotkeyHost.cs
@@ -93,7 +93,9 @@ public sealed class GlobalHotkeyHost : IDisposable
             return modifiers != 0;
         }
 
-        if (Enum.TryParse<Keys>(keyPart, ignoreCase: true, out var parsedKey) && parsedKey != Keys.None)
+        if (!int.TryParse(keyPart, out _) &&
+            Enum.TryParse<Keys>(keyPart, ignoreCase: true, out var parsedKey) &&
+            Enum.IsDefined(parsedKey) &&
+            parsedKey is > Keys.None and < Keys.Modifiers)
         {
             virtualKey = (uint)parsedKey;
             return modifiers != 0;
```
