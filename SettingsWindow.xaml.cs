using ScreenTranslator.Core;
using System.Diagnostics;
using System.Windows;

namespace ScreenTranslator;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    internal SettingsWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadValues();
        LoadProcesses();
    }

    private void LoadValues()
    {
        TxtAppId.Text = _settings.BaiduAppId;
        TxtSecretKey.Text = _settings.BaiduSecretKey;
        TxtRefreshInterval.Text = _settings.RefreshIntervalMs.ToString();
        TxtFontSize.Text = _settings.FontSize.ToString();
        TxtFontColor.Text = _settings.FontColor;
    }

    private void LoadProcesses()
    {
        CmbProcess.Items.Clear();

        // 添加"全屏"选项
        CmbProcess.Items.Add(new ProcessItem { DisplayName = "（全屏 - 翻译整个桌面）", ProcessName = "" });

        // 获取所有有主窗口的进程
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero) continue;
                if (string.IsNullOrEmpty(proc.ProcessName)) continue;
                if (seen.Contains(proc.ProcessName)) continue;

                seen.Add(proc.ProcessName);

                var title = proc.MainWindowTitle;
                var displayName = string.IsNullOrEmpty(title)
                    ? proc.ProcessName
                    : $"{proc.ProcessName} — {title}";

                if (displayName.Length > 60)
                    displayName = displayName[..57] + "...";

                CmbProcess.Items.Add(new ProcessItem
                {
                    DisplayName = displayName,
                    ProcessName = proc.ProcessName,
                });
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }

        // 选中当前值
        var current = _settings.TargetProcessName ?? "";
        for (int i = 0; i < CmbProcess.Items.Count; i++)
        {
            if (CmbProcess.Items[i] is ProcessItem item &&
                string.Equals(item.ProcessName, current, StringComparison.OrdinalIgnoreCase))
            {
                CmbProcess.SelectedIndex = i;
                return;
            }
        }

        // 没找到匹配项，手动填入
        if (!string.IsNullOrEmpty(current))
        {
            CmbProcess.Text = current;
        }
        else
        {
            CmbProcess.SelectedIndex = 0;
        }
    }

    private void OnRefreshProcesses(object sender, RoutedEventArgs e)
    {
        var current = GetSelectedProcessName();
        LoadProcesses();

        // 恢复选中
        if (!string.IsNullOrEmpty(current))
        {
            for (int i = 0; i < CmbProcess.Items.Count; i++)
            {
                if (CmbProcess.Items[i] is ProcessItem item &&
                    string.Equals(item.ProcessName, current, StringComparison.OrdinalIgnoreCase))
                {
                    CmbProcess.SelectedIndex = i;
                    return;
                }
            }
            CmbProcess.Text = current;
        }
    }

    private string? GetSelectedProcessName()
    {
        if (CmbProcess.SelectedItem is ProcessItem item)
            return string.IsNullOrEmpty(item.ProcessName) ? null : item.ProcessName;

        var text = CmbProcess.Text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtRefreshInterval.Text, out var interval) || interval < 100)
        {
            System.Windows.MessageBox.Show("刷新间隔必须为 >= 100 的整数", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(TxtFontSize.Text, out var fontSize) || fontSize <= 0)
        {
            System.Windows.MessageBox.Show("字体大小必须为正数", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.BaiduAppId = TxtAppId.Text.Trim();
        _settings.BaiduSecretKey = TxtSecretKey.Text.Trim();
        _settings.TargetProcessName = GetSelectedProcessName();
        _settings.RefreshIntervalMs = interval;
        _settings.FontSize = fontSize;
        _settings.FontColor = TxtFontColor.Text.Trim();

        _settings.Save();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private class ProcessItem
    {
        public string DisplayName { get; set; } = "";
        public string ProcessName { get; set; } = "";
    }
}
