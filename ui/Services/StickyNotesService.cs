using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using CloudRedirect.Models;
using CloudRedirect.Windows;

namespace CloudRedirect.Services;

public static class StickyNotesService
{
    private static readonly List<StickyNoteItem> _notes = new();
    private static readonly Dictionary<string, StickyNoteWindow> _openWindows = new();
    private static bool _isLoaded;
    private static readonly object _fileLock = new();

    public static event Action? OnNotesChanged;

    public static string GetNotesFilePath()
    {
        var dir = Path.Combine(SteamDetector.GetConfigDir(), "notes");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "sticky_notes.json");
    }

    public static void EnsureLoaded()
    {
        if (_isLoaded) return;
        lock (_fileLock)
        {
            if (_isLoaded) return;
            try
            {
                var path = GetNotesFilePath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<List<StickyNoteItem>>(json);
                    if (loaded != null)
                    {
                        _notes.Clear();
                        _notes.AddRange(loaded);
                    }
                }

                // If no notes exist yet, create a default sample note for guidance
                if (_notes.Count == 0)
                {
                    _notes.Add(new StickyNoteItem
                    {
                        Title = "🎮 In-Game Quick Notes",
                        Content = "[ ] Puzzle Door Code: 4821\n[ ] Check hidden chest behind waterfall\n• Use Ctrl+Space to toggle Game Space\n• Pin 📌 this note to keep it visible while gaming!",
                        GameName = "Global",
                        ThemeColor = "#142232",
                        AccentColor = "#66C0F4",
                        Opacity = 0.88,
                        IsPinned = false
                    });
                    SaveNotes();
                }
            }
            catch (Exception ex)
            {
                App.LogStartup($"Failed to load sticky notes: {ex.Message}");
            }
            finally
            {
                _isLoaded = true;
            }
        }
    }

    public static void SaveNotes()
    {
        lock (_fileLock)
        {
            try
            {
                var path = GetNotesFilePath();
                var json = JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                App.LogStartup($"Failed to save sticky notes: {ex.Message}");
            }
        }
        OnNotesChanged?.Invoke();
    }

    public static IReadOnlyList<StickyNoteItem> GetAllNotes()
    {
        EnsureLoaded();
        return _notes.OrderByDescending(n => n.UpdatedAt).ToList();
    }

    public static IReadOnlyList<StickyNoteItem> GetNotesForGame(string? gameName)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(gameName))
        {
            return _notes.OrderByDescending(n => n.UpdatedAt).ToList();
        }

        // Return notes for this game plus Global notes
        return _notes.Where(n =>
            string.Equals(n.GameName, gameName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.GameName, "Global", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.UpdatedAt)
            .ToList();
    }

    public static StickyNoteItem? GetNoteById(string id)
    {
        EnsureLoaded();
        return _notes.FirstOrDefault(n => n.Id == id);
    }

    public static StickyNoteItem CreateNote(string? gameName = null, string title = "New Note", string content = "")
    {
        EnsureLoaded();
        var note = new StickyNoteItem
        {
            Title = string.IsNullOrWhiteSpace(title) ? "New Note" : title,
            Content = content,
            GameName = string.IsNullOrWhiteSpace(gameName) ? "Global" : gameName,
            ThemeColor = "#142232",
            AccentColor = "#66C0F4",
            Opacity = 0.90,
            IsPinned = true,
            UpdatedAt = DateTime.Now
        };

        _notes.Insert(0, note);
        SaveNotes();
        return note;
    }

    public static void DeleteNote(string id)
    {
        EnsureLoaded();
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note != null)
        {
            CloseNoteWindow(id);
            _notes.Remove(note);
            SaveNotes();
        }
    }

    public static void ShowNoteWindow(StickyNoteItem note)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openWindows.TryGetValue(note.Id, out var existing))
            {
                if (existing.IsLoaded)
                {
                    existing.Show();
                    existing.Activate();
                    return;
                }
                _openWindows.Remove(note.Id);
            }

            var win = new StickyNoteWindow(note);
            _openWindows[note.Id] = win;
            win.Closed += (_, _) => _openWindows.Remove(note.Id);
            win.Show();
            win.Activate();
        });
    }

    public static void CloseNoteWindow(string id)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_openWindows.TryGetValue(id, out var win))
            {
                _openWindows.Remove(id);
                try { win.Close(); } catch { }
            }
        });
    }

    public static bool IsWindowOpen(string id)
    {
        return _openWindows.ContainsKey(id);
    }

    public static void ShowAllPinnedNotes(string? gameName = null)
    {
        EnsureLoaded();
        var pinned = GetNotesForGame(gameName).Where(n => n.IsPinned);
        foreach (var note in pinned)
        {
            ShowNoteWindow(note);
        }
    }

    public static void HideAllNotes()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var windows = _openWindows.Values.ToList();
            _openWindows.Clear();
            foreach (var win in windows)
            {
                try { win.Close(); } catch { }
            }
        });
    }

    public static void UpdateActiveGame(string? gameName)
    {
        if (string.IsNullOrEmpty(gameName))
        {
            // Game closed: close pinned in-game note windows to clean up user desktop
            HideAllNotes();
            return;
        }

        // Game started: pop up pinned notes for this game
        ShowAllPinnedNotes(gameName);
    }
}
