using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace EmojiManager
{
    /// <summary>
    /// 程序设置管理类
    /// </summary>
    public class Settings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EmojiManager",
            "settings.json");

        /// <summary>
        /// 表情包根目录路径
        /// </summary>
        public string EmojiBasePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "表情包");

        /// <summary>
        /// 快捷键修饰符
        /// </summary>
        public uint HotkeyModifiers { get; set; } = 0x0000; // MOD_NONE

        /// <summary>
        /// 快捷键键码
        /// </summary>
        public uint HotkeyVirtualKey { get; set; } = 0x7B; // VK_F12

        /// <summary>
        /// 快捷键显示名称
        /// </summary>
        public string HotkeyDisplayName { get; set; } = "F12";

        /// <summary>
        /// 窗口位置 X
        /// </summary>
        public double WindowLeft { get; set; } = double.NaN; // NaN表示首次运行，使用默认位置

        /// <summary>
        /// 窗口位置 Y
        /// </summary>
        public double WindowTop { get; set; } = double.NaN; // NaN表示首次运行，使用默认位置

        /// <summary>
        /// 窗口宽度
        /// </summary>
        public double WindowWidth { get; set; } = 400;

        /// <summary>
        /// 窗口高度
        /// </summary>
        public double WindowHeight { get; set; } = 600;

        /// <summary>
        /// 是否钉住窗口
        /// </summary>
        public bool IsPinned { get; set; } = false;

        /// <summary>
        /// 窗口状态
        /// </summary>
        public WindowState WindowState { get; set; } = WindowState.Normal;

        /// <summary>
        /// 最近使用表情上限数量
        /// </summary>
        public int RecentEmojisLimit { get; set; } = 20;

        /// <summary>
        /// 最近使用的表情列表（按使用顺序，最新的在前面）
        /// </summary>
        public List<string> RecentEmojis { get; set; } = [];

        /// <summary>
        /// 加载设置
        /// </summary>
        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                    return settings ?? new Settings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return new Settings();
        }

        /// <summary>
        /// 保存设置
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 获取设置文件路径
        /// </summary>
        public static string GetSettingsPath()
        {
            return SettingsPath;
        }

        /// <summary>
        /// 添加最近使用的表情
        /// </summary>
        /// <param name="emojiPath">表情图片路径</param>
        public void AddRecentEmoji(string emojiPath)
        {
            if (string.IsNullOrEmpty(emojiPath) || !File.Exists(emojiPath))
                return;

            // 如果限制为0，表示关闭功能，清空列表并退出
            if (RecentEmojisLimit == 0)
            {
                if (RecentEmojis.Count > 0)
                {
                    RecentEmojis.Clear();
                    Save();
                }
                return;
            }

            // 移除已存在的相同表情（如果有）
            RecentEmojis.Remove(emojiPath);

            // 在列表前端插入新表情
            RecentEmojis.Insert(0, emojiPath);

            // 限制列表长度
            while (RecentEmojis.Count > RecentEmojisLimit)
            {
                RecentEmojis.RemoveAt(RecentEmojis.Count - 1);
            }

            // 自动保存
            Save();
        }

        /// <summary>
        /// 从最近使用表情列表中移除指定表情
        /// </summary>
        /// <param name="emojiPath">要移除的表情图片路径</param>
        public void RemoveRecentEmoji(string emojiPath)
        {
            if (!string.IsNullOrEmpty(emojiPath))
            {
                RecentEmojis.Remove(emojiPath);
            }
        }

        /// <summary>
        /// 清理无效的最近表情（文件不存在的）
        /// </summary>
        public void CleanupRecentEmojis()
        {
            RecentEmojis.RemoveAll(path => !File.Exists(path));
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
    }
} 