using Avalonia.DfrFrameBuffer;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using DFRContentHost.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using WindowsInput.Native;

namespace DFRContentHost.ViewModels
{
    public class FunctionRowButtonCollectionViewModel : ReactiveObject
    {
        public ObservableCollection<FunctionRowButtonModel> FnKeys { get; }
        public FunctionRowButtonModel FnButton { get; }
        private FnKeyNotifier _fnKeyNotifier;

        private bool _fnPressed;
        public bool FnPressed
        {
            get => _fnPressed;
            set
            {
                this.RaiseAndSetIfChanged(ref _fnPressed, value);
                UpdateFnButtonState();
            }
        }

        private bool _fnKeysExpanded;
        public bool FnKeysExpanded
        {
            get => _fnKeysExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _fnKeysExpanded, value);
                UpdateFnButtonState();
            }
        }

        public FunctionRowButtonCollectionViewModel()
        {
            FnButton = new FunctionRowButtonModel("Fn", ToggleFnKeys, "Segoe UI", 36);
            FnKeys = new ObservableCollection<FunctionRowButtonModel>
            {
                new FunctionRowButtonModel("F1", VirtualKeyCode.F1, "Segoe UI", 33),
                new FunctionRowButtonModel("F2", VirtualKeyCode.F2, "Segoe UI", 33),
                new FunctionRowButtonModel("F3", VirtualKeyCode.F3, "Segoe UI", 33),
                new FunctionRowButtonModel("F4", VirtualKeyCode.F4, "Segoe UI", 33),
                new FunctionRowButtonModel("F5", VirtualKeyCode.F5, "Segoe UI", 33),
                new FunctionRowButtonModel("F6", VirtualKeyCode.F6, "Segoe UI", 33),
                new FunctionRowButtonModel("F7", VirtualKeyCode.F7, "Segoe UI", 33),
                new FunctionRowButtonModel("F8", VirtualKeyCode.F8, "Segoe UI", 33),
                new FunctionRowButtonModel("F9", VirtualKeyCode.F9, "Segoe UI", 33),
                new FunctionRowButtonModel("F10", VirtualKeyCode.F10, "Segoe UI", 33),
                new FunctionRowButtonModel("F11", VirtualKeyCode.F11, "Segoe UI", 33),
                new FunctionRowButtonModel("F12", VirtualKeyCode.F12, "Segoe UI", 33)
            };

            _fnKeyNotifier = new FnKeyNotifier(Dispatcher.UIThread);
            _fnKeyNotifier.Event += OnFnKeyStateChanged;
        }

        private void ToggleFnKeys()
        {
            FnKeysExpanded = !FnKeysExpanded;
        }

        private void OnFnKeyStateChanged(RawInputEventArgs obj)
        {
            var args = (RawKeyEventArgs) obj;

            if (args.Key == Key.F23)
            {
                FnPressed = args.Type == RawKeyEventType.KeyDown;
            }

            obj.Handled = true;
        }

        private void UpdateFnButtonState()
        {
            FnButton.IsActive = FnKeysExpanded || FnPressed;
        }
    }
}
