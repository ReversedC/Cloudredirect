using System;
using System.Text.Json.Serialization;

namespace CloudRedirect.Models;

public class StickyNoteItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Quick Note";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("gameName")]
    public string GameName { get; set; } = "Global";

    [JsonPropertyName("themeColor")]
    public string ThemeColor { get; set; } = "#142232"; // Steam Slate Navy

    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = "#66C0F4"; // Steam Cyan

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.90;

    [JsonPropertyName("isPinned")]
    public bool IsPinned { get; set; } = false;

    [JsonPropertyName("isCollapsed")]
    public bool IsCollapsed { get; set; } = false;

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; } = false;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 320;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 240;

    [JsonPropertyName("left")]
    public double Left { get; set; } = -1;

    [JsonPropertyName("top")]
    public double Top { get; set; } = -1;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
