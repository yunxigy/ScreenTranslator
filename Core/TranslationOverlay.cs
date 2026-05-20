using ScreenTranslator.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace ScreenTranslator.Core;

/// <summary>
/// 译文渲染控件 — 基于 DrawingVisual 的高性能渲染
/// 直接在 HwndSource 中通过 OnRender 绘制译文文本
/// </summary>
internal sealed class TranslationOverlay : FrameworkElement
{
    // ===== 可配置样式 =====
    internal double FontSize { get; set; } = 16;
    internal Color FontColor { get; set; } = Colors.White;
    internal Color StrokeColor { get; set; } = Colors.Black;
    internal double StrokeThickness { get; set; } = 2;
    internal byte BackgroundAlpha { get; set; } = 0x60;

    private TranslationItem[] _items = Array.Empty<TranslationItem>();
    private readonly object _lockObj = new();

    // ===== 缓存字体和样式对象（提升性能） =====
    private Typeface? _cachedTypeface;
    private CultureInfo? _cachedCulture;

    internal TranslationOverlay()
    {
        _cachedTypeface = new Typeface("Microsoft YaHei");
        _cachedCulture = CultureInfo.CurrentCulture;
    }

    /// <summary>
    /// 更新要渲染的译文条目并触发重绘
    /// </summary>
    internal void UpdateItems(IEnumerable<TranslationItem> items)
    {
        lock (_lockObj)
        {
            _items = items as TranslationItem[] ?? items.ToArray();
        }

        // 触发 WPF 重新渲染
        InvalidateVisual();
    }

    /// <summary>
    /// 核心渲染方法 — WPF 自动调用
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        TranslationItem[] items;
        lock (_lockObj)
        {
            items = _items;
        }

        if (items.Length == 0) return;

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Text)) continue;

            RenderTranslationItem(dc, item);
        }
    }

    /// <summary>
    /// 渲染单个译文条目
    /// </summary>
    private void RenderTranslationItem(DrawingContext dc, TranslationItem item)
    {
        var x = item.ScreenX;
        var y = item.ScreenY;

        // 1. 测量文本尺寸
        var formattedText = CreateFormattedText(item.Text);
        var textWidth = formattedText.Width;
        var textHeight = formattedText.Height;

        // 2. 根据原文宽度调节字号（如果译文太长则缩小）
        if (item.SourceWidth > 10 && textWidth > item.SourceWidth * 1.5)
        {
            var scale = Math.Max(0.5, item.SourceWidth * 1.2 / textWidth);
            var adjustedSize = Math.Max(8, FontSize * scale);
            formattedText = CreateFormattedText(item.Text, adjustedSize);
            textWidth = formattedText.Width;
            textHeight = formattedText.Height;
        }

        // 3. 半透明背景
        var bgBrush = new SolidColorBrush(Color.FromArgb(BackgroundAlpha, 0, 0, 0));
        var bgRect = new Rect(x - 2, y - 2, textWidth + 4, textHeight + 4);
        dc.DrawRectangle(bgBrush, null, bgRect);

        // 4. 黑色描边（外描边：8 方向偏移）
        var outlineOffset = StrokeThickness;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                dc.DrawText(
                    CreateFormattedText(item.Text, FontSize, StrokeColor),
                    new Point(x + dx * outlineOffset, y + dy * outlineOffset));
            }
        }

        // 5. 白色文字（在描边之上）
        dc.DrawText(formattedText, new Point(x, y));
    }

    /// <summary>
    /// 创建格式化文本对象（带缓存）
    /// </summary>
    private FormattedText CreateFormattedText(string text, double? fontSize = null, Color? fontColor = null)
    {
        var size = fontSize ?? FontSize;
        var color = fontColor ?? FontColor;

        var formattedText = new FormattedText(
            text,
            _cachedCulture ?? CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            _cachedTypeface,
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return formattedText;
    }

    /// <summary>
    /// 清除所有译文
    /// </summary>
    internal void Clear()
    {
        UpdateItems(Array.Empty<TranslationItem>());
    }
}
