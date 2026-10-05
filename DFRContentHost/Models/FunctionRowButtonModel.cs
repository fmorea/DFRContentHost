using Avalonia.Media;
using DFRContentHost.Interop;
using ReactiveUI;
using System;
using System.Reactive;
using WindowsInput.Native;

namespace DFRContentHost.Models
{
    public class FunctionRowButtonModel : ReactiveObject
    {
        private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
        private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));

        private bool _enabled = true;
        private string _keyContent;
        private VirtualKeyCode _keyCode;
        private Action _action;
        private FontFamily _fontFamily = new FontFamily("Segoe UI");
        private double _width = 34;
        private bool _isActive;
        private IBrush _backgroundBrush = DefaultBrush;

        public string Content
        {
            get => _keyContent;
            set => this.RaiseAndSetIfChanged(ref _keyContent, value);
        }

        public VirtualKeyCode Code
        {
            get => _keyCode;
            set => this.RaiseAndSetIfChanged(ref _keyCode, value);
        }

        public bool Enabled
        {
            get => _enabled;
            set => this.RaiseAndSetIfChanged(ref _enabled, value);
        }

        public FontFamily FontFamily
        {
            get => _fontFamily;
            set => this.RaiseAndSetIfChanged(ref _fontFamily, value);
        }

        public double Width
        {
            get => _width;
            set => this.RaiseAndSetIfChanged(ref _width, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                this.RaiseAndSetIfChanged(ref _isActive, value);
                BackgroundBrush = value ? ActiveBrush : DefaultBrush;
            }
        }

        public IBrush BackgroundBrush
        {
            get => _backgroundBrush;
            set => this.RaiseAndSetIfChanged(ref _backgroundBrush, value);
        }

        public ReactiveCommand<Unit, Unit> KeyCommand => ReactiveCommand.Create(SendKeyItem);

        private void SendKeyItem()
        {
            try
            {
                if (_action != null)
                {
                    _action();
                    return;
                }

                NativeMethods.SendVirtualKey((ushort)_keyCode);
            }
            catch (Exception)
            {
                // ULPI issue
            }
        }

        public FunctionRowButtonModel() { }

        public FunctionRowButtonModel(string content, VirtualKeyCode code, string fontFamily = "Segoe UI", double width = 34)
        {
            _enabled = true;
            _keyContent = content;
            _keyCode = code;
            _fontFamily = new FontFamily(fontFamily);
            _width = width;
            _backgroundBrush = DefaultBrush;
        }

        public FunctionRowButtonModel(string content, Action action, string fontFamily = "Segoe UI", double width = 34)
        {
            _enabled = true;
            _keyContent = content;
            _action = action;
            _fontFamily = new FontFamily(fontFamily);
            _width = width;
            _backgroundBrush = DefaultBrush;
        }
    }
}
