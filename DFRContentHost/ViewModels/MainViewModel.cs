using ReactiveUI;

namespace DFRContentHost.ViewModels
{
    public class MainViewModel : ReactiveObject
    {
        public FunctionRowButtonCollectionViewModel FnKeyViewModel { get; }
        public SystemMediaTransportControlViewModel SmtcViewModel { get; }
        public VirtualKeyboardViewModel KeyboardViewModel { get; }

        private bool _functionKeysVisible;
        public bool FunctionKeysVisible
        {
            get => _functionKeysVisible;
            private set => this.RaiseAndSetIfChanged(ref _functionKeysVisible, value);
        }

        private bool _isKeyboardVisible;
        public bool IsKeyboardVisible
        {
            get => _isKeyboardVisible;
            private set => this.RaiseAndSetIfChanged(ref _isKeyboardVisible, value);
        }

        private bool _isDesktopStripVisible;
        public bool IsDesktopStripVisible
        {
            get => _isDesktopStripVisible;
            private set => this.RaiseAndSetIfChanged(ref _isDesktopStripVisible, value);
        }

        public MainViewModel()
        {
            FnKeyViewModel = new FunctionRowButtonCollectionViewModel();
            SmtcViewModel = new SystemMediaTransportControlViewModel();
            KeyboardViewModel = new VirtualKeyboardViewModel();

            FnKeyViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(FnKeyViewModel.FnKeysExpanded) && FnKeyViewModel.FnKeysExpanded)
                {
                    KeyboardViewModel.IsKeyboardVisible = false;
                }
                UpdateViewStates();
            };

            KeyboardViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(KeyboardViewModel.IsKeyboardVisible) && KeyboardViewModel.IsKeyboardVisible)
                {
                    if (FnKeyViewModel.FnKeysExpanded)
                    {
                        FnKeyViewModel.FnKeysExpanded = false;
                    }
                }
                UpdateViewStates();
            };

            SmtcViewModel.PropertyChanged += (sender, args) => UpdateViewStates();
            UpdateViewStates();
        }

        private void UpdateViewStates()
        {
            FunctionKeysVisible = FnKeyViewModel.FnKeysExpanded || FnKeyViewModel.FnPressed;
            var isSystemSliderVisible = SmtcViewModel.IsVolumeSliderVisible || SmtcViewModel.IsBrightnessSliderVisible;
            IsKeyboardVisible = KeyboardViewModel.IsKeyboardVisible && !isSystemSliderVisible;
            IsDesktopStripVisible = !KeyboardViewModel.IsKeyboardVisible && !isSystemSliderVisible;
        }
    }
}
