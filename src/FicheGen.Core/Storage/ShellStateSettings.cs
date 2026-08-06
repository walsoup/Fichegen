namespace FicheGen.Core.Storage;

public sealed class ShellStateSettings
{
    public int X { get; set; } = -32000;
    public int Y { get; set; } = -32000;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 840;
    public bool IsMaximized { get; set; } = false;
    public bool IsAssistantVisible { get; set; } = true;
    public string Theme { get; set; } = "System";
    public string LastNavigationTag { get; set; } = "fiche";
}
