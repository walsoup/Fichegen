using System;
using System.IO;
using System.Runtime.InteropServices;
using FicheGen.Core.Storage;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;
using Windows.Graphics;
using Color = Windows.UI.Color;
using Colors = Microsoft.UI.Colors;

namespace FicheGen.App;

public sealed partial class MainWindow
{
    private void UpdateCaptionButtonColors()
    {
        try
        {
            var tb = _appWindow.TitleBar;
            bool hc = IsHighContrastEnabled();
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark || _shellState.Theme is "Dark" or "Oled";

            tb.BackgroundColor = Colors.Transparent;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.InactiveBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (hc)
            {
                var windowText = GetSystemColorResource("SystemColorWindowTextColor", Colors.White);
                var highlight = GetSystemColorResource("SystemColorHighlightColor", Windows.UI.Color.FromArgb(255, 0, 120, 215));

                tb.ForegroundColor = windowText;
                tb.ButtonForegroundColor = windowText;
                tb.InactiveForegroundColor = windowText;
                tb.ButtonInactiveForegroundColor = windowText;
                tb.ButtonHoverForegroundColor = windowText;
                tb.ButtonHoverBackgroundColor = highlight;
                tb.ButtonPressedForegroundColor = windowText;
                tb.ButtonPressedBackgroundColor = highlight;
                return;
            }

            tb.ForegroundColor = dark ? Colors.White : Colors.Black;
            tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;

            var inactive = dark ? Color.FromArgb(255, 160, 160, 160) : Color.FromArgb(255, 110, 110, 110);
            tb.InactiveForegroundColor = inactive;
            tb.ButtonInactiveForegroundColor = inactive;

            tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
            tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0);
            tb.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
            tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(56, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Couleurs des boutons de la barre de titre non appliquées.");
        }
    }

    private static Windows.UI.Color GetSystemColorResource(string key, Windows.UI.Color fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var res))
            {
                if (res is SolidColorBrush brush) return brush.Color;
                if (res is Windows.UI.Color color) return color;
            }
        }
        catch { }
        return fallback;
    }

    private void UpdateTitleBarLayout()
    {
        try
        {
            double scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1;

            var tb = _appWindow.TitleBar;
            AppTitleBar.Padding = new Thickness(
                Math.Max(tb.LeftInset, 0) / scale, 0,
                Math.Max(tb.RightInset, 0) / scale, 0);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Mise à jour des insets de la barre de titre impossible.");
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter op && op.State == OverlappedPresenterState.Restored)
        {
            _lastNormalBounds = new RectInt32(
                sender.Position.X, sender.Position.Y,
                sender.Size.Width, sender.Size.Height);
        }

        if (args.DidSizeChange || args.DidPresenterChange)
        {
            UpdateTitleBarLayout();
        }
    }

    private void RestoreWindowPlacement()
    {
        try
        {
            double scale = GetDpiForWindow(_hwnd) / 96.0;
            if (scale <= 0) scale = 1;

            if (_shellState.X == PlacementSentinel || _shellState.Y == PlacementSentinel)
            {
                // Première exécution : taille par défaut, centrée sur l'écran
                int w = (int)(DefaultWindowWidthDip * scale);
                int h = (int)(DefaultWindowHeightDip * scale);
                var wa = DisplayArea.GetFromWindowId(_windowId, DisplayAreaFallback.Primary).WorkArea;
                w = Math.Min(w, wa.Width);
                h = Math.Min(h, wa.Height);
                _appWindow.MoveAndResize(new RectInt32(
                    wa.X + (wa.Width - w) / 2,
                    wa.Y + (wa.Height - h) / 2,
                    w, h));
            }
            else
            {
                int w = Math.Max(_shellState.Width, (int)(MinWindowWidthDip * scale));
                int h = Math.Max(_shellState.Height, (int)(MinWindowHeightDip * scale));
                var rect = new RectInt32(_shellState.X, _shellState.Y, w, h);

                if (IsRectOnScreen(rect))
                {
                    _appWindow.MoveAndResize(rect);
                }
                else
                {
                    // La position enregistrée est hors écran : on restaure au moins la taille
                    _appWindow.Resize(new SizeInt32(w, h));
                }
            }

            if (_shellState.IsMaximized && _appWindow.Presenter is OverlappedPresenter op)
            {
                op.Maximize();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Restauration de la géométrie de la fenêtre impossible.");
        }
    }

    private static bool IsRectOnScreen(RectInt32 rect)
    {
        try
        {
            foreach (var area in DisplayArea.FindAll())
            {
                var wa = area.WorkArea;
                bool intersects =
                    rect.X < wa.X + wa.Width && rect.X + rect.Width > wa.X &&
                    rect.Y < wa.Y + wa.Height && rect.Y + rect.Height > wa.Y;
                if (intersects) return true;
            }
        }
        catch { }
        return false;
    }

    // ----- Sous-classement Win32 pour la taille minimale (1024 × 640) -----

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newProc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr newProc);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, newProc)
            : SetWindowLong32(hWnd, nIndex, newProc);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private void HookWindowProc()
    {
        try
        {
            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc,
                Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Impossible d'installer la taille minimale de fenêtre.");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmGetMinMaxInfo)
        {
            double scale = GetDpiForWindow(hWnd) / 96.0;
            if (scale <= 0) scale = 1;

            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize = new POINT
            {
                X = (int)Math.Ceiling(MinWindowWidthDip * scale),
                Y = (int)Math.Ceiling(MinWindowHeightDip * scale)
            };
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private ShellStateSettings LoadShellState()
    {
        try
        {
            return _settingsStore.GetSettings<ShellStateSettings>() ?? new ShellStateSettings();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Impossible de charger l'état de la coque.");
            return new ShellStateSettings();
        }
    }

    private void SaveShellState()
    {
        _ = SaveShellStateCoreAsync();

        async Task SaveShellStateCoreAsync()
        {
            try
            {
                await _settingsStore.SaveSettingsAsync(_shellState).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Impossible d'enregistrer l'état de la coque.");
            }
        }
    }

    private static bool IsHighContrastEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\HighContrast");
            var raw = key?.GetValue("Flags");
            var flags = raw switch
            {
                int i => i,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 0
            };
            return (flags & 0x1) != 0;
        }
        catch
        {
            return false;
        }
    }
}
