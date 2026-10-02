using DFRContentHost.Interop;
using DFRContentHost.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;

namespace DFRContentHost.ViewModels
{
    /// <summary>
    /// Virtual Keyboard ViewModel.
    /// The keyboard uses a manual sliding window instead of a ScrollViewer,
    /// because the Digitizer only fires MouseMove/LeftButtonDown/Up events —
    /// Avalonia ScrollViewer cannot be scrolled by Touch Bar touch input.
    ///
    /// The user navigates left/right with dedicated arrow buttons.
    /// </summary>
    public class VirtualKeyboardViewModel : ReactiveObject
    {
        // ─── full character sets ────────────────────────────────────────────
        private static readonly char[] LetterSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        private static readonly string[] SymbolSet = new string[]
        {
            "1","2","3","4","5","6","7","8","9","0",
            "-","=","[","]","\\",";","'",",",".","/"
        };

        // ─── visible window ─────────────────────────────────────────────────
        private const int PageSize = 8;   // how many keys fit on screen at a time
        private int _windowStart = 0;      // index into the active charset

        // ─── state ──────────────────────────────────────────────────────────
        private bool _isKeyboardVisible;
        public bool IsKeyboardVisible
        {
            get => _isKeyboardVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isKeyboardVisible, value);
                AzButton.IsActive = value;
                if (value) RefreshWindow();
            }
        }

        private bool _isCapsActive;
        public bool IsCapsActive
        {
            get => _isCapsActive;
            set
            {
                this.RaiseAndSetIfChanged(ref _isCapsActive, value);
                CapsButton.IsActive = value;
                RefreshWindow();
            }
        }

        private bool _isNumericMode;
        public bool IsNumericMode
        {
            get => _isNumericMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _isNumericMode, value);
                NumModeButton.Content = value ? "ABC" : "?123";
                NumModeButton.IsActive = value;
                _windowStart = 0;
                RefreshWindow();
            }
        }

        private bool _canScrollLeft;
        public bool CanScrollLeft
        {
            get => _canScrollLeft;
            private set => this.RaiseAndSetIfChanged(ref _canScrollLeft, value);
        }

        private bool _canScrollRight;
        public bool CanScrollRight
        {
            get => _canScrollRight;
            private set => this.RaiseAndSetIfChanged(ref _canScrollRight, value);
        }

        // ─── control buttons ────────────────────────────────────────────────
        public FunctionRowButtonModel AzButton { get; }
        public FunctionRowButtonModel CapsButton { get; }
        public FunctionRowButtonModel NumModeButton { get; }
        public FunctionRowButtonModel SpaceButton { get; }
        public FunctionRowButtonModel BackspaceButton { get; }
        public FunctionRowButtonModel EnterButton { get; }
        public FunctionRowButtonModel TabButton { get; }
        public FunctionRowButtonModel EscButton { get; }
        public FunctionRowButtonModel ScrollLeftButton { get; }
        public FunctionRowButtonModel ScrollRightButton { get; }

        // ─── visible key window ─────────────────────────────────────────────
        public ObservableCollection<FunctionRowButtonModel> VisibleKeys { get; }
            = new ObservableCollection<FunctionRowButtonModel>();

        // ────────────────────────────────────────────────────────────────────
        public VirtualKeyboardViewModel()
        {
            AzButton = new FunctionRowButtonModel("Az", ToggleKeyboard, "Segoe UI", 32);

            CapsButton = new FunctionRowButtonModel("Aa", () => IsCapsActive = !IsCapsActive, "Segoe UI", 30);
            NumModeButton = new FunctionRowButtonModel("?123", () => IsNumericMode = !IsNumericMode, "Segoe UI", 36);

            // These use native Win32 SendInput VK codes
            SpaceButton = new FunctionRowButtonModel("Spc", () => NativeMethods.SendChar(' '), "Segoe UI", 56);
            BackspaceButton = new FunctionRowButtonModel("Bksp", () => NativeMethods.SendVirtualKey(0x08), "Segoe UI", 34);
            EnterButton = new FunctionRowButtonModel("Ent", () => NativeMethods.SendVirtualKey(0x0D), "Segoe UI", 32);
            TabButton = new FunctionRowButtonModel("Tab", () => NativeMethods.SendVirtualKey(0x09), "Segoe UI", 30);
            EscButton = new FunctionRowButtonModel("Esc", () => NativeMethods.SendVirtualKey(0x1B), "Segoe UI", 30);

            ScrollLeftButton = new FunctionRowButtonModel("<", ScrollLeft, "Segoe UI", 26);
            ScrollRightButton = new FunctionRowButtonModel(">", ScrollRight, "Segoe UI", 26);

            RefreshWindow();
        }

        private void ToggleKeyboard() => IsKeyboardVisible = !IsKeyboardVisible;

        private void ScrollLeft()
        {
            if (_windowStart >= PageSize)
            {
                _windowStart -= PageSize;
                RefreshWindow();
            }
            else if (_windowStart > 0)
            {
                _windowStart = 0;
                RefreshWindow();
            }
        }

        private void ScrollRight()
        {
            var total = _isNumericMode ? SymbolSet.Length : LetterSet.Length;
            if (_windowStart + PageSize < total)
            {
                _windowStart += PageSize;
                RefreshWindow();
            }
        }

        private void RefreshWindow()
        {
            VisibleKeys.Clear();

            if (_isNumericMode)
            {
                var end = Math.Min(_windowStart + PageSize, SymbolSet.Length);
                for (int i = _windowStart; i < end; i++)
                {
                    var sym = SymbolSet[i];
                    VisibleKeys.Add(new FunctionRowButtonModel(sym, () => SendSymbol(sym), "Segoe UI", 28));
                }
                CanScrollLeft = _windowStart > 0;
                CanScrollRight = end < SymbolSet.Length;
            }
            else
            {
                var end = Math.Min(_windowStart + PageSize, LetterSet.Length);
                for (int i = _windowStart; i < end; i++)
                {
                    var ch = _isCapsActive ? LetterSet[i] : char.ToLowerInvariant(LetterSet[i]);
                    var capture = ch;
                    VisibleKeys.Add(new FunctionRowButtonModel(ch.ToString(), () => NativeMethods.SendChar(capture), "Segoe UI", 28));
                }
                CanScrollLeft = _windowStart > 0;
                CanScrollRight = end < LetterSet.Length;
            }

            ScrollLeftButton.Enabled = CanScrollLeft;
            ScrollRightButton.Enabled = CanScrollRight;
        }

        private static void SendSymbol(string sym)
        {
            if (sym.Length == 1) NativeMethods.SendChar(sym[0]);
        }
    }
}
