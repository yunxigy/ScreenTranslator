namespace ScreenTranslator.Models;

internal sealed class TranslationItem
{
    public string Text { get; set; } = "";
    public string SourceText { get; set; } = "";
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public float Confidence { get; set; }
}
