using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CloudRedirect.Models;
using CloudRedirect.Services;

namespace CloudRedirect.Windows;

public partial class StickyNoteWindow : Window
{
    private readonly StickyNoteItem _note;
    private readonly DispatcherTimer _debounceTimer;
    private double _expandedHeight = 240;
    private bool _isInitializing = true;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public StickyNoteWindow(StickyNoteItem note)
    {
        InitializeComponent();
        _note = note;

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            SaveNoteData();
        };

        ApplyNoteDataToUI();
        _isInitializing = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int currentStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, currentStyle | WS_EX_TOOLWINDOW);
        }
    }

    private void ApplyNoteDataToUI()
    {
        TitleInput.Text = _note.Title;
        NoteContentInput.Text = _note.Content;
        GameTagText.Text = string.IsNullOrWhiteSpace(_note.GameName) ? "Global" : _note.GameName;

        // Restore dimensions
        if (_note.Width >= MinWidth) Width = _note.Width;
        if (_note.Height >= MinHeight)
        {
            Height = _note.Height;
            _expandedHeight = _note.Height;
        }

        // Restore position
        if (_note.Left >= 0 && _note.Top >= 0)
        {
            Left = _note.Left;
            Top = _note.Top;
        }
        else
        {
            // Default position on right side of primary work area
            var work = SystemParameters.WorkArea;
            Left = Math.Max(20, work.Right - Width - 30);
            Top = Math.Max(20, work.Top + 60);
        }

        // Transparency / Opacity
        double op = Math.Clamp(_note.Opacity, 0.20, 1.0);
        Opacity = op;
        OpacitySlider.Value = Math.Round(op * 100);
        OpacityPercentText.Text = $"{(int)(op * 100)}%";
        PopupOpacityValueText.Text = $"{(int)(op * 100)}%";

        // Pinning
        Topmost = _note.IsPinned;
        UpdatePinButtonVisual();

        // Theme colors
        ApplyThemeColor(_note.ThemeColor, _note.AccentColor);

        // Lock state
        UpdateLockVisual();

        // Collapse state
        if (_note.IsCollapsed)
        {
            CollapseToTitle();
        }
    }

    private void SaveNoteData()
    {
        if (_isInitializing) return;

        _note.Title = TitleInput.Text;
        _note.Content = NoteContentInput.Text;
        _note.Width = Width;
        _note.Height = _note.IsCollapsed ? _expandedHeight : Height;
        _note.Left = Left;
        _note.Top = Top;
        _note.Opacity = Opacity;
        _note.IsLocked = NoteContentInput.IsReadOnly;
        _note.UpdatedAt = DateTime.Now;

        StickyNotesService.SaveNotes();

        SaveStatusText.Visibility = Visibility.Visible;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            SaveStatusText.Visibility = Visibility.Collapsed;
        };
        t.Start();
    }

    #region Dragging & Resizing

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            _note.Left = Left;
            _note.Top = Top;
            StickyNotesService.SaveNotes();
        }
    }

    private void WindowResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = Math.Max(MinWidth, Width + e.HorizontalChange);
        double newHeight = Math.Max(MinHeight, Height + e.VerticalChange);

        Width = newWidth;
        Height = newHeight;
        _expandedHeight = newHeight;

        _note.Width = newWidth;
        _note.Height = newHeight;
        StickyNotesService.SaveNotes();
    }

    #endregion

    #region Pinning

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _note.IsPinned = !_note.IsPinned;
        Topmost = _note.IsPinned;
        UpdatePinButtonVisual();
        StickyNotesService.SaveNotes();

        var stateStr = _note.IsPinned ? "Pinned On Top 📌" : "Unpinned";
        SaveStatusText.Text = stateStr;
        SaveStatusText.Visibility = Visibility.Visible;
    }

    private void UpdatePinButtonVisual()
    {
        if (_note.IsPinned)
        {
            PinIconText.Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)); // Bright green
            PinButton.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x1E));
        }
        else
        {
            PinIconText.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            PinButton.Background = Brushes.Transparent;
        }
    }

    #endregion

    #region Transparency / Opacity

    private void OpacityButton_Click(object sender, RoutedEventArgs e)
    {
        OpacityPopup.IsOpen = !OpacityPopup.IsOpen;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        double val = e.NewValue;
        Opacity = Math.Clamp(val / 100.0, 0.20, 1.0);
        _note.Opacity = Opacity;
        OpacityPercentText.Text = $"{(int)val}%";
        PopupOpacityValueText.Text = $"{(int)val}%";
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OpacityPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && double.TryParse(tagStr, out var val))
        {
            OpacitySlider.Value = val;
            OpacityPopup.IsOpen = false;
        }
    }

    #endregion

    #region Theme & Colors

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = !ColorPopup.IsOpen;
    }

    private void ColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && tag.Contains('|'))
        {
            var parts = tag.Split('|');
            _note.ThemeColor = parts[0];
            _note.AccentColor = parts[1];
            ApplyThemeColor(_note.ThemeColor, _note.AccentColor);
            StickyNotesService.SaveNotes();
            ColorPopup.IsOpen = false;
        }
    }

    private void ApplyThemeColor(string bgHex, string accentHex)
    {
        try
        {
            var bgCol = (Color)ColorConverter.ConvertFromString(bgHex);
            var accentCol = (Color)ColorConverter.ConvertFromString(accentHex);

            NoteContainerBorder.Background = new SolidColorBrush(bgCol);
            NoteContainerBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, accentCol.R, accentCol.G, accentCol.B));
            AccentDot.Background = new SolidColorBrush(accentCol);
            NoteContentInput.CaretBrush = new SolidColorBrush(accentCol);
        }
        catch { }
    }

    #endregion

    #region Collapse / Minimize

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _note.IsCollapsed = !_note.IsCollapsed;
        if (_note.IsCollapsed)
        {
            CollapseToTitle();
        }
        else
        {
            ExpandToBody();
        }
        StickyNotesService.SaveNotes();
    }

    private void CollapseToTitle()
    {
        _expandedHeight = Height;
        BodyContainer.Visibility = Visibility.Collapsed;
        Height = 46;
        CollapseIconText.Text = "▼";
        CollapseButton.ToolTip = "Expand Note";
    }

    private void ExpandToBody()
    {
        BodyContainer.Visibility = Visibility.Visible;
        Height = Math.Max(120, _expandedHeight);
        CollapseIconText.Text = "▲";
        CollapseButton.ToolTip = "Collapse Note";
    }

    #endregion

    #region Text Editing & Quick Tools

    private void TitleInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void NoteContentInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void InsertTodo_Click(object sender, RoutedEventArgs e)
    {
        InsertTextAtCaret("[ ] ");
    }

    private void InsertBullet_Click(object sender, RoutedEventArgs e)
    {
        InsertTextAtCaret("• ");
    }

    private void InsertTimestamp_Click(object sender, RoutedEventArgs e)
    {
        InsertTextAtCaret($"[{DateTime.Now:HH:mm}] ");
    }

    private void InsertTextAtCaret(string text)
    {
        int idx = NoteContentInput.CaretIndex;
        NoteContentInput.Text = NoteContentInput.Text.Insert(idx, text);
        NoteContentInput.CaretIndex = idx + text.Length;
        NoteContentInput.Focus();
    }

    #endregion

    #region New Note & Lock Mode

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        CreateAndOpenNewNote();
    }

    private void CreateAndOpenNewNote()
    {
        var newNote = StickyNotesService.CreateNote(_note.GameName, "New Note", "");
        // Position slightly offset from current note
        newNote.Left = Left + 28;
        newNote.Top = Top + 28;
        newNote.ThemeColor = _note.ThemeColor;
        newNote.AccentColor = _note.AccentColor;
        newNote.Opacity = _note.Opacity;
        StickyNotesService.SaveNotes();
        StickyNotesService.ShowNoteWindow(newNote);
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _note.IsLocked = !_note.IsLocked;
        UpdateLockVisual();
        StickyNotesService.SaveNotes();

        SaveStatusText.Text = _note.IsLocked ? "Locked 🔒" : "Unlocked 🔓";
        SaveStatusText.Visibility = Visibility.Visible;
    }

    private void UpdateLockVisual()
    {
        NoteContentInput.IsReadOnly = _note.IsLocked;
        TitleInput.IsReadOnly = _note.IsLocked;
        if (_note.IsLocked)
        {
            LockIconText.Text = "🔒";
            LockIconText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xA1, 0x30)); // Amber warning
            LockButton.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x20, 0x10));
            LockButton.ToolTip = "Note is locked (Read-Only). Click or press Ctrl+L to unlock.";
        }
        else
        {
            LockIconText.Text = "🔓";
            LockIconText.Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0));
            LockButton.Background = Brushes.Transparent;
            LockButton.ToolTip = "Lock Note / Read-Only mode (Ctrl+L)";
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Alt+N or Ctrl+N: Create new note
        if ((Keyboard.Modifiers == ModifierKeys.Alt && (e.SystemKey == Key.N || e.Key == Key.N)) ||
            (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N))
        {
            e.Handled = true;
            CreateAndOpenNewNote();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.S)
            {
                e.Handled = true;
                SaveNoteData();
            }
            else if (e.Key == Key.P)
            {
                e.Handled = true;
                PinButton_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.L)
            {
                e.Handled = true;
                LockButton_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.M)
            {
                e.Handled = true;
                CollapseButton_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.D)
            {
                e.Handled = true;
                InsertTextAtCaret("[ ] ");
            }
            else if (e.Key == Key.T)
            {
                e.Handled = true;
                InsertTextAtCaret($"[{DateTime.Now:HH:mm}] ");
            }
        }
        else if (e.Key == Key.Escape)
        {
            CloseWindow_Click(this, new RoutedEventArgs());
        }
    }

    #endregion

    #region Window Lifecycle

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        var msg = $"Delete \"{_note.Title}\"?";
        var res = MessageBox.Show(msg, "Delete Sticky Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            StickyNotesService.DeleteNote(_note.Id);
            Close();
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        SaveNoteData();
        Close();
    }

    #endregion
}
