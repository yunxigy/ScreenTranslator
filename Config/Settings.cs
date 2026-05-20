using System.Diagnostics;
using System.Text.Json;

namespace ScreenTranslator.Core;

/// <summary>
/// 应用配置管理（JSON 格式）
/// 配置文件位置: %APPDATA%/ScreenTranslator/settings.json
/// </summary>
public sealed class Settings
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScreenTranslator");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "settings.json");

    // ===== 百度翻译 API =====
    public string BaiduAppId { get; set; } = string.Empty;
    public string BaiduSecretKey { get; set; } = string.Empty;

    // ===== OCR 配置 =====
    /// <summary>模型文件目录</summary>
    public string ModelPath { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");

    /// <summary>OCR 刷新间隔（毫秒）</summary>
    public int RefreshIntervalMs { get; set; } = 1500;

    /// <summary>OCR 置信度阈值</summary>
    public float OcrConfidenceThreshold { get; set; } = 0.5f;

    // ===== 渲染样式 =====
    public double FontSize { get; set; } = 16;
    public string FontColor { get; set; } = "#FFFFFF";
    public double StrokeWidth { get; set; } = 2;
    public byte BackgroundAlpha { get; set; } = 0x30;

    // ===== 目标窗口 =====
    /// <summary>目标进程名称（为空则全屏 OCR）</summary>
    public string? TargetProcessName { get; set; } = "Unity";

    // ===== 热键 =====
    public string ToggleHotKey { get; set; } = "Ctrl+Alt+T";

    // ===== 运行时状态 =====
    public bool AutoStartTranslation { get; set; } = false;

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] 加载配置失败: {ex.Message}");
        }

        return new Settings();
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void Save()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(ConfigFile, json);

            Debug.WriteLine($"[Settings] 配置已保存: {ConfigFile}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] 保存配置失败: {ex.Message}");
        }
    }
}
