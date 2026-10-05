using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using DFRContentHost.Interop;
using DFRContentHost.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Windows.Media.Control;
using WindowsInput.Native;

namespace DFRContentHost.ViewModels
{
    public class SystemMediaTransportControlViewModel : ReactiveObject
    {
        private GlobalSystemMediaTransportControlsSession _glbSmtcSession;
        private GlobalSystemMediaTransportControlsSessionManager _glbSmtcMgr;
        private FunctionRowButtonModel _playPauseButton;

        // ─── Media ──────────────────────────────────────────────────────────

        private string _mediaTitle;
        public string MediaTitle
        {
            get => _mediaTitle;
            set => this.RaiseAndSetIfChanged(ref _mediaTitle, value);
        }

        private string _mediaArtist;
        public string MediaArtist
        {
            get => _mediaArtist;
            set => this.RaiseAndSetIfChanged(ref _mediaArtist, value);
        }

        private Bitmap _bitmap;
        public Bitmap MediaThumbnail
        {
            get => _bitmap;
            set => this.RaiseAndSetIfChanged(ref _bitmap, value);
        }

        private bool _thumbnailAvailable;
        public bool ThumbnailAvailable
        {
            get => _thumbnailAvailable;
            set => this.RaiseAndSetIfChanged(ref _thumbnailAvailable, value);
        }

        private bool _isSmtcActive;
        public bool IsSmtcActive
        {
            get => _isSmtcActive;
            set => this.RaiseAndSetIfChanged(ref _isSmtcActive, value);
        }

        // ─── Controls ───────────────────────────────────────────────────────

        public FunctionRowButtonModel VolumeButton { get; }
        public FunctionRowButtonModel BrightnessButton { get; }
        public FunctionRowButtonModel LockButton { get; }
        public FunctionRowButtonModel StartButton { get; }
        public ObservableCollection<FunctionRowButtonModel> ContextButtons { get; }
        public ObservableCollection<FunctionRowButtonModel> PlayControlKeys { get; }

        private bool _isAppContextActive;
        public bool IsAppContextActive
        {
            get => _isAppContextActive;
            private set => this.RaiseAndSetIfChanged(ref _isAppContextActive, value);
        }

        // ─── Volume panel visibility ─────────────────────────────────────────
        // Now this is a button row, not an Avalonia Slider.
        private bool _isVolumePanelVisible;
        public bool IsVolumeSliderVisible           // keep same name for XAML compat
        {
            get => _isVolumePanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isVolumePanelVisible, value);
        }

        private bool _isBrightnessPanelVisible;
        public bool IsBrightnessSliderVisible
        {
            get => _isBrightnessPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isBrightnessPanelVisible, value);
        }

        private double _volumeLevel;
        private DispatcherTimer _volumeUpdateTimer;
        private const double VolumeSliderThumbWidth = 20;
        private const double VolumeSliderTravelWidth = 260;
        public double VolumeLevel
        {
            get => _volumeLevel;
            set
            {
                var clamped = Math.Max(0, Math.Min(100, value));
                if (Math.Abs(_volumeLevel - clamped) < 0.01)
                    return;

                this.RaiseAndSetIfChanged(ref _volumeLevel, clamped);
                this.RaisePropertyChanged(nameof(VolumeSliderThumbLeft));
                this.RaisePropertyChanged(nameof(VolumeSliderFillWidth));
                this.RaisePropertyChanged(nameof(VolumePercentageText));
                _volumeUpdateTimer.Stop();
                _volumeUpdateTimer.Start();
            }
        }

        public double VolumeSliderThumbLeft => VolumeSliderTravelWidth * VolumeLevel / 100;
        public double VolumeSliderFillWidth => VolumeSliderThumbLeft + VolumeSliderThumbWidth / 2;
        public string VolumePercentageText => $"{(int)Math.Round(VolumeLevel)}%";

        private double _brightnessLevel;
        private DispatcherTimer _brightnessUpdateTimer;
        public double BrightnessLevel
        {
            get => _brightnessLevel;
            set
            {
                var clamped = Math.Max(0, Math.Min(100, value));
                if (Math.Abs(_brightnessLevel - clamped) < 0.01)
                    return;

                this.RaiseAndSetIfChanged(ref _brightnessLevel, clamped);
                this.RaisePropertyChanged(nameof(BrightnessSliderThumbLeft));
                this.RaisePropertyChanged(nameof(BrightnessSliderFillWidth));
                this.RaisePropertyChanged(nameof(BrightnessPercentageText));
                _brightnessUpdateTimer.Stop();
                _brightnessUpdateTimer.Start();
            }
        }

        public double BrightnessSliderThumbLeft => 260 * BrightnessLevel / 100;
        public double BrightnessSliderFillWidth => BrightnessSliderThumbLeft + 10;
        public string BrightnessPercentageText => $"{(int)Math.Round(BrightnessLevel)}%";

        // ─── Status ──────────────────────────────────────────────────────────

        private string _clockText;
        public string ClockText
        {
            get => _clockText;
            private set => this.RaiseAndSetIfChanged(ref _clockText, value);
        }

        private string _batteryText;
        public string BatteryText
        {
            get => _batteryText;
            private set => this.RaiseAndSetIfChanged(ref _batteryText, value);
        }

        private int _wifiRefreshTicks;
        private string _contextAppKey;

        private int _wifiSignalBars;
        public int WifiSignalBars
        {
            get => _wifiSignalBars;
            private set
            {
                this.RaiseAndSetIfChanged(ref _wifiSignalBars, value);
                this.RaisePropertyChanged(nameof(WifiBar1Opacity));
                this.RaisePropertyChanged(nameof(WifiBar2Opacity));
                this.RaisePropertyChanged(nameof(WifiBar3Opacity));
                this.RaisePropertyChanged(nameof(WifiBar4Opacity));
            }
        }

        public double WifiBar1Opacity => WifiSignalBars >= 1 ? 1 : 0.25;
        public double WifiBar2Opacity => WifiSignalBars >= 2 ? 1 : 0.25;
        public double WifiBar3Opacity => WifiSignalBars >= 3 ? 1 : 0.25;
        public double WifiBar4Opacity => WifiSignalBars >= 4 ? 1 : 0.25;

        private double _batteryLevel;
        public double BatteryLevel
        {
            get => _batteryLevel;
            private set
            {
                this.RaiseAndSetIfChanged(ref _batteryLevel, value);
                BatteryFillWidth = value / 100.0 * 32.0;
            }
        }

        private double _batteryFillWidth;
        public double BatteryFillWidth
        {
            get => _batteryFillWidth;
            private set => this.RaiseAndSetIfChanged(ref _batteryFillWidth, value);
        }

        private IBrush _batteryBrush;
        public IBrush BatteryBrush
        {
            get => _batteryBrush;
            private set => this.RaiseAndSetIfChanged(ref _batteryBrush, value);
        }

        private double _batteryOpacity = 1;
        public double BatteryOpacity
        {
            get => _batteryOpacity;
            private set => this.RaiseAndSetIfChanged(ref _batteryOpacity, value);
        }

        private string _activeAppText;
        public string ActiveAppText
        {
            get => _activeAppText;
            private set => this.RaiseAndSetIfChanged(ref _activeAppText, value);
        }

        // ─── Constructor ─────────────────────────────────────────────────────

        public SystemMediaTransportControlViewModel()
        {
            _playPauseButton = new FunctionRowButtonModel("\uE102", VirtualKeyCode.MEDIA_PLAY_PAUSE, "Segoe MDL2 Assets", 32)
            {
                Enabled = false
            };

            PlayControlKeys = new ObservableCollection<FunctionRowButtonModel>
            {
                new FunctionRowButtonModel("\uE100", VirtualKeyCode.MEDIA_PREV_TRACK, "Segoe MDL2 Assets", 32),
                _playPauseButton,
                new FunctionRowButtonModel("\uE101", VirtualKeyCode.MEDIA_NEXT_TRACK, "Segoe MDL2 Assets", 32)
            };

            // Volume button toggles the +/- panel
            VolumeButton  = new FunctionRowButtonModel("\uE995", ToggleVolumePanel, "Segoe MDL2 Assets", 36);
            BrightnessButton = new FunctionRowButtonModel("\uE706", ToggleBrightnessPanel, "Segoe MDL2 Assets", 36);

            LockButton   = new FunctionRowButtonModel("\uE1F6", () => NativeMethods.LockWorkStation(), "Segoe MDL2 Assets", 36);
            StartButton  = new FunctionRowButtonModel(string.Empty, () => NativeMethods.SendWinKey(), "Segoe UI", 36);

            ContextButtons = new ObservableCollection<FunctionRowButtonModel>();

            _volumeUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _volumeUpdateTimer.Tick += (sender, args) => ApplyVolumeLevel();

            _brightnessUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _brightnessUpdateTimer.Tick += (sender, args) => ApplyBrightnessLevel();
            var currentBrightness = WindowsDisplayBrightness.GetBrightness();
            _brightnessLevel = currentBrightness < 0 ? 50 : currentBrightness;

            // Read real volume once on startup for display
            SyncVolumeDisplay();

            ActiveAppText = "Desktop";

            UpdateSystemStatus();
            var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            statusTimer.Tick += (sender, args) => UpdateSystemStatus();
            statusTimer.Start();

            ResetSmtc();
            InitializeSmtcAsync();
        }

        // ─── Volume actions ──────────────────────────────────────────────────

        /// <summary>
        /// Reads the real Windows master volume and updates the slider and icon.
        /// </summary>
        private void SyncVolumeDisplay(float? knownLevel = null)
        {
            var level = knownLevel ?? NativeAudio.GetMasterVolume();
            if (level < 0) level = 0;
            level = Math.Max(0, Math.Min(100, level));
            this.RaiseAndSetIfChanged(ref _volumeLevel, (double)level);
            this.RaisePropertyChanged(nameof(VolumeSliderThumbLeft));
            this.RaisePropertyChanged(nameof(VolumeSliderFillWidth));
            this.RaisePropertyChanged(nameof(VolumePercentageText));
            UpdateVolumeIcon(level);
        }

        private void ApplyVolumeLevel()
        {
            _volumeUpdateTimer.Stop();
            NativeAudio.SetMasterVolume((float)VolumeLevel);
            UpdateVolumeIcon((float)VolumeLevel);
        }

        private void ToggleVolumePanel()
        {
            if (!IsVolumeSliderVisible && IsBrightnessSliderVisible)
            {
                ApplyBrightnessLevel();
                IsBrightnessSliderVisible = false;
                BrightnessButton.IsActive = false;
            }

            if (IsVolumeSliderVisible)
                ApplyVolumeLevel();

            IsVolumeSliderVisible = !IsVolumeSliderVisible;
            VolumeButton.IsActive = IsVolumeSliderVisible;
        }

        private void ToggleBrightnessPanel()
        {
            if (!IsBrightnessSliderVisible && IsVolumeSliderVisible)
            {
                ApplyVolumeLevel();
                IsVolumeSliderVisible = false;
                VolumeButton.IsActive = false;
            }

            if (IsBrightnessSliderVisible)
                ApplyBrightnessLevel();

            IsBrightnessSliderVisible = !IsBrightnessSliderVisible;
            BrightnessButton.IsActive = IsBrightnessSliderVisible;
        }

        private void ApplyBrightnessLevel()
        {
            _brightnessUpdateTimer.Stop();
            var brightness = (int)Math.Round(BrightnessLevel);
            Task.Run(() => WindowsDisplayBrightness.SetBrightness(brightness));
        }

        private void UpdateVolumeIcon(float? level = null)
        {
            var v = level ?? NativeAudio.GetMasterVolume();
            if (v < 0) v = 0;
            if (v <= 0 || NativeAudio.IsMuted())  VolumeButton.Content = "\uE74F";
            else if (v < 33)                       VolumeButton.Content = "\uE993";
            else if (v < 66)                       VolumeButton.Content = "\uE994";
            else                                   VolumeButton.Content = "\uE995";
        }

        // ─── SMTC ────────────────────────────────────────────────────────────

        private void ResetSmtc()
        {
            _playPauseButton.Enabled = false;
            ThumbnailAvailable = false;
            IsSmtcActive = false;
            MediaTitle = string.Empty;
            MediaArtist = string.Empty;
        }

        // ─── 1-second status tick ─────────────────────────────────────────────

        private void UpdateSystemStatus()
        {
            ClockText = DateTime.Now.ToString("HH:mm");

            // Battery — Win32 GetSystemPowerStatus
            NativeMethods.GetPowerStatus(out var batteryPercentage, out _);
            BatteryLevel = batteryPercentage == 255 ? 0 : batteryPercentage;
            BatteryText  = batteryPercentage == 255 ? "--%" : $"{batteryPercentage}%";
            BatteryBrush = Brushes.DimGray;
            BatteryOpacity = 1;

            // Foreground process name
            var proc = NativeMethods.GetForegroundProcessName();
            var title = NativeMethods.GetForegroundWindowTitle();
            
            ActiveAppText = string.IsNullOrWhiteSpace(proc) ||
                            proc.Equals("DFRContentHost", StringComparison.OrdinalIgnoreCase)
                ? "Desktop"
                : proc;

            UpdateContextButtons(proc);

            // Avoid pulling the system value back into the slider while it is being adjusted.
            if (!IsVolumeSliderVisible)
                SyncVolumeDisplay();

            if (_wifiRefreshTicks++ % 5 == 0)
                UpdateWifiStatus();
        }

        // ─── Wifi ────────────────────────────────────────────────────────────

        private void UpdateWifiStatus()
        {
            var networks = NetworkInterface.GetAllNetworkInterfaces();
            var wireless = networks.FirstOrDefault(n =>
                n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

            if (wireless == null)
            {
                WifiSignalBars = 0;
                return;
            }

            if (wireless.OperationalStatus == OperationalStatus.Up)
            {
                var signalPercentage = GetWifiSignalPercentage();
                WifiSignalBars = signalPercentage < 0
                    ? 4
                    : Math.Max(1, Math.Min(4, (signalPercentage + 24) / 25));
                return;
            }

            WifiSignalBars = 0;
        }

        private int GetWifiSignalPercentage()
        {
            try
            {
                var si = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(si))
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1000);
                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                        {
                            var idx = t.IndexOf(':');
                            if (idx >= 0)
                            {
                                var value = t.Substring(idx + 1).Trim().TrimEnd('%');
                                if (int.TryParse(value, out var percentage))
                                    return Math.Max(0, Math.Min(100, percentage));
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        // ─── Context shortcuts ────────────────────────────────────────────────
        // Each app context uses plain ASCII/Segoe UI labels so every glyph is visible.
        // Segoe MDL2 icons are only used where the glyph is universally supported.

        private void UpdateContextButtons(string procName)
        {
            var proc  = (procName ?? string.Empty).ToLowerInvariant();
            var title = NativeMethods.GetForegroundWindowTitle().ToLowerInvariant();

            // Build a combined key — react to page-level changes too (e.g. YouTube vs Gmail in same browser)
            var contextKey = $"{proc}|{(title.Length > 60 ? title.Substring(0, 60) : title)}";
            if (string.Equals(_contextAppKey, contextKey, StringComparison.Ordinal)) return;
            _contextAppKey = contextKey;
            ContextButtons.Clear();

            if (proc.Contains("notepad"))
            {
                AddSymbol("New", "\uE710", () => NativeMethods.SendShortcut(0x11, 0x4E));
                AddSymbol("Open", "\uE8E5", () => NativeMethods.SendShortcut(0x11, 0x4F));
                AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                AddEditingShortcuts(false);
                IsAppContextActive = true;
                return;
            }

            // ── Detect app family ──────────────────────────────────────────────
            bool IsBrowser()  => proc.Contains("chrome") || proc.Contains("msedge") || proc.Contains("edge") ||
                                 proc.Contains("firefox") || proc.Contains("brave") || proc.Contains("opera");
            bool IsExplorer() => proc.Contains("explorer");
            bool IsCode()     => proc.Contains("code") || proc.Contains("devenv") || proc.Contains("rider") || proc.Contains("idea");
            bool IsCodeBlocks() => proc.Contains("codeblocks");
            bool IsOffice()   => proc.Contains("winword") || proc.Contains("excel") || proc.Contains("powerpnt") ||
                                 proc.Contains("onenote") || proc.Contains("notepad");
            bool IsTerminal() => proc.Contains("powershell") || proc.Contains("cmd") || proc.Contains("windowsterminal") ||
                                 proc.Contains("bash") || proc.Contains("wsl");
            bool IsMedia()    => proc.Contains("vlc") || proc.Contains("mpc") || proc.Contains("wmplayer") ||
                                 proc.Contains("spotify") || proc.Contains("groove");
            bool IsPhoto()    => proc.Contains("paint") || proc.Contains("photos") || proc.Contains("photoshop") ||
                                 proc.Contains("gimp") || proc.Contains("affinity");
            bool IsTeams()    => proc.Contains("teams") || proc.Contains("slack") || proc.Contains("discord") || proc.Contains("zoom");

            // ── Title-level smart sub-contexts ────────────────────────────────
            if (IsBrowser())
            {
                if (title.Contains("youtube"))
                {
                    // YouTube: keyboard shortcuts (all single-key, no Ctrl)
                    AddSymbol("Seek back 10 seconds", "\uE892", () => NativeMethods.SendVirtualKey(0x4A));
                    AddSymbol("Play or pause", "\uE768", () => NativeMethods.SendVirtualKey(0x4B));
                    AddSymbol("Seek forward 10 seconds", "\uE893", () => NativeMethods.SendVirtualKey(0x4C));
                    AddSymbol("Mute", "\uE74F", () => NativeMethods.SendVirtualKey(0x4D));
                    AddSymbol("Full screen", "\uE740", () => NativeMethods.SendVirtualKey(0x46));
                    AddSymbol("Captions", "\uE7F4", () => NativeMethods.SendVirtualKey(0x43));
                }
                else if (title.Contains("netflix") || title.Contains("disney") || title.Contains("prime video"))
                {
                    AddSymbol("Seek back", "\uE892", () => NativeMethods.SendVirtualKey(0x25));
                    AddSymbol("Play or pause", "\uE768", () => NativeMethods.SendVirtualKey(0x20));
                    AddSymbol("Seek forward", "\uE893", () => NativeMethods.SendVirtualKey(0x27));
                    AddSymbol("Full screen", "\uE740", () => NativeMethods.SendVirtualKey(0x46));
                }
                else if (title.Contains("github") || title.Contains("gitlab") || title.Contains("bitbucket"))
                {
                    AddSymbol("Back", "\uE72B", () => NativeMethods.SendVirtualKey(0xA6));
                    AddSymbol("Forward", "\uE72A", () => NativeMethods.SendVirtualKey(0xA7));
                    AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                    AddCtrl("Find File", 0x54, 52);
                }
                else if (title.Contains("google docs") || title.Contains("word online"))
                {
                    AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                    AddSymbol("Undo", "\uE7A7", () => NativeMethods.SendShortcut(0x11, 0x5A));
                    AddSymbol("Redo", "\uE7A6", () => NativeMethods.SendShortcut(0x11, 0x59));
                    AddSymbol("Bold", "\uE8DD", () => NativeMethods.SendShortcut(0x11, 0x42));
                    AddSymbol("Italic", "\uE8DB", () => NativeMethods.SendShortcut(0x11, 0x49));
                    AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                    AddEditingShortcuts(false);
                }
                else if (title.Contains("google sheets") || title.Contains("excel online"))
                {
                    AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                    AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                    AddEditingShortcuts(true);
                }
                else if (title.Contains("gmail") || title.Contains("outlook") || title.Contains("mail"))
                {
                    AddSymbol("Compose", "\uE710", () => NativeMethods.SendVirtualKey(0x43));
                    AddSymbol("Reply", "\uE8CA", () => NativeMethods.SendVirtualKey(0x52));
                    AddSymbol("Back", "\uE72B", () => NativeMethods.SendVirtualKey(0xA6));
                    AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                }
                else
                {
                    // Generic browser
                    AddSymbol("Back", "\uE72B", () => NativeMethods.SendVirtualKey(0xA6));
                    AddSymbol("Forward", "\uE72A", () => NativeMethods.SendVirtualKey(0xA7));
                    AddSymbol("Refresh", "\uE72C", () => NativeMethods.SendVirtualKey(0x74));
                    AddCtrl("Tab+",  0x54, 38);   // Ctrl+T
                    AddCtrl("TabX",  0x57, 38);   // Ctrl+W
                    AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                }
            }
            else if (IsCodeBlocks())
            {
                AddSymbol("Build", "\uE768", () => NativeMethods.SendVirtualKey(0x78));
                AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                AddSymbol("Undo", "\uE7A7", () => NativeMethods.SendShortcut(0x11, 0x5A));
                AddSymbol("Redo", "\uE7A6", () => NativeMethods.SendShortcut(0x11, 0x59));
                AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                AddEditingShortcuts(true);
            }
            else if (IsCode())
            {
                AddSymbol("Run", "\uE768", () => NativeMethods.SendVirtualKey(0x74));
                AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                AddSymbol("Undo", "\uE7A7", () => NativeMethods.SendShortcut(0x11, 0x5A));
                AddSymbol("Redo", "\uE7A6", () => NativeMethods.SendShortcut(0x11, 0x59));
                AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                AddEditingShortcuts(true);
            }
            else if (IsTerminal())
            {
                AddSymbol("Copy", "\uE8C8", () => NativeMethods.SendShortcut(0x11, 0x43));
                AddSymbol("Paste", "\uE77F", () => NativeMethods.SendShortcut(0x11, 0x56));
                AddCtrlShift("NewTab", 0x54, 52);
                AddKey("Clr",  0x4C, 28);        // L (clear in bash)
                AddSymbol("Up", "\uE70E", () => NativeMethods.SendVirtualKey(0x26));
                AddSymbol("Down", "\uE70D", () => NativeMethods.SendVirtualKey(0x28));
            }
            else if (IsExplorer())
            {
                AddSymbol("Back", "\uE72B", () => NativeMethods.SendVirtualKey(0xA6));
                AddSymbol("Forward", "\uE72A", () => NativeMethods.SendVirtualKey(0xA7));
                AddSymbol("Parent folder", "\uE74A", () => NativeMethods.SendShortcut(0x12, 0x26));
                AddSymbol("Refresh", "\uE72C", () => NativeMethods.SendVirtualKey(0x74));
                AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                AddSymbol("Cut", "\uE8C6", () => NativeMethods.SendShortcut(0x11, 0x58));
                AddSymbol("Copy", "\uE8C8", () => NativeMethods.SendShortcut(0x11, 0x43));
                AddSymbol("Paste", "\uE77F", () => NativeMethods.SendShortcut(0x11, 0x56));
                AddSymbol("Select all", "\uE8B3", () => NativeMethods.SendShortcut(0x11, 0x41));
            }
            else if (IsOffice())
            {
                AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                AddSymbol("Print", "\uE749", () => NativeMethods.SendShortcut(0x11, 0x50));
                AddSymbol("Undo", "\uE7A7", () => NativeMethods.SendShortcut(0x11, 0x5A));
                AddSymbol("Redo", "\uE7A6", () => NativeMethods.SendShortcut(0x11, 0x59));
                AddSymbol("Bold", "\uE8DD", () => NativeMethods.SendShortcut(0x11, 0x42));
                AddSymbol("Italic", "\uE8DB", () => NativeMethods.SendShortcut(0x11, 0x49));
                AddSymbol("Underline", "\uE8DC", () => NativeMethods.SendShortcut(0x11, 0x55));
                AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                AddEditingShortcuts(false);
            }
            else if (IsMedia())
            {
                // Media player controls via virtual keys
                AddSymbol("Previous track", "\uE892", () => NativeMethods.SendVirtualKey(0xB1));
                AddSymbol("Play or pause", "\uE768", () => NativeMethods.SendVirtualKey(0xB3));
                AddSymbol("Next track", "\uE893", () => NativeMethods.SendVirtualKey(0xB0));
                AddSymbol("Mute", "\uE74F", () => NativeMethods.SendVirtualKey(0xAD));
            }
            else if (IsPhoto())
            {
                AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                AddSymbol("Undo", "\uE7A7", () => NativeMethods.SendShortcut(0x11, 0x5A));
                AddSymbol("Redo", "\uE7A6", () => NativeMethods.SendShortcut(0x11, 0x59));
                AddSymbol("Zoom in", "\uE8A3", () => NativeMethods.SendShortcut(0x11, 0xBB));
                AddSymbol("Zoom out", "\uE71F", () => NativeMethods.SendShortcut(0x11, 0xBD));
                AddCtrl("Fit",   0x30, 28);  // Ctrl+0
                AddEditingShortcuts(false);
            }
            else if (IsTeams())
            {
                AddSymbol("Mute", "\uE74F", () => NativeMethods.SendShortcut(0x11, 0x4D));
                AddCtrl("Cam",    0x4F, 34);   // Ctrl+O (toggle camera many apps)
                AddCtrl("Reply",  0x52, 36);
                AddSymbol("Find", "\uE721", () => NativeMethods.SendShortcut(0x11, 0x46));
                AddEditingShortcuts(true);
            }
            else
            {
                // Generic fallback: Undo, Redo, Copy, Paste
                AddSymbol("Undo", "\uE7A7", () => NativeMethods.SendShortcut(0x11, 0x5A));
                AddSymbol("Redo", "\uE7A6", () => NativeMethods.SendShortcut(0x11, 0x59));
                AddSymbol("Copy", "\uE8C8", () => NativeMethods.SendShortcut(0x11, 0x43));
                AddSymbol("Paste", "\uE77F", () => NativeMethods.SendShortcut(0x11, 0x56));
                AddSymbol("Save", "\uE74E", () => NativeMethods.SendShortcut(0x11, 0x53));
                AddEditingShortcuts(true);
            }

            IsAppContextActive = ContextButtons.Count > 0;
        }

        // ─── Button factory helpers ───────────────────────────────────────────

        // Single VK press (no modifier)
        private void AddKey(string label, ushort vk, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(label, () => NativeMethods.SendVirtualKey(vk), "Segoe UI", w));

        private void AddSymbol(string label, string glyph, Action action)
        {
            ContextButtons.Add(new FunctionRowButtonModel(glyph, action, "Segoe MDL2 Assets", 30));
        }

        private void AddEditingShortcuts(bool includeFormatting)
        {
            AddSymbol("Copy", "\uE8C8", () => NativeMethods.SendShortcut(0x11, 0x43));
            AddSymbol("Cut", "\uE8C6", () => NativeMethods.SendShortcut(0x11, 0x58));
            AddSymbol("Paste", "\uE77F", () => NativeMethods.SendShortcut(0x11, 0x56));
            AddSymbol("Select all", "\uE8B3", () => NativeMethods.SendShortcut(0x11, 0x41));
            if (includeFormatting)
            {
                AddSymbol("Bold", "\uE8DD", () => NativeMethods.SendShortcut(0x11, 0x42));
                AddSymbol("Italic", "\uE8DB", () => NativeMethods.SendShortcut(0x11, 0x49));
            }
        }

        // Single VK press via SendVirtualKey (legacy — unchanged from before)
        private void AddShortcutText(string label, ushort vk, ushort _, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(label, () => NativeMethods.SendVirtualKey(vk), "Segoe UI", w));

        // Ctrl + key
        private void AddCtrl(string label, ushort key, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(label, () => NativeMethods.SendShortcut(0x11, key), "Segoe UI", w));

        // Ctrl + Shift + key
        private void AddCtrlShift(string label, ushort key, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(label, () => NativeMethods.SendChord(new ushort[] {0x11, 0x10}, key), "Segoe UI", w));

        // Alt + key
        private void AddAltKey(string label, ushort key, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(label, () => NativeMethods.SendShortcut(0x12, key), "Segoe UI", w));

        // Legacy icon helpers (MDL2, kept for compatibility)
        private void AddIcon(string icon, ushort vk, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(icon, () => NativeMethods.SendVirtualKey(vk), "Segoe MDL2 Assets", w));

        private void AddShortcut(string icon, ushort mod, ushort key, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(icon, () => NativeMethods.SendShortcut(mod, key), "Segoe MDL2 Assets", w));

        private void AddText(string text, ushort mod, ushort key, double w = 34)
            => ContextButtons.Add(new FunctionRowButtonModel(text, () => NativeMethods.SendShortcut(mod, key), "Segoe UI", w));

        // Bold/Italic helpers alias (same as Ctrl but named for clarity)
        private void AddCtrlB(string label, ushort key, double w = 34)
            => AddCtrl(label, key, w);

        // ─── SMTC events ─────────────────────────────────────────────────────

        private async void InitializeSmtcAsync()
        {
            _glbSmtcMgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _glbSmtcSession = _glbSmtcMgr.GetCurrentSession();
            if (_glbSmtcSession != null)
            {
                _glbSmtcSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _glbSmtcSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                await LoadMediaPropertiesAsync();
                UpdatePlayButtonStatus();
                IsSmtcActive = true;
            }
            _glbSmtcMgr.CurrentSessionChanged += OnSmtcSessionChanged;
        }

        private async void OnSmtcSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
            CurrentSessionChangedEventArgs args)
        {
            _glbSmtcSession = _glbSmtcMgr.GetCurrentSession();
            if (_glbSmtcSession != null)
            {
                _glbSmtcSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _glbSmtcSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                await LoadMediaPropertiesAsync();
                UpdatePlayButtonStatus();
                IsSmtcActive = true;
            }
            else
            {
                ResetSmtc();
            }
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
            => UpdatePlayButtonStatus();

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
            => await LoadMediaPropertiesAsync();

        private void UpdatePlayButtonStatus()
        {
            if (_glbSmtcSession == null) return;
            try
            {
                var playbackInfo = _glbSmtcSession.GetPlaybackInfo();
                switch (playbackInfo.PlaybackStatus)
                {
                    case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing:
                        _playPauseButton.Enabled = true;
                        _playPauseButton.Content = "\uE103"; // Pause icon
                        break;
                    case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused:
                    case GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped:
                        _playPauseButton.Enabled = true;
                        _playPauseButton.Content = "\uE102"; // Play icon
                        break;
                    default:
                        _playPauseButton.Enabled = false;
                        _playPauseButton.Content = "\uE102";
                        break;
                }
            }
            catch { }
        }

        private async Task LoadMediaPropertiesAsync()
        {
            try
            {
                var prop = await _glbSmtcSession.TryGetMediaPropertiesAsync();
                MediaTitle  = prop.Title;
                MediaArtist = prop.Artist;

                if (prop.Thumbnail != null)
                {
                    using (var rs = await prop.Thumbnail.OpenReadAsync())
                    using (var s  = rs.AsStreamForRead())
                    {
                        MediaThumbnail = new Bitmap(s);
                    }
                    ThumbnailAvailable = true;
                }
                else
                {
                    ThumbnailAvailable = false;
                }
            }
            catch { }
        }
    }
}
