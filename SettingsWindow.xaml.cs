using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using MessageBox = System.Windows.MessageBox;

namespace EmojiManager
{
    public partial class SettingsWindow : Window
    {
        private readonly Settings _settings = null!;
        private bool _isCapturingHotkey = false;
        private HwndSource? _hwndSource;
        private const int HOTKEY_TEST_ID = 9001;

        // Windows API 用于快捷键注册
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

        public SettingsWindow(Settings settings)
        {
            InitializeComponent();
            _settings = settings ?? new Settings();
            LoadSettings();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource.AddHook(HwndHook);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
            {
                UnregisterHotKey(_hwndSource.Handle, HOTKEY_TEST_ID);
                _hwndSource.RemoveHook(HwndHook);
            }
            base.OnClosed(e);
        }

        private void LoadSettings()
        {
            txtEmojiPath.Text = _settings.EmojiBasePath;
            txtHotkey.Text = _settings.HotkeyDisplayName;
            txtRecentLimit.Text = _settings.RecentEmojisLimit.ToString();
            
            UpdateHotkeyStatus("当前快捷键: " + _settings.HotkeyDisplayName, false);
        }

        private void BrowseEmojiPath_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "选择表情包根目录";
            dialog.SelectedPath = txtEmojiPath.Text;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtEmojiPath.Text = dialog.SelectedPath;
            }
        }

        private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (_isCapturingHotkey)
            {
                StopCapturingHotkey();
            }
            else
            {
                StartCapturingHotkey();
            }
        }

        private void StartCapturingHotkey()
        {
            _isCapturingHotkey = true;
            btnCaptureHotkey.Content = "停止录制";
            txtHotkey.Text = "请按下快捷键...";
            UpdateHotkeyStatus("正在录制快捷键，请按下您要设置的组合键", false);
            
            // 监听所有键盘输入
            this.KeyDown += OnHotkeyCapture;
            this.Focus();
        }

        private void StopCapturingHotkey()
        {
            _isCapturingHotkey = false;
            btnCaptureHotkey.Content = "录制快捷键";
            this.KeyDown -= OnHotkeyCapture;
        }

        private void OnHotkeyCapture(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isCapturingHotkey) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            
            // 忽略单独的修饰键
            if (IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            var modifiers = Keyboard.Modifiers;

            // 转换为Windows API格式
            var winModifiers = GetWindowsModifiers(modifiers);
            var virtualKey = GetVirtualKey(key);

            if (virtualKey == 0) return;

            var displayName = GetHotkeyDisplayName(modifiers, key);
            
            // 测试快捷键是否可以注册
            if (TestHotkeyRegistration(winModifiers, virtualKey))
            {
                _settings.HotkeyModifiers = winModifiers;
                _settings.HotkeyVirtualKey = virtualKey;
                _settings.HotkeyDisplayName = displayName;
                
                txtHotkey.Text = displayName;
                UpdateHotkeyStatus($"快捷键 {displayName} 设置成功", false);
            }
            else
            {
                UpdateHotkeyStatus($"快捷键 {displayName} 已被其他程序占用，请选择其他组合键", true);
            }

            StopCapturingHotkey();
            e.Handled = true;
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

        private bool TestHotkeyRegistration(uint modifiers, uint virtualKey)
        {
            if (_hwndSource?.Handle == null || _hwndSource.Handle == IntPtr.Zero) return false;

            // 先注销之前的测试快捷键
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_TEST_ID);
            
            // 尝试注册新的快捷键
            var success = RegisterHotKey(_hwndSource.Handle, HOTKEY_TEST_ID, modifiers, virtualKey);
            
            if (success)
            {
                // 注册成功后立即注销
                UnregisterHotKey(_hwndSource.Handle, HOTKEY_TEST_ID);
            }
            
            return success;
        }

        private void ResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            _settings.HotkeyModifiers = 0x0000; // MOD_NONE
            _settings.HotkeyVirtualKey = 0x7B;  // VK_F12
            _settings.HotkeyDisplayName = "F12";
            
            txtHotkey.Text = "F12";
            UpdateHotkeyStatus("已重置为默认快捷键 F12", false);
        }

        private void TestHotkey_Click(object sender, RoutedEventArgs e)
        {
            // 通知主窗口临时注销快捷键以进行测试
            var mainWindow = Owner as MainWindow;
            var wasRegistered = false;
            
            if (mainWindow != null)
            {
                wasRegistered = mainWindow.TemporarilyUnregisterHotkey();
            }

            try
            {
                if (TestHotkeyRegistration(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey))
                {
                    UpdateHotkeyStatus($"快捷键 {_settings.HotkeyDisplayName} 可以正常使用", false);
                }
                else
                {
                    UpdateHotkeyStatus($"快捷键 {_settings.HotkeyDisplayName} 已被占用或无效", true);
                }
            }
            finally
            {
                // 恢复主窗口的快捷键注册
                if (mainWindow != null && wasRegistered)
                {
                    mainWindow.RestoreHotkeyRegistration();
                }
            }
        }

        private void UpdateHotkeyStatus(string message, bool isError)
        {
            txtHotkeyStatus.Text = message;
            txtHotkeyStatus.Foreground = isError ? 
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red) :
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
        }

        private static uint GetWindowsModifiers(ModifierKeys modifiers)
        {
            uint result = 0;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= 0x0002; // MOD_CONTROL
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= 0x0001;     // MOD_ALT
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= 0x0004;   // MOD_SHIFT
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= 0x0008; // MOD_WIN
            return result;
        }

        private static uint GetVirtualKey(Key key)
        {
            return (uint)KeyInterop.VirtualKeyFromKey(key);
        }

        private static string GetHotkeyDisplayName(ModifierKeys modifiers, Key key)
        {
            var parts = new System.Collections.Generic.List<string>();
            
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            
            // 优化按键显示名称
            var keyName = GetFriendlyKeyName(key);
            parts.Add(keyName);
            
            return string.Join(" + ", parts);
        }

        private static string GetFriendlyKeyName(Key key)
        {
            return key switch
            {
                // 主键盘数字键优化显示
                Key.D0 => "0",
                Key.D1 => "1",
                Key.D2 => "2",
                Key.D3 => "3",
                Key.D4 => "4",
                Key.D5 => "5",
                Key.D6 => "6",
                Key.D7 => "7",
                Key.D8 => "8",
                Key.D9 => "9",
                
                // 小键盘数字键
                Key.NumPad0 => "小键盘0",
                Key.NumPad1 => "小键盘1",
                Key.NumPad2 => "小键盘2",
                Key.NumPad3 => "小键盘3",
                Key.NumPad4 => "小键盘4",
                Key.NumPad5 => "小键盘5",
                Key.NumPad6 => "小键盘6",
                Key.NumPad7 => "小键盘7",
                Key.NumPad8 => "小键盘8",
                Key.NumPad9 => "小键盘9",
                
                // 其他特殊键优化
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemBackslash => "\\",
                Key.OemMinus => "-",
                Key.OemPlus => "=",
                Key.OemTilde => "`",
                
                // 空格键
                Key.Space => "空格",
                
                // 方向键
                Key.Up => "↑",
                Key.Down => "↓",
                Key.Left => "←",
                Key.Right => "→",
                
                // 默认使用原始名称
                _ => key.ToString()
            };
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_TEST_ID)
            {
                // 测试快捷键被触发
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // 验证表情包路径
            if (string.IsNullOrWhiteSpace(txtEmojiPath.Text))
            {
                MessageBox.Show("请选择表情包路径", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(txtEmojiPath.Text))
            {
                MessageBox.Show("指定的表情包路径不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 验证最近表情数量限制
            if (!int.TryParse(txtRecentLimit.Text, out int recentLimit) || recentLimit < 0 || recentLimit > 100)
            {
                MessageBox.Show("最近表情数量限制必须是0-100之间的整数（0表示关闭功能）", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtRecentLimit.Focus();
                return;
            }

            // 保存设置
            _settings.EmojiBasePath = txtEmojiPath.Text;
            _settings.RecentEmojisLimit = recentLimit;

            // 如果限制数量减少了，需要裁剪现有的最近表情列表
            while (_settings.RecentEmojis.Count > _settings.RecentEmojisLimit)
            {
                _settings.RecentEmojis.RemoveAt(_settings.RecentEmojis.Count - 1);
            }

            try
            {
                _settings.Save();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async void ClearRecentEmojis_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要清空所有最近使用的表情记录吗？", "确认", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _settings.RecentEmojis.Clear();
                _settings.Save();
                
                // 通知主窗口刷新表情数据
                if (Owner is MainWindow mainWindow)
                {
                    await mainWindow.RefreshEmojiData();
                }
                
                MessageBox.Show("最近使用表情记录已清空", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public Settings GetSettings()
        {
            return _settings;
        }
    }
} 