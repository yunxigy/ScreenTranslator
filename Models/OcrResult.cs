namespace ScreenTranslator.Models;

internal sealed class OcrResult
{
    public string Text { get; set; } = "";
    public float Confidence { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Language { get; set; } = "en";
}
