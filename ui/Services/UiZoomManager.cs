using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CloudRedirect.Services;

/// <summary>
/// Manages responsive auto-zoom and manual interface scaling for CloudRedirect.
/// Scales the GUI dynamically to fit the window in a single interface without scrolling,
/// while supporting interactive Ctrl+Wheel and toolbar zoom controls.
/// </summary>
public class UiZoomManager
{
    private static UiZoomManager? _instance;
    public static UiZoomManager Instance => _instance ??= new UiZoomManager();

    private MainWindow? _window;
    private FrameworkElement? _contentHost;
    private Frame? _rootFrame;
    private ScaleTransform? _scaleTransform;
    private DispatcherTimer? _debounceTimer;

    public bool IsAutoFit { get; private set; } = true;
    public double CurrentScale { get; private set; } = 1.0;

    public event Action<double, bool>? OnZoomChanged;

    private bool _initialAutoFitApplied;

    public void Initialize(MainWindow window, FrameworkElement contentHost, Frame rootFrame, ScaleTransform scaleTransform)
    {
        _window = window;
        _contentHost = contentHost;
        _rootFrame = rootFrame;
        _scaleTransform = scaleTransform;

        IsAutoFit = AppSettings.AutoFitZoom;
        CurrentScale = AppSettings.ZoomScale > 0 ? AppSettings.ZoomScale : 1.0;

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            if (IsAutoFit)
            {
                RecalculateAutoFitImmediate();
            }
        };

        _contentHost.SizeChanged += (_, _) =>
        {
            if (IsAutoFit)
            {
                TriggerAutoFitRecalculation();
            }
        };

        _window.SizeChanged += (_, _) =>
        {
            if (IsAutoFit)
            {
                TriggerAutoFitRecalculation();
            }
        };

        _window.StateChanged += (_, _) =>
        {
            if (IsAutoFit)
            {
                TriggerAutoFitRecalculation();
            }
        };

        _window.DpiChanged += (_, _) =>
        {
            if (IsAutoFit)
            {
                TriggerAutoFitRecalculation();
            }
        };

        _rootFrame.Navigated += (_, _) =>
        {
            if (_rootFrame.Content is Page page)
            {
                HookPageContent(page);
                page.Loaded += (_, _) =>
                {
                    HookPageContent(page);
                    if (IsAutoFit)
                    {
                        RecalculateAutoFitImmediate();
                    }
                };
            }

            _window.Dispatcher.InvokeAsync(() =>
            {
                if (_rootFrame.Content is Pages.SuoRemotePage)
                {
                    // WebView2 (HwndHost) in WPF does not support LayoutTransform;
                    // Any ScaleTransform != 1.0 causes WebView2 to render blank/black.
                    ApplyScale(1.0, false);
                    return;
                }

                if (IsAutoFit)
                {
                    RecalculateAutoFitImmediate();
                }
                else
                {
                    ApplyScale(CurrentScale, false);
                }
            }, DispatcherPriority.Loaded);

            // Re-evaluate auto-fit after page controls finish asynchronous measuring
            var navDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            navDelayTimer.Tick += (_, _) =>
            {
                navDelayTimer.Stop();
                if (IsAutoFit && _rootFrame.Content is not Pages.SuoRemotePage)
                {
                    RecalculateAutoFitImmediate();
                }
            };
            navDelayTimer.Start();
        };

        _window.PreviewMouseWheel += Window_PreviewMouseWheel;
        _window.PreviewKeyDown += Window_PreviewKeyDown;

        if (IsAutoFit)
        {
            _initialAutoFitApplied = false;
            _window.Dispatcher.InvokeAsync(RecalculateAutoFitImmediate, DispatcherPriority.Loaded);
            OnZoomChanged?.Invoke(CurrentScale, true);

            // Multi-pass startup timers to ensure auto-fit recalculates after async card data loads
            var startupTimer1 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            startupTimer1.Tick += (_, _) =>
            {
                startupTimer1.Stop();
                if (IsAutoFit && _rootFrame.Content is not Pages.SuoRemotePage)
                {
                    RecalculateAutoFitImmediate();
                }
            };
            startupTimer1.Start();

            var startupTimer2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            startupTimer2.Tick += (_, _) =>
            {
                startupTimer2.Stop();
                if (IsAutoFit && _rootFrame.Content is not Pages.SuoRemotePage)
                {
                    RecalculateAutoFitImmediate();
                }
            };
            startupTimer2.Start();
        }
        else
        {
            ApplyScale(CurrentScale, false);
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            if (e.Delta > 0)
                ZoomIn();
            else if (e.Delta < 0)
                ZoomOut();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key is Key.D0 or Key.NumPad0)
            {
                e.Handled = true;
                SetAutoFit(true);
            }
            else if (e.Key is Key.OemPlus or Key.Add)
            {
                e.Handled = true;
                ZoomIn();
            }
            else if (e.Key is Key.OemMinus or Key.Subtract)
            {
                e.Handled = true;
                ZoomOut();
            }
        }
    }

    public void ZoomIn()
    {
        if (_rootFrame?.Content is Pages.SuoRemotePage suoPage)
        {
            suoPage.ZoomIn();
            return;
        }

        _debounceTimer?.Stop();
        IsAutoFit = false;
        // Snap to clean 10% steps
        double current = Math.Round(CurrentScale * 10.0) / 10.0;
        double newScale = Math.Round(current + 0.10, 2);
        if (newScale > 1.50) newScale = 1.50;
        ApplyScale(newScale, false);
        AppSettings.ZoomScale = newScale;
    }

    public void ZoomOut()
    {
        if (_rootFrame?.Content is Pages.SuoRemotePage suoPage)
        {
            suoPage.ZoomOut();
            return;
        }

        _debounceTimer?.Stop();
        IsAutoFit = false;
        // Snap to clean 10% steps
        double current = Math.Round(CurrentScale * 10.0) / 10.0;
        double newScale = Math.Round(current - 0.10, 2);
        if (newScale < 0.60) newScale = 0.60;
        ApplyScale(newScale, false);
        AppSettings.ZoomScale = newScale;
    }

    public void SetScale(double scale, bool isAuto = false)
    {
        IsAutoFit = isAuto;
        AppSettings.AutoFitZoom = isAuto;
        double clamped = Math.Clamp(scale, 0.60, 1.50);
        ApplyScale(clamped, isAuto);
        if (!isAuto)
        {
            AppSettings.ZoomScale = clamped;
        }
    }

    public void ToggleAutoFit()
    {
        if (IsAutoFit)
        {
            // If currently in AutoFit, toggle to 100% manual scale
            SetScale(1.0, isAuto: false);
        }
        else
        {
            // If in manual scale, toggle back to AutoFit
            SetAutoFit(true);
        }
    }

    public void RefreshDisplay()
    {
        ApplyScale(CurrentScale, IsAutoFit);
    }

    public void SetAutoFit(bool enable)
    {
        IsAutoFit = enable;
        AppSettings.AutoFitZoom = enable;
        if (enable)
        {
            _initialAutoFitApplied = false;
            RecalculateAutoFitImmediate();
        }
        else
        {
            ApplyScale(CurrentScale, false);
        }
    }

    public void TriggerAutoFitRecalculation()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    public void RecalculateAutoFitImmediate()
    {
        if (_contentHost == null || _rootFrame == null || _rootFrame.Content is not Page page)
            return;

        if (page is Pages.SuoRemotePage)
        {
            ApplyScale(1.0, false);
            return;
        }

        HookPageContent(page);

        double availableHeight = _contentHost.ActualHeight;
        double availableWidth = _contentHost.ActualWidth;

        if (availableHeight <= 50 || availableWidth <= 50) return;

        var scrollViewer = FindVisualChild<ScrollViewer>(page);
        double unscaledHeight = 0;
        double unscaledWidth = 0;

        if (scrollViewer != null)
        {
            if (scrollViewer.Content is FrameworkElement scrollContent)
            {
                // Force an immediate layout update so any visibility toggles or collapsed panels
                // are fully computed into DesiredSize / ActualHeight before measuring
                scrollContent.UpdateLayout();
                scrollContent.Measure(new Size(availableWidth > 0 ? availableWidth : 800, double.PositiveInfinity));
                double paddingY = scrollViewer.Padding.Top + scrollViewer.Padding.Bottom;
                double paddingX = scrollViewer.Padding.Left + scrollViewer.Padding.Right;

                double measuredH = scrollContent.DesiredSize.Height + paddingY;
                double measuredW = scrollContent.DesiredSize.Width + paddingX;

                unscaledHeight = measuredH > 0 ? measuredH : scrollViewer.ExtentHeight;
                unscaledWidth = measuredW > 0 ? measuredW : scrollViewer.ExtentWidth;
            }
            else
            {
                unscaledHeight = scrollViewer.ExtentHeight;
                unscaledWidth = scrollViewer.ExtentWidth;
            }
        }

        if (unscaledHeight <= 0 && page.Content is FrameworkElement rootElem)
        {
            if (rootElem is ContentControl cc && cc.Content is FrameworkElement innerChild)
            {
                innerChild.Measure(new Size(availableWidth, double.PositiveInfinity));
                unscaledHeight = innerChild.DesiredSize.Height;
                unscaledWidth = innerChild.DesiredSize.Width;
            }
            else
            {
                rootElem.Measure(new Size(availableWidth, double.PositiveInfinity));
                unscaledHeight = rootElem.DesiredSize.Height;
                unscaledWidth = rootElem.DesiredSize.Width;
            }
        }

        if (unscaledHeight > 0)
        {
            // Ample safety margin so content never touches the bottom/side edge and no scrollbar appears
            double safetyMarginY = 20.0;
            double safetyMarginX = 16.0;
            double targetHeight = Math.Max(100, availableHeight - safetyMarginY);
            double targetWidth = Math.Max(100, availableWidth - safetyMarginX);

            // In normal windowed mode, never upscale beyond 1.0 (100%).
            // 100% is the ideal baseline; only scale down when needed to prevent scrolling!
            double[] allowedScales;
            if (_window != null && _window.WindowState == WindowState.Maximized)
            {
                allowedScales = new[] { 1.15, 1.10, 1.05, 1.0, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60 };
            }
            else
            {
                allowedScales = new[] { 1.0, 0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60 };
            }

            double autoScale = 0.60;
            foreach (var s in allowedScales)
            {
                if (unscaledHeight * s <= targetHeight && (unscaledWidth <= 0 || unscaledWidth * s <= targetWidth))
                {
                    autoScale = s;
                    break;
                }
            }

            // In AutoFit mode, manage scrollbars so interface looks clean and unclipped
            if (scrollViewer != null)
            {
                scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                if (unscaledHeight * autoScale <= availableHeight + 2)
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                }
                else
                {
                    scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
            }

            // Apply if there's a noticeable difference or if initial scale hasn't been applied yet
            if (Math.Abs(autoScale - CurrentScale) > 0.015 || !_initialAutoFitApplied)
            {
                _initialAutoFitApplied = true;
                ApplyScale(autoScale, true);
            }
            else if (IsAutoFit)
            {
                OnZoomChanged?.Invoke(CurrentScale, true);
            }
        }
    }

    private FrameworkElement? _subscribedScrollContent;

    public void HookPageContent(Page page)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(page);
        var content = (scrollViewer?.Content as FrameworkElement) ?? (page.Content as FrameworkElement);
        if (content != null && content != _subscribedScrollContent)
        {
            if (_subscribedScrollContent != null)
                _subscribedScrollContent.SizeChanged -= ScrollContent_SizeChanged;
            _subscribedScrollContent = content;
            _subscribedScrollContent.SizeChanged += ScrollContent_SizeChanged;
        }
    }

    private void ScrollContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsAutoFit && _rootFrame?.Content is not Pages.SuoRemotePage)
        {
            TriggerAutoFitRecalculation();
        }
    }

    private void ApplyScale(double scale, bool isAuto)
    {
        CurrentScale = scale;
        if (_scaleTransform != null)
        {
            _scaleTransform.ScaleX = scale;
            _scaleTransform.ScaleY = scale;
        }
        OnZoomChanged?.Invoke(scale, isAuto);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }
}
