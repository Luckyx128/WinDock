using System;
using System.Collections.Generic;

namespace WinDock.Models;

public sealed class DockSettings
{
    public BarEdge Edge { get; set; } = BarEdge.Bottom;
    public double BarHeightDip { get; set; } = 48.0;
    public bool AutoHideNativeTaskbar { get; set; } = true;
    public bool AutoHideOnFullscreen { get; set; } = true;
    public bool ShowStartButton { get; set; } = true;
    public bool ShowSystemTrayCluster { get; set; } = true;
    public DockTheme Theme { get; set; } = new();
    public string SelectedThemeId { get; set; } = "dark-modern";
    public string SelectedAccentHex { get; set; } = "#007ACC";
    public bool AreDesktopWidgetsVisible { get; set; } = true;
    public List<string> DockOrderKeys { get; set; } = new();
    public List<DockShortcut> PinnedShortcuts { get; set; } = new();
    public List<string> EnabledWidgets { get; set; } = new()
    {
        "hardware",
        "media",
        "steam-jump-in",
        "discord"
    };
    public Dictionary<string, WidgetSize> WidgetSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hardware"] = WidgetSize.Size1x2,
        ["media"] = WidgetSize.Size1x2,
        ["steam-jump-in"] = WidgetSize.Size1x2,
        ["steam-recent"] = WidgetSize.Size1x3,
        ["steam-downloads"] = WidgetSize.Size1x2,
        ["steam-now-playing"] = WidgetSize.Size1x2,
        ["steam-hub"] = WidgetSize.Size1x1,
        ["steam-storage"] = WidgetSize.Size1x2,
        ["discord"] = WidgetSize.Size1x1,
        ["discord-voice"] = WidgetSize.Size1x2,
        ["discord-controls"] = WidgetSize.Size1x2,
        ["discord-mentions"] = WidgetSize.Size1x1,
        ["discord-channel"] = WidgetSize.Size1x2,
        ["discord-hub"] = WidgetSize.Size1x2,
        ["ai"] = WidgetSize.Size1x1,
        ["clock"] = WidgetSize.Size1x2
    };
    public bool AreDesktopWidgetsPinned { get; set; } = true;
}
