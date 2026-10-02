using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DFRContentHost.Interop;
using DFRContentHost.ViewModels;
using System;

namespace DFRContentHost
{
    public class MainView : UserControl
    {
        private const double SliderThumbWidth = 20;
        private Control _activeSliderCanvas;
        private bool _activeSliderIsBrightness;
        private double _sliderPointerOffset;

        public MainView()
        {
            this.InitializeComponent();
            this.DataContext = new MainViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnLockClicked(object sender, RoutedEventArgs e)
        {
            NativeMethods.LockWorkStation();
        }

        private void OnSliderPointerPressed(object sender, PointerPressedEventArgs e)
        {
            var canvas = sender as Canvas;
            var viewModel = DataContext as MainViewModel;
            if (canvas == null || viewModel == null)
                return;

            _activeSliderIsBrightness = string.Equals(canvas.Tag as string, "Brightness", StringComparison.Ordinal);
            _activeSliderCanvas = canvas;
            var position = e.GetPosition(canvas);
            var thumbLeft = _activeSliderIsBrightness
                ? viewModel.SmtcViewModel.BrightnessSliderThumbLeft
                : viewModel.SmtcViewModel.VolumeSliderThumbLeft;
            _sliderPointerOffset = position.X >= thumbLeft && position.X <= thumbLeft + SliderThumbWidth
                ? position.X - thumbLeft
                : SliderThumbWidth / 2;
            e.Device.Capture(canvas);
            UpdateSliderFromPointer(canvas, e, viewModel);
            e.Handled = true;
        }

        private void OnSliderPointerMoved(object sender, PointerEventArgs e)
        {
            var canvas = sender as Canvas;
            var viewModel = DataContext as MainViewModel;
            if (canvas == null || canvas != _activeSliderCanvas || viewModel == null)
                return;

            UpdateSliderFromPointer(canvas, e, viewModel);
            e.Handled = true;
        }

        private void OnSliderPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_activeSliderCanvas == null || sender != _activeSliderCanvas)
                return;

            _activeSliderCanvas = null;
            e.Device.Capture(null);
            e.Handled = true;
        }

        private void UpdateSliderFromPointer(Control canvas, PointerEventArgs e, MainViewModel viewModel)
        {
            var travelWidth = canvas.Bounds.Width - SliderThumbWidth;
            if (travelWidth <= 0)
                return;

            var thumbLeft = e.GetPosition(canvas).X - _sliderPointerOffset;
            var level = Math.Max(0, Math.Min(1, thumbLeft / travelWidth)) * 100;
            if (_activeSliderIsBrightness)
                viewModel.SmtcViewModel.BrightnessLevel = level;
            else
                viewModel.SmtcViewModel.VolumeLevel = level;
        }
    }
}
