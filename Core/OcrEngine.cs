using OpenCvSharp;
using ScreenTranslator.Models;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ScreenTranslator.Core;

/// <summary>
/// PaddleOCR 引擎封装（对接 Sdcb.PaddleOCR）
/// </summary>
internal sealed class OcrEngine : IDisposable
{
    private readonly object _lockObj = new();
    private bool _initialized;
    private bool _disposed;

    private PaddleOcrAll? _ocrAll;

    /// <summary>OCR 置信度阈值</summary>
    internal float ConfidenceThreshold { get; set; } = 0.5f;

    /// <summary>是否仅保留英文识别结果</summary>
    internal bool FilterEnglishOnly { get; set; } = true;

    /// <summary>
    /// 初始化 PaddleOCR 引擎
    /// </summary>
    /// <param name="modelDir">模型根目录，包含 det/rec/cls 子目录</param>
    internal void Initialize(string modelDir)
    {
        if (_initialized) return;

        lock (_lockObj)
        {
            if (_initialized) return;

            try
            {
                // 处理相对路径：基于程序所在目录
                if (!Path.IsPathRooted(modelDir))
                {
                    modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelDir);
                }

                Logger.Info($"模型目录: {modelDir}");

                var detDir = Path.Combine(modelDir, "ch_PP-OCRv4_det_infer");
                var recDir = Path.Combine(modelDir, "en_PP-OCRv4_rec_infer");
                var clsDir = Path.Combine(modelDir, "ch_ppocr_mobile_v2.0_cls_infer");

                if (!Directory.Exists(detDir))
                    throw new DirectoryNotFoundException($"检测模型目录不存在: {detDir}");
                if (!Directory.Exists(recDir))
                    throw new DirectoryNotFoundException($"识别模型目录不存在: {recDir}");
                if (!Directory.Exists(clsDir))
                    throw new DirectoryNotFoundException($"分类模型目录不存在: {clsDir}");

                // 查找识别模型标签文件
                var labelPath = FindLabelFile(recDir);
                if (labelPath == null)
                    throw new FileNotFoundException($"识别模型标签文件不存在于: {recDir}");

                var det = DetectionModel.FromDirectory(detDir, ModelVersion.V4);
                var cls = ClassificationModel.FromDirectory(clsDir, ModelVersion.V2);
                var rec = RecognizationModel.FromDirectory(recDir, labelPath, ModelVersion.V4);
                var model = new FullOcrModel(det, cls, rec);

                _ocrAll = new PaddleOcrAll(model, default!);

                _initialized = true;
                Logger.Info("OCR 引擎初始化成功 (Sdcb.PaddleOCR)");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Logger.Error($"OCR 引擎初始化失败: {inner.GetType().Name}: {inner.Message}");
                if (inner.InnerException != null)
                    Logger.Error($"  内层: {inner.InnerException.GetType().Name}: {inner.InnerException.Message}");
                Logger.Error($"  堆栈: {inner.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// 查找识别模型的标签文件
    /// </summary>
    private static string? FindLabelFile(string recDir)
    {
        // 常见标签文件名
        string[] names = { "ppocr_keys.txt", "en_dict.txt", "dict.txt" };
        foreach (var name in names)
        {
            var path = Path.Combine(recDir, name);
            if (File.Exists(path)) return path;
        }

        // 尝试上级目录
        var parentDir = Path.GetDirectoryName(recDir);
        if (parentDir != null)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(parentDir, name);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 执行 OCR 识别
    /// </summary>
    internal OcrResult[] Recognize(Bitmap image)
    {
        if (_disposed || image == null)
            return Array.Empty<OcrResult>();

        if (_ocrAll == null || !_initialized)
        {
            Logger.Warn("OCR 引擎未初始化");
            return Array.Empty<OcrResult>();
        }

        lock (_lockObj)
        {
            try
            {
                using var mat = BitmapToMat(image);
                if (mat.Empty())
                {
                    Logger.Warn("Bitmap 转换失败");
                    return Array.Empty<OcrResult>();
                }

                var result = _ocrAll.Run(mat);
                if (result.Regions == null || result.Regions.Length == 0)
                    return Array.Empty<OcrResult>();

                return result.Regions.Select(r => new OcrResult
                {
                    Text = r.Text,
                    Confidence = r.Score,
                    X = (int)r.Rect.BoundingRect().X,
                    Y = (int)r.Rect.BoundingRect().Y,
                    Width = (int)r.Rect.BoundingRect().Width,
                    Height = (int)r.Rect.BoundingRect().Height,
                    Language = DetectLanguage(r.Text),
                }).ToArray();
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Logger.Error($"OCR 识别异常: {inner.Message}");
                return Array.Empty<OcrResult>();
            }
        }
    }

    /// <summary>
    /// Bitmap → OpenCvSharp.Mat 转换
    /// </summary>
    private static Mat BitmapToMat(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        var bytes = ms.ToArray();
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    /// <summary>
    /// 过滤：只保留英文且置信度达标的结果
    /// </summary>
    internal OcrResult[] FilterResults(OcrResult[] results)
    {
        return results.Where(r =>
        {
            if (r.Confidence < ConfidenceThreshold) return false;
            if (FilterEnglishOnly && !IsEnglishText(r.Text)) return false;
            if (string.IsNullOrWhiteSpace(r.Text)) return false;
            return true;
        }).ToArray();
    }

    /// <summary>
    /// 检测文本是否为英文（仅含 ASCII 可打印字符）
    /// </summary>
    private static bool IsEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var letterCount = text.Count(char.IsLetter);
        var asciiCount = text.Count(c => c >= 0x20 && c <= 0x7E);

        return letterCount > 0 && (double)asciiCount / text.Length > 0.7;
    }

    /// <summary>
    /// 简单语言检测（通过字符范围判断）
    /// </summary>
    private static string DetectLanguage(string text)
    {
        if (string.IsNullOrEmpty(text)) return "unknown";

        if (text.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            return "zh";

        if (text.Any(c => (c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF)))
            return "ja";

        return "en";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lockObj)
        {
            try { _ocrAll?.Dispose(); } catch { }
            _ocrAll = null;
        }

        _initialized = false;
    }
}
