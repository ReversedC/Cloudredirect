using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CloudRedirect.Services;

public class LayoutElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Type { get; set; } = "button"; // "button", "stick", "dpad", "trackpad", "macro"
    public string Label { get; set; } = "BTN";
    public string Key { get; set; } = "SPACE"; // Key code or mouse code
    public float X { get; set; } = 0.5f; // Normalized 0.0 to 1.0
    public float Y { get; set; } = 0.5f;
    public float Width { get; set; } = 64f;
    public float Height { get; set; } = 64f;
    public float Opacity { get; set; } = 0.75f;
    public string Behavior { get; set; } = "normal"; // "normal", "toggle", "turbo"
    public int TurboHz { get; set; } = 10;
    public List<string> MacroKeys { get; set; } = new();
}

public class ControllerLayoutProfile
{
    public string AppId { get; set; } = "default";
    public string ProfileName { get; set; } = "Default Action Layout";
    public List<LayoutElement> Elements { get; set; } = new();
}

public static class RemoteProfileStore
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CloudRedirect",
        "remote_profiles"
    );

    static RemoteProfileStore()
    {
        try
        {
            if (!Directory.Exists(ProfilesDir))
                Directory.CreateDirectory(ProfilesDir);
        }
        catch { }
    }

    public static ControllerLayoutProfile GetProfileForGame(string appId)
    {
        try
        {
            string path = Path.Combine(ProfilesDir, $"{appId}.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ControllerLayoutProfile>(json);
                if (loaded != null && loaded.Elements.Count > 0)
                    return loaded;
            }
        }
        catch { }

        // Fallback to default Action/RPG preset
        return CreateDefaultPreset(appId);
    }

    public static bool SaveProfile(ControllerLayoutProfile profile)
    {
        try
        {
            if (!Directory.Exists(ProfilesDir))
                Directory.CreateDirectory(ProfilesDir);

            string path = Path.Combine(ProfilesDir, $"{profile.AppId}.json");
            string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static ControllerLayoutProfile CreateDefaultPreset(string appId)
    {
        return new ControllerLayoutProfile
        {
            AppId = appId,
            ProfileName = "Action / RPG (GameHub Style)",
            Elements = new List<LayoutElement>
            {
                // Left Analog Stick (WASD)
                new LayoutElement
                {
                    Id = "stick_wasd",
                    Type = "stick",
                    Label = "MOVE",
                    Key = "WASD",
                    X = 0.12f,
                    Y = 0.65f,
                    Width = 140f,
                    Height = 140f,
                    Opacity = 0.7f
                },
                // Sprint (Shift)
                new LayoutElement
                {
                    Id = "btn_shift",
                    Type = "button",
                    Label = "SPRINT",
                    Key = "SHIFT",
                    Behavior = "toggle",
                    X = 0.08f,
                    Y = 0.35f,
                    Width = 60f,
                    Height = 60f,
                    Opacity = 0.7f
                },
                // Crouch (Ctrl)
                new LayoutElement
                {
                    Id = "btn_ctrl",
                    Type = "button",
                    Label = "CROUCH",
                    Key = "CTRL",
                    Behavior = "toggle",
                    X = 0.18f,
                    Y = 0.35f,
                    Width = 60f,
                    Height = 60f,
                    Opacity = 0.7f
                },
                // Jump (Space)
                new LayoutElement
                {
                    Id = "btn_jump",
                    Type = "button",
                    Label = "JUMP",
                    Key = "SPACE",
                    X = 0.88f,
                    Y = 0.75f,
                    Width = 72f,
                    Height = 72f,
                    Opacity = 0.85f
                },
                // Interact / Use (E)
                new LayoutElement
                {
                    Id = "btn_interact",
                    Type = "button",
                    Label = "E",
                    Key = "E",
                    X = 0.88f,
                    Y = 0.55f,
                    Width = 64f,
                    Height = 64f,
                    Opacity = 0.85f
                },
                // Attack (Mouse Left Click)
                new LayoutElement
                {
                    Id = "btn_attack",
                    Type = "button",
                    Label = "ATK",
                    Key = "MOUSE_LEFT",
                    X = 0.80f,
                    Y = 0.65f,
                    Width = 70f,
                    Height = 70f,
                    Opacity = 0.85f
                },
                // Aim / Block (Mouse Right Click)
                new LayoutElement
                {
                    Id = "btn_aim",
                    Type = "button",
                    Label = "AIM",
                    Key = "MOUSE_RIGHT",
                    X = 0.80f,
                    Y = 0.45f,
                    Width = 64f,
                    Height = 64f,
                    Opacity = 0.8f
                },
                // Mouse Aim Trackpad Area (Right screen swipe)
                new LayoutElement
                {
                    Id = "trackpad_aim",
                    Type = "trackpad",
                    Label = "SWIPE TO LOOK",
                    Key = "MOUSE_LOOK",
                    X = 0.60f,
                    Y = 0.50f,
                    Width = 220f,
                    Height = 180f,
                    Opacity = 0.2f
                },
                // Escape / Menu
                new LayoutElement
                {
                    Id = "btn_esc",
                    Type = "button",
                    Label = "ESC",
                    Key = "ESC",
                    X = 0.05f,
                    Y = 0.08f,
                    Width = 50f,
                    Height = 40f,
                    Opacity = 0.6f
                },
                // Inventory / Tab
                new LayoutElement
                {
                    Id = "btn_tab",
                    Type = "button",
                    Label = "TAB",
                    Key = "TAB",
                    X = 0.14f,
                    Y = 0.08f,
                    Width = 50f,
                    Height = 40f,
                    Opacity = 0.6f
                }
            }
        };
    }
}
