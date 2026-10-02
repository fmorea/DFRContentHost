using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DFRContentHost.Interop
{
    public static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte AcLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        #region User32 APIs

        [DllImport("user32.dll")]
        public static extern void LockWorkStation();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxLength);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static string GetForegroundWindowTitle()
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return string.Empty;

            var title = new StringBuilder(256);
            return GetWindowText(window, title, title.Capacity) > 0 ? title.ToString() : string.Empty;
        }

        public static string GetForegroundProcessName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return "Desktop";
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0) return "Desktop";
                using (var proc = Process.GetProcessById((int)pid))
                {
                    return proc.ProcessName;
                }
            }
            catch
            {
                return "Desktop";
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetMenu(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr menu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int maxLength, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr menu, int position);

        [DllImport("user32.dll")]
        private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);

        private const uint MenuByPosition = 0x0400;
        private const uint MenuStateByPosition = 0x0400;
        private const uint MenuStateDisabled = 0x0003;
        private const uint MenuStateSeparator = 0x0800;

        public static IList<RuntimeMenuCommand> GetRuntimeMenuCommands()
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return new List<RuntimeMenuCommand>();

            try
            {
                var startInfo = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                    "--menu-probe " + window.ToInt64())
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return new List<RuntimeMenuCommand>();
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill();
                        return new List<RuntimeMenuCommand>();
                    }

                    if (process.ExitCode != 0)
                        return new List<RuntimeMenuCommand>();

                    var commands = new List<RuntimeMenuCommand>();
                    foreach (var line in process.StandardOutput.ReadToEnd().Split(
                        new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var fields = line.Split('|');
                        if (fields.Length != 3)
                            continue;

                        var category = DecodeMenuField(fields[0]);
                        var text = DecodeMenuField(fields[1]);
                        var shortcut = DecodeMenuField(fields[2]);
                        var invoke = CreateMenuCommandAction(category, text, shortcut);
                        if (invoke != null)
                            commands.Add(new RuntimeMenuCommand(category, text, shortcut, invoke));
                    }

                    return commands;
                }
            }
            catch
            {
                return new List<RuntimeMenuCommand>();
            }
        }

        public static void WriteRuntimeMenuSnapshot(string windowHandle)
        {
            if (!long.TryParse(windowHandle, out var handleValue))
                return;

            var commands = GetRuntimeMenuCommandsForWindow(new IntPtr(handleValue));
            foreach (var command in commands)
            {
                Console.WriteLine(EncodeMenuField(command.Category) + "|" +
                    EncodeMenuField(command.Text) + "|" + EncodeMenuField(command.Shortcut));
            }
        }

        private static IList<RuntimeMenuCommand> GetRuntimeMenuCommandsForWindow(IntPtr window)
        {
            var commandGroups = new List<List<RuntimeMenuCommand>>();
            var menu = GetMenu(window);
            if (window == IntPtr.Zero || menu == IntPtr.Zero)
                return new List<RuntimeMenuCommand>();

            var menuCount = GetMenuItemCount(menu);
            for (var position = 0; position < menuCount; position++)
            {
                var menuLabel = ReadMenuLabel(menu, position, out _);
                var submenu = GetSubMenu(menu, position);
                if (submenu == IntPtr.Zero || string.IsNullOrWhiteSpace(menuLabel))
                    continue;

                var group = new List<RuntimeMenuCommand>();
                ReadMenu(window, submenu, menuLabel, group, 0);
                if (group.Count > 0)
                    commandGroups.Add(group);
            }

            var commands = new List<RuntimeMenuCommand>();
            for (var commandIndex = 0; commands.Count < 12; commandIndex++)
            {
                var addedCommand = false;
                foreach (var group in commandGroups)
                {
                    if (commandIndex >= group.Count)
                        continue;

                    commands.Add(group[commandIndex]);
                    addedCommand = true;
                    if (commands.Count == 12)
                        break;
                }

                if (!addedCommand)
                    break;
            }

            return commands;
        }

        private static string EncodeMenuField(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static string DecodeMenuField(string value)
            => Encoding.UTF8.GetString(Convert.FromBase64String(value));

        private static void ReadMenu(IntPtr window, IntPtr menu, string menuPath,
            IList<RuntimeMenuCommand> commands, int depth)
        {
            if (depth > 2 || commands.Count >= 8)
                return;

            var count = GetMenuItemCount(menu);
            for (var position = 0; position < count && commands.Count < 8; position++)
            {
                var state = GetMenuState(menu, (uint)position, MenuStateByPosition);
                if (state == uint.MaxValue || (state & (MenuStateDisabled | MenuStateSeparator)) != 0)
                    continue;

                var text = ReadMenuLabel(menu, position, out var shortcut);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var submenu = GetSubMenu(menu, position);
                if (submenu != IntPtr.Zero)
                {
                    ReadMenu(window, submenu, menuPath + " > " + text, commands, depth + 1);
                    continue;
                }

                var invoke = CreateMenuCommandAction(menuPath, text, shortcut);
                if (invoke != null)
                {
                    commands.Add(new RuntimeMenuCommand(menuPath, text, shortcut, invoke));
                }
            }
        }

        private static Action CreateMenuCommandAction(string menuPath, string text, string shortcut)
        {
            if (!TryParseMenuShortcut(shortcut, out var modifiers, out var key))
            {
                var category = menuPath.Split('>')[0].Trim();
                var command = text.TrimEnd('.').ToLowerInvariant();
                if (category.Equals("File", StringComparison.OrdinalIgnoreCase))
                {
                    if (command == "new") { modifiers = new ushort[] { 0x11 }; key = 0x4E; }
                    else if (command == "open") { modifiers = new ushort[] { 0x11 }; key = 0x4F; }
                    else if (command == "save") { modifiers = new ushort[] { 0x11 }; key = 0x53; }
                    else if (command == "print") { modifiers = new ushort[] { 0x11 }; key = 0x50; }
                    else return null;
                }
                else if (category.Equals("Edit", StringComparison.OrdinalIgnoreCase))
                {
                    if (command == "undo") { modifiers = new ushort[] { 0x11 }; key = 0x5A; }
                    else if (command == "redo") { modifiers = new ushort[] { 0x11 }; key = 0x59; }
                    else if (command == "cut") { modifiers = new ushort[] { 0x11 }; key = 0x58; }
                    else if (command == "copy") { modifiers = new ushort[] { 0x11 }; key = 0x43; }
                    else if (command == "paste") { modifiers = new ushort[] { 0x11 }; key = 0x56; }
                    else if (command == "select all") { modifiers = new ushort[] { 0x11 }; key = 0x41; }
                    else if (command == "find") { modifiers = new ushort[] { 0x11 }; key = 0x46; }
                    else if (command == "replace") { modifiers = new ushort[] { 0x11 }; key = 0x48; }
                    else return null;
                }
                else
                {
                    return null;
                }
            }

            return () =>
            {
                if (modifiers.Length == 0)
                    SendVirtualKey(key);
                else if (modifiers.Length == 1)
                    SendShortcut(modifiers[0], key);
                else
                    SendChord(modifiers, key);
            };
        }

        private static bool TryParseMenuShortcut(string shortcut, out ushort[] modifiers, out ushort key)
        {
            var parsedModifiers = new List<ushort>();
            key = 0;
            var parts = (shortcut ?? string.Empty).Split('+');
            foreach (var part in parts)
            {
                var token = part.Trim();
                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    parsedModifiers.Add(0x11);
                else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    parsedModifiers.Add(0x10);
                else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    parsedModifiers.Add(0x12);
                else if (TryGetVirtualKey(token, out key))
                    continue;
                else
                {
                    modifiers = new ushort[0];
                    key = 0;
                    return false;
                }
            }

            modifiers = parsedModifiers.ToArray();
            return key != 0;
        }

        private static bool TryGetVirtualKey(string token, out ushort key)
        {
            key = 0;
            if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
            {
                key = (ushort)char.ToUpperInvariant(token[0]);
                return true;
            }

            if (token.Length >= 2 && token[0] == 'F' &&
                ushort.TryParse(token.Substring(1), out var functionKey) && functionKey >= 1 && functionKey <= 24)
            {
                key = (ushort)(0x6F + functionKey);
                return true;
            }

            switch (token.ToLowerInvariant())
            {
                case "space": key = 0x20; break;
                case "backspace": key = 0x08; break;
                case "tab": key = 0x09; break;
                case "enter":
                case "return": key = 0x0D; break;
                case "esc":
                case "escape": key = 0x1B; break;
                case "page up":
                case "pgup": key = 0x21; break;
                case "page down":
                case "pgdn": key = 0x22; break;
                case "end": key = 0x23; break;
                case "home": key = 0x24; break;
                case "left": key = 0x25; break;
                case "up": key = 0x26; break;
                case "right": key = 0x27; break;
                case "down": key = 0x28; break;
                case "insert":
                case "ins": key = 0x2D; break;
                case "delete":
                case "del": key = 0x2E; break;
                default: return false;
            }

            return true;
        }

        private static string ReadMenuLabel(IntPtr menu, int position, out string shortcut)
        {
            var textBuffer = new StringBuilder(128);
            GetMenuString(menu, (uint)position, textBuffer, textBuffer.Capacity, MenuByPosition);
            var text = textBuffer.ToString().Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&").Trim();
            var separatorIndex = text.IndexOf('\t');
            shortcut = separatorIndex >= 0 ? text.Substring(separatorIndex + 1).Trim() : string.Empty;
            return separatorIndex >= 0 ? text.Substring(0, separatorIndex).Trim() : text;
        }

        #endregion

        #region Native Win32 SendInput Keyboard API

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        /// <summary>
        /// Sends a Unicode character directly to the active foreground window.
        /// Works for any character (A-Z, a-z, numbers, symbols, accents) independent of keyboard layout.
        /// </summary>
        public static void SendChar(char c)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    INPUT[] inputs = new INPUT[2];
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].U.ki.wScan = (ushort)c;
                    inputs[0].U.ki.dwFlags = KEYEVENTF_UNICODE;

                    inputs[1].type = INPUT_KEYBOARD;
                    inputs[1].U.ki.wScan = (ushort)c;
                    inputs[1].U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

                    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                }
                catch { }
            });
        }

        /// <summary>
        /// Sends a virtual key down and up.
        /// </summary>
        public static void SendVirtualKey(ushort vk)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    INPUT[] inputs = new INPUT[2];
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].U.ki.wVk = vk;

                    inputs[1].type = INPUT_KEYBOARD;
                    inputs[1].U.ki.wVk = vk;
                    inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;

                    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                }
                catch { }
            });
        }

        /// <summary>
        /// Sends the Windows/Start key using EXTENDEDKEY flag (required for VK_LWIN/VK_RWIN).
        /// </summary>
        public static void SendWinKey()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    const ushort VK_LWIN = 0x5B;
                    INPUT[] inputs = new INPUT[2];
                    inputs[0].type = INPUT_KEYBOARD;
                    inputs[0].U.ki.wVk = VK_LWIN;
                    inputs[0].U.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

                    inputs[1].type = INPUT_KEYBOARD;
                    inputs[1].U.ki.wVk = VK_LWIN;
                    inputs[1].U.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

                    SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                }
                catch { }
            });
        }

        /// <summary>
        /// Sends a keyboard shortcut (e.g. Ctrl + Key).
        /// </summary>
        public static void SendShortcut(ushort modifierVk, ushort keyVk)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    INPUT[] inputs = new INPUT[4];
                    inputs[0].type = INPUT_KEYBOARD; inputs[0].U.ki.wVk = modifierVk;
                    inputs[1].type = INPUT_KEYBOARD; inputs[1].U.ki.wVk = keyVk;
                    inputs[2].type = INPUT_KEYBOARD; inputs[2].U.ki.wVk = keyVk; inputs[2].U.ki.dwFlags = KEYEVENTF_KEYUP;
                    inputs[3].type = INPUT_KEYBOARD; inputs[3].U.ki.wVk = modifierVk; inputs[3].U.ki.dwFlags = KEYEVENTF_KEYUP;

                    SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
                }
                catch { }
            });
        }

        /// <summary>
        /// Sends a keyboard chord with multiple modifiers (e.g. Ctrl+Shift+Key).
        /// </summary>
        public static void SendChord(ushort[] modifiers, ushort keyVk)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int n = modifiers.Length;
                    INPUT[] inputs = new INPUT[(n + 1) * 2];
                    // Press all modifiers
                    for (int i = 0; i < n; i++)
                    {
                        inputs[i].type = INPUT_KEYBOARD;
                        inputs[i].U.ki.wVk = modifiers[i];
                    }
                    // Press the key
                    inputs[n].type = INPUT_KEYBOARD;
                    inputs[n].U.ki.wVk = keyVk;
                    // Release the key
                    inputs[n + 1].type = INPUT_KEYBOARD;
                    inputs[n + 1].U.ki.wVk = keyVk;
                    inputs[n + 1].U.ki.dwFlags = KEYEVENTF_KEYUP;
                    // Release modifiers in reverse
                    for (int i = 0; i < n; i++)
                    {
                        inputs[n + 2 + i].type = INPUT_KEYBOARD;
                        inputs[n + 2 + i].U.ki.wVk = modifiers[n - 1 - i];
                        inputs[n + 2 + i].U.ki.dwFlags = KEYEVENTF_KEYUP;
                    }
                    SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                }
                catch { }
            });
        }

        #endregion

        #region Kernel32 Power API

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        public static bool GetPowerStatus(out byte batteryPercentage, out bool charging)
        {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status))
            {
                batteryPercentage = 255;
                charging = false;
                return false;
            }

            batteryPercentage = status.BatteryLifePercent;
            charging = status.AcLineStatus == 1;
            return true;
        }

        #endregion
    }

    #region Native Windows Core Audio Volume API (COM)

    public static class NativeAudio
    {
        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        private static IAudioEndpointVolume _volumeControl;
        private static readonly object _lock = new object();

        private static IAudioEndpointVolume GetVolumeControl()
        {
            lock (_lock)
            {
                if (_volumeControl != null) return _volumeControl;
                try
                {
                    var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                    // dataFlow: 0 = eRender, role: 1 = eMultimedia
                    if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) == 0 && device != null)
                    {
                        var iid = typeof(IAudioEndpointVolume).GUID;
                        if (device.Activate(ref iid, 1, IntPtr.Zero, out var endpointObj) == 0)
                        {
                            _volumeControl = (IAudioEndpointVolume)endpointObj;
                        }
                    }
                }
                catch { }
                return _volumeControl;
            }
        }

        /// <summary>
        /// Reads native Windows master volume directly from Core Audio (0 to 100).
        /// Returns -1 on failure.
        /// </summary>
        public static float GetMasterVolume()
        {
            try
            {
                var vol = GetVolumeControl();
                if (vol != null && vol.GetMasterVolumeLevelScalar(out var level) == 0)
                {
                    return level * 100f;
                }
            }
            catch
            {
                _volumeControl = null;
            }
            return -1f;
        }

        /// <summary>
        /// Sets native Windows master volume directly via Core Audio (0 to 100).
        /// </summary>
        public static bool SetMasterVolume(float levelPercent)
        {
            try
            {
                var vol = GetVolumeControl();
                if (vol != null)
                {
                    var scalar = Math.Max(0f, Math.Min(1f, levelPercent / 100f));
                    var guid = Guid.Empty;
                    return vol.SetMasterVolumeLevelScalar(scalar, ref guid) == 0;
                }
            }
            catch
            {
                _volumeControl = null;
            }
            return false;
        }

        public static bool IsMuted()
        {
            try
            {
                var vol = GetVolumeControl();
                if (vol != null && vol.GetMute(out var muted) == 0)
                {
                    return muted;
                }
            }
            catch { _volumeControl = null; }
            return false;
        }

        public static void SetMute(bool mute)
        {
            try
            {
                var vol = GetVolumeControl();
                if (vol != null)
                {
                    var guid = Guid.Empty;
                    vol.SetMute(mute, ref guid);
                }
            }
            catch { _volumeControl = null; }
        }
    }

    #endregion

    public sealed class RuntimeMenuCommand
    {
        public string Category { get; }
        public string Text { get; }
        public string Shortcut { get; }
        public Action Invoke { get; }

        public RuntimeMenuCommand(string category, string text, string shortcut, Action invoke)
        {
            Category = category;
            Text = text;
            Shortcut = shortcut;
            Invoke = invoke;
        }
    }
}
