using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace EmojiManager
{
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID = 9000;
        
        private HwndSource? _source;
        private Settings _settings = null!;
        private bool _isVisible = false;
        private bool _isPinned = false;
        private FileSystemWatcher? _fileWatcher;
        private readonly object _reloadLock = new();
        private DateTime _lastReloadTime = DateTime.MinValue;
        private TaskbarIcon? _taskbarIcon;
        private System.Windows.Threading.DispatcherTimer? _foregroundWindowTracker;

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [LibraryImport("user32.dll")]
        private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private IntPtr _lastActiveWindow = IntPtr.Zero;
        private bool _shouldPasteAfterDeactivate = false;
        private IntPtr _previousForegroundWindow = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            InitializeWindow();
            InitializeFileWatcher();
            InitializeTaskbarIcon();
            StartForegroundWindowTracking();
        }

        private void LoadSettings()
        {
            try
            {
                _settings = Settings.Load();
            }
            catch (Exception ex)
            {
                // 如果加载设置失败，使用默认设置
                _settings = new Settings();
                MessageBox.Show($"加载设置时发生错误，将使用默认设置: {ex.Message}", "警告", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void InitializeWindow()
        {
            // 从设置中恢复窗口属性
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
            WindowState = _settings.WindowState;

            // 恢复窗口位置
            if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop))
            {
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
                
                // 确保窗口在屏幕范围内
                EnsureWindowInBounds();
            }
            else
            {
                // 默认位置在屏幕右下角
                var workingArea = SystemParameters.WorkArea;
                Left = workingArea.Right - Width - 20;
                Top = workingArea.Bottom - Height - 20;
            }

            // 恢复钉住状态
            _isPinned = _settings.IsPinned;
            Topmost = _isPinned;

            // 窗口初始显示，让用户知道程序已启动
            _isVisible = true;
        }

        private void EnsureWindowInBounds()
        {
            var workingArea = SystemParameters.WorkArea;
            
            // 确保窗口不超出屏幕边界
            if (Left < workingArea.Left) Left = workingArea.Left;
            if (Top < workingArea.Top) Top = workingArea.Top;
            if (Left + Width > workingArea.Right) Left = workingArea.Right - Width;
            if (Top + Height > workingArea.Bottom) Top = workingArea.Bottom - Height;
        }

        private void InitializeFileWatcher()
        {
            // 检查表情包路径是否存在，如果不存在则不启用文件监听
            if (!Directory.Exists(_settings.EmojiBasePath))
            {
                return; // 路径不存在时跳过文件监听器初始化
            }

            try
            {
                _fileWatcher = new FileSystemWatcher(_settings.EmojiBasePath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                };

                _fileWatcher.Created += OnFileSystemChanged;
                _fileWatcher.Deleted += OnFileSystemChanged;
                _fileWatcher.Renamed += OnFileSystemChanged;
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch
            {
                // 如果初始化失败，忽略错误继续运行
                _fileWatcher?.Dispose();
                _fileWatcher = null;
            }
        }

        private void InitializeTaskbarIcon()
        {
            try
            {
                _taskbarIcon = new TaskbarIcon();
                
                // 设置托盘图标路径
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmapImage.EndInit();
                    _taskbarIcon.IconSource = bitmapImage;
                }
                else
                {
                    // 如果图标文件不存在，创建一个简单的默认图标
                    _taskbarIcon.IconSource = CreateDefaultIcon;
                }
                
                _taskbarIcon.ToolTipText = "表情管理器";
                
                // 左键单击事件
                _taskbarIcon.TrayLeftMouseUp += (_, _) =>
                {
                    if (_isVisible)
                    {
                        HideWindow();
                    }
                    else
                    {
                        // 使用之前记录的前台窗口，而不是当前的（可能已经不是QQNT了）
                        _lastActiveWindow = _previousForegroundWindow;
                        ShowWindowFromTray();
                    }
                };
                
                // 右键菜单
                var contextMenu = new System.Windows.Controls.ContextMenu();
                
                var exitMenuItem = new System.Windows.Controls.MenuItem
                {
                    Header = "退出程序"
                };
                exitMenuItem.Click += (_, _) => 
                {
                    ExitApplication();
                };
                contextMenu.Items.Add(exitMenuItem);
                
                _taskbarIcon.ContextMenu = contextMenu;
            }
            catch (Exception ex)
            {
                // 如果托盘图标初始化失败，记录错误但继续运行
                MessageBox.Show($"托盘图标初始化失败: {ex.Message}", "警告", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static System.Windows.Media.ImageSource CreateDefaultIcon
        {
            get
            {
                // 创建一个简单的默认图标（16x16像素的纯色图标）
                var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(16, 16, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null);

                // 填充为蓝色
                var color = System.Windows.Media.Colors.DodgerBlue;
                var pixels = new uint[16 * 16];
                var colorValue = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);

                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = colorValue;
                }

                bitmap.WritePixels(new Int32Rect(0, 0, 16, 16), pixels, 16 * 4, 0);
                return bitmap;
            }
        }

        private void StartForegroundWindowTracking()
        {
            // 启动一个定时器，定期记录前台窗口（仅在窗口隐藏时）
            _foregroundWindowTracker = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // 每500ms检查一次
            };
            
            _foregroundWindowTracker.Tick += (_, _) =>
            {
                // 只有在窗口隐藏时才更新前台窗口记录
                if (_isVisible) 
                    return;
                var currentForeground = GetForegroundWindow();
                // 避免记录自己的窗口句柄
                if (currentForeground != new WindowInteropHelper(this).Handle)
                {
                    _previousForegroundWindow = currentForeground;
                }
            };
            
            _foregroundWindowTracker.Start();
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            // 防抖处理，避免频繁刷新
            lock (_reloadLock)
            {
                if (DateTime.Now - _lastReloadTime < TimeSpan.FromMilliseconds(500))
                    return;
                _lastReloadTime = DateTime.Now;
            }

            // 检查是否应该处理此文件变化
            var shouldProcess = false;
            var extension = Path.GetExtension(e.FullPath).ToLower();
            
            // 删除操作总是处理
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                shouldProcess = true;
            }
            else
            {
                // 检查是否为已知的图片格式
                var supportedExtensions = ImageFormatDetector.GetSupportedExtensions();
                var extensionsWithDot = supportedExtensions.Select(ext => "." + ext);
                
                if (extensionsWithDot.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    shouldProcess = true;
                }
                // 或者是可疑的文件（可能是QQNT错误命名的图片）
                else if (extension == ".null" || string.IsNullOrEmpty(extension) || 
                         !IsCommonNonImageExtension(extension))
                {
                    shouldProcess = true;
                }
            }

            if (shouldProcess)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(100); // 等待文件操作完成
                    await LoadEmojiData();
                });
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 注册热键
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            _source.AddHook(HwndHook);
            RegisterHotkey();

            // 初始化WebView2
            await InitializeWebView();
        }

        private void RegisterHotkey()
        {
            if (_source?.Handle != null && _source.Handle != IntPtr.Zero)
            {
                // 先注销之前的热键
                UnregisterHotKey(_source.Handle, HOTKEY_ID);
                // 注册新的热键
                RegisterHotKey(_source.Handle, HOTKEY_ID, _settings.HotkeyModifiers, _settings.HotkeyVirtualKey);
            }
        }

        /// <summary>
        /// 临时注销快捷键（用于测试）
        /// </summary>
        public bool TemporarilyUnregisterHotkey()
        {
            if (_source?.Handle != null && _source.Handle != IntPtr.Zero)
            {
                UnregisterHotKey(_source.Handle, HOTKEY_ID);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 恢复快捷键注册
        /// </summary>
        public void RestoreHotkeyRegistration()
        {
            RegisterHotkey();
        }

        /// <summary>
        /// 刷新表情数据（供设置窗口调用）
        /// </summary>
        public async Task RefreshEmojiData()
        {
            await LoadEmojiData();
        }

        private async Task InitializeWebView()
        {
            await webView.EnsureCoreWebView2Async();

            // 设置WebView2选项
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            webView.CoreWebView2.Settings.IsScriptEnabled = true;

            // 设置虚拟主机映射以访问本地文件
            await SetupVirtualHostMapping();

            // 注册JavaScript交互
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // 加载HTML内容
            var htmlContent = await GetHtmlContent();
            webView.NavigateToString(htmlContent);

            // 等待页面加载完成后加载表情数据
            webView.NavigationCompleted += async (_, e) =>
            {
                if (e.IsSuccess)
                {
                    await LoadEmojiData();
                    await UpdatePinnedState();
                }
            };
        }

        /// <summary>
        /// 设置WebView2的虚拟主机映射
        /// </summary>
        private async Task SetupVirtualHostMapping()
        {
            if (webView?.CoreWebView2 == null)
                return;

            try
            {
                // 先尝试清除现有的虚拟主机映射
                try
                {
                    webView.CoreWebView2.ClearVirtualHostNameToFolderMapping("local.images");
                }
                catch
                {
                    // 忽略清除失败的错误（可能映射不存在）
                }

                // 确保表情包路径存在
                var emojiPath = _settings.EmojiBasePath;
                if (string.IsNullOrEmpty(emojiPath))
                {
                    // 使用默认路径
                    emojiPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "表情包");
                    _settings.EmojiBasePath = emojiPath;
                }

                // 如果路径不存在，尝试创建
                if (!Directory.Exists(emojiPath))
                {
                    try
                    {
                        Directory.CreateDirectory(emojiPath);
                    }
                    catch
                    {
                        // 如果无法创建，显示提示并使用临时目录
                        await ShowToast("无法访问表情包目录，请检查路径设置", ToastType.Error);
                        emojiPath = Path.GetTempPath();
                    }
                }

                // 规范化路径（确保是绝对路径）
                emojiPath = Path.GetFullPath(emojiPath);

                // 设置新的虚拟主机映射
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "local.images",
                    emojiPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                // 等待映射设置生效
                await Task.Delay(100);
                
                Console.WriteLine($"Virtual host mapping set: local.images -> {emojiPath}");
            }
            catch (Exception ex)
            {
                await ShowToast($"设置文件访问权限失败: {ex.Message}", ToastType.Error);
                Console.WriteLine($"SetupVirtualHostMapping failed: {ex}");
            }
        }

        private static async Task<string> GetHtmlContent()
        {
            // 尝试从同目录下加载HTML文件
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmojiManager.html");
            if (File.Exists(htmlPath))
            {
                return await File.ReadAllTextAsync(htmlPath);
            }

            // 如果文件不存在，返回内嵌的HTML
            return GetEmbeddedHtml();
        }

        private async Task LoadEmojiData()
        {
            // 清理无效的最近表情
            _settings.CleanupRecentEmojis();
            
            var emojiData = ScanEmojiDirectory(_settings.EmojiBasePath);
            
            // 构建最近表情文件夹
            var recentFolder = new EmojiFolder
            {
                Name = "最近使用",
                Path = "",
                Images = [.. _settings.RecentEmojis],
                Children = []
            };
            
            // 将最近表情插入到文件夹列表的最前面（只有当有最近表情时）
            var allFolders = new List<EmojiFolder>();
            if (recentFolder.Images.Count > 0)
            {
                allFolders.Add(recentFolder);
            }
            allFolders.AddRange(emojiData);
            
            var dataObject = new
            {
                folders = allFolders,
                basePath = _settings.EmojiBasePath,
                recentLimit = _settings.RecentEmojisLimit
            };
            var json = JsonSerializer.Serialize(dataObject);
            await webView.CoreWebView2.ExecuteScriptAsync($"loadEmojiData({json})");
        }

        private List<EmojiFolder> ScanEmojiDirectory(string path)
        {
            var result = new List<EmojiFolder>();

            if (!Directory.Exists(path))
                return result;

            try
            {
                var directories = Directory.GetDirectories(path);
                foreach (var dir in directories)
                {
                    var folder = new EmojiFolder
                    {
                        Name = Path.GetFileName(dir),
                        Path = dir,
                        Images = GetImages(dir),
                        Children = ScanEmojiDirectory(dir)
                    };

                    if (folder.Images.Count > 0 || folder.Children.Count > 0)
                    {
                        result.Add(folder);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略无权限访问的文件夹
            }

            return result;
        }

        private List<string> GetImages(string path)
        {
            try
            {
                var validImages = new List<string>();
                var supportedExtensions = ImageFormatDetector.GetSupportedExtensions();
                
                // 首先按扩展名筛选已知的图片文件
                var extensionsWithDot = supportedExtensions.Select(ext => "." + ext).ToArray();
                var filesByExtension = Directory.GetFiles(path)
                    .Where(f => extensionsWithDot.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .ToList();
                
                validImages.AddRange(filesByExtension);
                
                // 然后检查那些可能被QQNT错误命名的文件（如.null, 无扩展名等）
                var suspiciousFiles = Directory.GetFiles(path)
                    .Where(f => 
                    {
                        var ext = Path.GetExtension(f).ToLower();
                        return ext == ".null" || string.IsNullOrEmpty(ext) || 
                               (!extensionsWithDot.Contains(ext, StringComparer.OrdinalIgnoreCase) && 
                                !IsCommonNonImageExtension(ext));
                    })
                    .ToList();
                
                // 对可疑文件进行格式检测
                foreach (var file in suspiciousFiles)
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        if (ImageFormatDetector.DetectImageFormat(bytes) != null)
                        {
                            validImages.Add(file);
                        }
                    }
                    catch
                    {
                        // 忽略无法读取的文件
                    }
                }
                
                // 根据设置排序图片
                if (_settings.SortImagesByTime)
                {
                    // 按创建时间排序（从最新到最老）
                    validImages = [.. validImages.OrderByDescending(file =>
                    {
                        try
                        {
                            return File.GetCreationTime(file);
                        }
                        catch
                        {
                            return DateTime.MinValue; // 无法获取时间的文件排在最后
                        }
                    })];
                }
                else
                {
                    // 按文件名排序（默认行为）
                    validImages.Sort(StringComparer.OrdinalIgnoreCase);
                }
                
                return validImages;
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// 检查是否为常见的非图片扩展名
        /// </summary>
        private static bool IsCommonNonImageExtension(string extension)
        {
            var nonImageExtensions = new[]
            {
                ".txt", ".doc", ".docx", ".pdf", ".zip", ".rar", ".exe", ".dll",
                ".mp3", ".mp4", ".avi", ".mov", ".mkv", ".wav", ".flac",
                ".json", ".xml", ".html", ".css", ".js", ".cs", ".cpp", ".h"
            };
            
            return nonImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 修正指定目录下所有图片文件的扩展名
        /// </summary>
        /// <param name="rootPath">要处理的根目录路径</param>
        /// <returns>修正结果统计</returns>
        public static async Task<(int corrected, int skipped, int errors)> CorrectImageExtensions(string rootPath)
        {
            var correctedCount = 0;
            var skippedCount = 0;
            var errorCount = 0;

            try
            {
                await Task.Run(() =>
                {
                    ProcessDirectory(rootPath, ref correctedCount, ref skippedCount, ref errorCount);
                });
            }
            catch
            {
                errorCount++;
            }

            return (correctedCount, skippedCount, errorCount);

            static void ProcessDirectory(string directory, ref int corrected, ref int skipped, ref int errors)
            {
                try
                {
                    // 处理当前目录的文件
                    var files = Directory.GetFiles(directory);
                    foreach (var file in files)
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(file);
                            var actualFormat = ImageFormatDetector.DetectImageFormat(bytes);
                            
                            if (actualFormat != null)
                            {
                                var currentExt = Path.GetExtension(file).TrimStart('.').ToLower();
                                if (currentExt != actualFormat && currentExt != "null") // 不处理.null文件，让拖拽功能处理
                                {
                                    var fileDirectory = Path.GetDirectoryName(file)!;
                                    var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                                    var newFileName = $"{nameWithoutExt}.{actualFormat}";
                                    var newFilePath = Path.Combine(fileDirectory, newFileName);
                                    
                                    if (File.Exists(newFilePath))
                                    {
                                        // 如果目标文件已存在，删除原文件
                                        File.Delete(file);
                                        skipped++;
                                    }
                                    else
                                    {
                                        // 重命名文件
                                        File.Move(file, newFilePath);
                                        corrected++;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            errors++;
                        }
                    }

                    // 递归处理子目录
                    var subdirectories = Directory.GetDirectories(directory);
                    foreach (var subdirectory in subdirectories)
                    {
                        ProcessDirectory(subdirectory, ref corrected, ref skipped, ref errors);
                    }
                }
                catch
                {
                    errors++;
                }
            }
        }

        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                var data = JsonSerializer.Deserialize<WebMessage>(message, _jsonOptions);

                switch (data?.Type)
                {
                    case "copyImage":
                        await CopyImageToClipboard(data.Path);
                        
                        // 记录到最近使用表情
                        _settings.AddRecentEmoji(data.Path);
                        
                        // 立即刷新表情数据以更新最近表情列表
                        await LoadEmojiData();
                        
                        _shouldPasteAfterDeactivate = true; // 设置粘贴标志
                        if (!_isPinned)
                        {
                            HideWindow();
                        }
                        else
                        {
                            // 即使钉住也要还原焦点并粘贴
                            RestoreFocusAndPaste();
                            _shouldPasteAfterDeactivate = false; // 立即重置标志
                        }
                        break;

                    case "hideWindow":
                        HideWindow();
                        break;

                    case "togglePin":
                        TogglePin();
                        break;

                    case "dropFiles":
                        await HandleDropFiles(data.Files, data.TargetPath);
                        break;

                    case "openLocation":
                        OpenFileLocation(data.Path);
                        break;

                    case "deleteImage":
                        await DeleteImageFile(data.Path);
                        break;

                    case "openSettings":
                        OpenSettingsWindow();
                        break;
                }
            }
            catch (Exception ex)
            {
                await ShowToast($"错误: {ex.Message}", ToastType.Error);
            }
        }

        private async Task ShowToast(string message, ToastType type)
        {
            var toastTypeStr = type switch
            {
                ToastType.Success => "success",
                ToastType.Error => "error",
                _ => "info"
            };

            await webView.CoreWebView2.ExecuteScriptAsync(
                $"handleMessage({{type: 'showToast', text: '{message.Replace("'", "\\'")}', toastType: '{toastTypeStr}'}})");
        }

        [GeneratedRegex(@"^[a-fA-F0-9]{32}$")]
        private static partial Regex Md5FileNameRegex();

        private static bool IsMd5FileName(string fileName)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            return Md5FileNameRegex().IsMatch(nameWithoutExt);
        }

        private async Task HandleDropFiles(List<FileData>? files, string targetPath)
        {
            if (files == null || files.Count == 0)
                return;

            var successCount = 0;
            var skippedCount = 0;
            var renamedCount = 0;
            var formatCorrectedCount = 0;
            var invalidFileCount = 0;

            try
            {
                foreach (var fileData in files)
                {
                    if (string.IsNullOrEmpty(fileData.Name) || fileData.Content == null)
                        continue;

                    // 将Base64内容解码为字节数组
                    var bytes = Convert.FromBase64String(fileData.Content);
                    
                    // 检测文件的实际图像格式
                    var actualFormat = ImageFormatDetector.DetectImageFormat(bytes);
                    if (actualFormat == null)
                    {
                        // 不是有效的图像文件，跳过
                        invalidFileCount++;
                        continue;
                    }

                    // 获取原始文件名（不含扩展名）
                    var originalNameWithoutExt = Path.GetFileNameWithoutExtension(fileData.Name);
                    var originalExt = Path.GetExtension(fileData.Name).TrimStart('.').ToLower();
                    
                    // 确定最终的文件名（使用正确的扩展名）
                    var finalFileName = $"{originalNameWithoutExt}.{actualFormat}";
                    var destPath = Path.Combine(targetPath, finalFileName);

                    // 记录是否进行了格式修正
                    var isFormatCorrected = !string.IsNullOrEmpty(originalExt) && 
                                          originalExt != actualFormat && 
                                          originalExt != "null"; // QQNT可能生成.null文件

                    // 检查文件是否已存在（使用正确的扩展名）
                    if (File.Exists(destPath))
                    {
                        // 如果是MD5文件名且文件已存在，跳过
                        if (IsMd5FileName(originalNameWithoutExt))
                        {
                            skippedCount++;
                            continue;
                        }

                        // 非MD5文件名，添加数字后缀
                        var counter = 1;
                        while (File.Exists(destPath))
                        {
                            destPath = Path.Combine(targetPath, $"{originalNameWithoutExt}_{counter}.{actualFormat}");
                            counter++;
                        }
                        renamedCount++;
                    }

                    // 写入文件
                    await File.WriteAllBytesAsync(destPath, bytes);
                    successCount++;
                    
                    if (isFormatCorrected)
                        formatCorrectedCount++;
                }

                // 构建提示信息
                var messages = new List<string>();
                if (successCount > 0) messages.Add($"{successCount} 个文件");
                if (formatCorrectedCount > 0) messages.Add($"{formatCorrectedCount} 个格式修正");
                if (skippedCount > 0) messages.Add($"{skippedCount} 个重复");
                if (renamedCount > 0) messages.Add($"{renamedCount} 个重命名");
                if (invalidFileCount > 0) messages.Add($"{invalidFileCount} 个无效");

                var message = messages.Count > 0
                    ? $"添加完成：{string.Join("，", messages)}"
                    : "没有添加任何文件";

                await ShowToast(message, successCount > 0 ? ToastType.Success : ToastType.Info);
            }
            catch (Exception ex)
            {
                await ShowToast($"添加失败: {ex.Message}", ToastType.Error);
            }
        }

        private void TogglePin()
        {
            _isPinned = !_isPinned;
            _settings.IsPinned = _isPinned;
            Topmost = _isPinned; // 根据钉住状态设置窗口置顶
            _ = UpdatePinnedState();
            SaveWindowState(); // 保存状态
        }

        private async Task UpdatePinnedState()
        {
            await webView.CoreWebView2.ExecuteScriptAsync($"updatePinnedState({_isPinned.ToString().ToLower()})");
        }

        private async Task CopyImageToClipboard(string imagePath)
        {
            try
            {
                var dataObject = new DataObject();
                var fileList = new System.Collections.Specialized.StringCollection { imagePath };
                dataObject.SetFileDropList(fileList);
                
                // 图像模式：同时设置文件列表和图像数据，确保最佳兼容性
                try
                {
                    using var image = System.Drawing.Image.FromFile(imagePath);
                    var bitmapSource = ConvertDrawingImageToBitmapSource(image);
                    if (bitmapSource != null)
                    {
                        dataObject.SetImage(bitmapSource);
                    }
                }
                catch
                {
                    // 如果无法加载为图像，仍然使用文件列表方式
                }
                
                Clipboard.SetDataObject(dataObject, true);
            }
            catch (Exception ex)
            {
                await ShowToast($"复制失败: {ex.Message}", ToastType.Error);
            }
        }


        /// <summary>
        /// 将System.Drawing.Image转换为System.Windows.Media.Imaging.BitmapImage
        /// </summary>
        /// <param name="drawingImage">System.Drawing.Image对象</param>
        /// <returns>BitmapImage对象，转换失败时返回null</returns>
        private static System.Windows.Media.Imaging.BitmapImage? ConvertDrawingImageToBitmapSource(System.Drawing.Image drawingImage)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                // 将Image保存到内存流中
                drawingImage.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Position = 0;

                // 从内存流创建BitmapImage
                var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // 使其线程安全

                return bitmapImage;
            }
            catch
            {
                return null;
            }
        }

        private void RestoreFocusAndPaste()
        {
            if (_lastActiveWindow != IntPtr.Zero)
            {
                // 还原焦点到之前的窗口
                SetForegroundWindow(_lastActiveWindow);

                // 检查是否是QQ窗口
                if (IsQQWindow(_lastActiveWindow))
                {
                    // 延迟一下确保焦点切换完成
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // 发送 Ctrl+V
                            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        });
                    });
                }
            }
        }

        /// <summary>
        /// 在资源管理器中打开文件位置
        /// </summary>
        /// <param name="filePath">文件路径</param>
        private void OpenFileLocation(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _ = ShowToast("文件不存在", ToastType.Error);
                    return;
                }

                // 使用explorer.exe的/select参数来选中文件
                // 这个方法兼容大多数第三方文件管理器，因为它们通常会接管这个命令
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                // 如果explorer.exe失败，尝试直接打开包含目录
                try
                {
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = directory,
                            UseShellExecute = true
                        });
                    }
                }
                catch
                {
                    _ = ShowToast($"无法打开文件位置: {ex.Message}", ToastType.Error);
                }
            }
        }

        /// <summary>
        /// 删除图片文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        private async Task DeleteImageFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    await ShowToast("文件不存在", ToastType.Error);
                    return;
                }

                // 获取文件名用于确认对话框
                var fileName = Path.GetFileName(filePath);
                
                // 显示确认对话框
                var result = MessageBox.Show(
                    $"确定要删除这个表情吗？\n\n文件: {fileName}\n\n此操作不可撤销。", 
                    "确认删除", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning,
                    MessageBoxResult.No); // 默认选择"否"，更安全

                if (result != MessageBoxResult.Yes)
                {
                    return; // 用户取消删除
                }

                // 删除文件
                File.Delete(filePath);
                
                // 从最近使用列表中移除（如果存在）
                _settings.RemoveRecentEmoji(filePath);
                _settings.Save();

                // 刷新表情数据
                await LoadEmojiData();
                
                await ShowToast("文件已删除", ToastType.Success);
            }
            catch (Exception ex)
            {
                await ShowToast($"删除失败: {ex.Message}", ToastType.Error);
            }
        }

        private static bool IsQQWindow(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out var processId);
                var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName.ToLower();

                // 检查各种QQ相关进程
                return processName.Contains("qq") &&
                       !processName.Contains("qqmusic") &&
                       !processName.Contains("qqbrowser");
            }
            catch
            {
                return false;
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (_isVisible)
                {
                    HideWindow();
                }
                else
                {
                    ShowWindow();
                }
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void ShowWindow()
        {
            _lastActiveWindow = GetForegroundWindow();
            Show();
            Activate();
            webView.Focus();
            _isVisible = true;
        }

        private void ShowWindowFromTray()
        {
            // 从托盘显示窗口，不重新获取前台窗口（已在点击事件中获取）
            Show();
            Activate();
            webView.Focus();
            _isVisible = true;
        }

        private void HideWindow()
        {
            Hide();
            _isVisible = false;
            
            // 如果是点击表情后隐藏窗口，执行粘贴操作
            if (_shouldPasteAfterDeactivate)
            {
                RestoreFocusAndPaste();
                _shouldPasteAfterDeactivate = false; // 重置标志
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 如果钉住了，不自动隐藏
            if (_isPinned)
                return;

            // 延迟一下再隐藏，避免点击时立即隐藏
            Task.Delay(100).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isVisible && !IsActive && !_isPinned)
                    {
                        HideWindow();
                    }
                });
            });
        }

        private void OpenSettingsWindow()
        {
            var settingsWindow = new SettingsWindow(_settings)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                // 获取更新后的设置
                _settings = settingsWindow.GetSettings();
                
                // 应用新设置
                ApplySettings();
            }
        }

        private void ApplySettings()
        {
            // 重新初始化文件监听器
            _fileWatcher?.Dispose();
            _fileWatcher = null;
            InitializeFileWatcher();

            // 重新注册热键
            RegisterHotkey();

            // 重新设置虚拟主机映射和加载数据
            Task.Run(async () =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    // 清除WebView2缓存并重新加载
                    await RefreshWebViewWithNewPath();
                });
            });
        }

        /// <summary>
        /// 刷新WebView2并使用新的路径设置
        /// </summary>
        private async Task RefreshWebViewWithNewPath()
        {
            if (webView?.CoreWebView2 == null)
                return;

            try
            {
                // 清除缓存（可选，但有助于确保干净的状态）
                await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
            }
            catch
            {
                // 忽略清除缓存失败的错误
            }

            try
            {
                // 重新设置虚拟主机映射
                await SetupVirtualHostMapping();
                
                // 重新加载HTML内容以确保使用新的映射
                var htmlContent = await GetHtmlContent();
                webView.NavigateToString(htmlContent);
                
                // 等待页面加载完成后重新加载表情数据
                // 使用一次性事件处理器
                void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    webView.NavigationCompleted -= OnNavigationCompleted;
                    if (e.IsSuccess)
                    {
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await LoadEmojiData();
                            await UpdatePinnedState();
                        });
                    }
                }
                
                webView.NavigationCompleted += OnNavigationCompleted;
            }
            catch (Exception ex)
            {
                await ShowToast($"刷新失败: {ex.Message}", ToastType.Error);
                
                // 如果刷新失败，至少尝试重新加载数据
                await Task.Delay(200);
                await LoadEmojiData();
            }
        }

        private void SaveWindowState()
        {
            try
            {
                // 总是保存窗口状态
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
                _settings.WindowState = WindowState;
                _settings.Save();
            }
            catch
            {
                // 如果保存失败，忽略错误
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 取消关闭事件，改为隐藏窗口
            e.Cancel = true;
            HideWindow();
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            SaveWindowState();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SaveWindowState();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 仅隐藏窗口，不退出程序
            HideWindow();
        }

        private void ExitApplication()
        {
            var result = MessageBox.Show(
                this, // 指定父窗口
                "确定要退出表情管理器吗？\n程序将完全关闭，需要手动重新启动。", 
                "确认退出", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question,
                MessageBoxResult.No); // 默认选择"否"
                
            if (result == MessageBoxResult.Yes)
            {
                // 清理资源
                CleanupResources();
                
                // 退出应用程序
                Application.Current.Shutdown();
            }
        }

        private void CleanupResources()
        {
            try
            {
                // 保存窗口状态
                SaveWindowState();

                // 注销热键
                if (_source != null)
                {
                    UnregisterHotKey(_source.Handle, HOTKEY_ID);
                }

                // 释放文件监听器
                _fileWatcher?.Dispose();
                
                // 释放托盘图标
                _taskbarIcon?.Dispose();
                
                // 停止前台窗口追踪
                _foregroundWindowTracker?.Stop();
                _foregroundWindowTracker = null;
            }
            catch
            {
                // 忽略清理过程中的错误
            }
        }

        private static string GetEmbeddedHtml()
        {
            // 作为后备，保留一个最小化的内嵌HTML
            return """
                   <!DOCTYPE html>
                   <html>
                   <head>
                       <meta charset='utf-8'>
                       <style>
                           body {
                               font-family: 'Microsoft YaHei', Arial, sans-serif;
                               background: #1e1e1e;
                               color: #e0e0e0;
                               display: flex;
                               align-items: center;
                               justify-content: center;
                               height: 100vh;
                               margin: 0;
                           }
                       </style>
                   </head>
                   <body>
                       <div>请确保 EmojiManager.html 文件存在于程序目录中</div>
                   </body>
                   </html>
                   """;
        }
    }

    public class EmojiFolder
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<string> Images { get; set; } = [];
        public List<EmojiFolder> Children { get; set; } = [];
    }

    public class WebMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public List<FileData> Files { get; set; } = [];
        public string TargetPath { get; set; } = string.Empty;
    }

    public class FileData
    {
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty; // Base64编码的文件内容
    }

    public enum ToastType
    {
        Success,
        Error,
        Info
    }
}