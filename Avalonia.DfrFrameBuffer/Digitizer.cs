using Avalonia.DfrFrameBuffer.Device.Hid;
using Avalonia.Input;
using Avalonia.Input.Raw;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;
using System;
using System.Linq;

namespace Avalonia.DfrFrameBuffer
{
    public class Digitizer
    {
        private readonly double _scale;
        private readonly double _width;
        private readonly double _height;
        private readonly Vector _dpi;

        private HidDevice _digitizer;
        private ReportDescriptor _reportDescr;
        private HidStream _hidStream;

        private HidDeviceInputReceiver _hidDeviceInputReceiver;
        private DeviceItemInputParser _hiddeviceInputParser;
        private readonly object _parserLock = new object();

        private int _prevTappedSlotIndex;

        public event Action<RawInputEventArgs> Event;

        public Digitizer(double physicalWidth, double physicalHeight, Vector? dpi = null)
        {
            // 96 DPI is the standard 100% scale
            _dpi = dpi ?? new Vector(192, 192);
            _scale = 96 / _dpi.X;
            _width = physicalWidth * _scale;
            _height = physicalHeight * _scale;

            _prevTappedSlotIndex = -1;

            // Discover DFR digitizer device
            _digitizer = DeviceList.Local.GetHidDeviceOrNull(0x05ac, 0x8302);
            if (_digitizer == null)
            {
                throw new Exception("iBridge HID digitizer not found");
            }

            _reportDescr = _digitizer.GetReportDescriptor();
        }

        public void Start()
        {
            if (_digitizer.TryOpen(out _hidStream))
            {
                _hidDeviceInputReceiver = _reportDescr.CreateHidDeviceInputReceiver();
                _hiddeviceInputParser = _reportDescr.DeviceItems[0].CreateDeviceItemInputParser();
                _hidDeviceInputReceiver.Received += OnDigitizerInputReceived;
                _hidDeviceInputReceiver.Start(_hidStream);
            }
            else
            {
                throw new Exception("Failed to open iBridge HID digitizer");
            }
        }

        private void OnDigitizerInputReceived(object sender, EventArgs e)
        {
            var inputReportBuffer = new byte[_digitizer.GetMaxInputReportLength()];
            while (_hidDeviceInputReceiver.TryRead(inputReportBuffer, 0, out Report report))
            {
                TouchReport[] currentReports;
                lock (_parserLock)
                {
                    // Snapshot parser values before another HID report can overwrite them.
                    if (!_hiddeviceInputParser.TryParseReport(inputReportBuffer, 0, report) ||
                        !_hiddeviceInputParser.HasChanged)
                        continue;

                    currentReports = ReadCurrentReports();
                }

                BridgeFrameBufferPlatform.Threading.Send(() => ProcessEvent(currentReports));
            }
        }

        private TouchReport[] ReadCurrentReports()
        {
            var reports = new TouchReport[11];
            for (var slot = 0; slot < reports.Length; slot++)
                reports[slot] = new TouchReport(0, 32767, false);

            var slotIndex = -1;
            for (var index = 0; index < _hiddeviceInputParser.ValueCount; index++)
            {
                var data = _hiddeviceInputParser.GetValue(index);
                if (data.Usages.FirstOrDefault() != VendorUsage.FingerIdentifier)
                    continue;

                slotIndex++;
                if (slotIndex >= reports.Length || index + 4 >= _hiddeviceInputParser.ValueCount)
                    break;

                var fingerTapData = _hiddeviceInputParser.GetValue(index + 1);
                var xData = _hiddeviceInputParser.GetValue(index + 3);
                reports[slotIndex] = new TouchReport(xData.GetPhysicalValue(),
                    xData.DataItem.PhysicalMaximum, fingerTapData.GetPhysicalValue() != 0);
            }

            return reports;
        }

        private void ProcessEvent(TouchReport[] currentReports)
        {
            if (currentReports == null || currentReports.Length != 11)
                return;

            // Check if need to raise touch leave event for prev slot
            if (_prevTappedSlotIndex >= 0)
            {
                var previousReport = currentReports[_prevTappedSlotIndex];
                var scaledX = previousReport.GetXInPercentage() * _width;

                if (!previousReport.FingerStatus)
                {
                    Event?.Invoke(new RawMouseEventArgs(
                        BridgeFrameBufferPlatform.MouseDevice,
                        BridgeFrameBufferPlatform.Timestamp,
                        BridgeFrameBufferPlatform.TopLevel.InputRoot,
                        RawMouseEventType.LeftButtonUp,
                        new Point(scaledX, _height / 2),
                        default));

                    _prevTappedSlotIndex = -1;
                }
                else if (BridgeFrameBufferPlatform.MouseDevice.Captured != null)
                {
                    Event?.Invoke(new RawMouseEventArgs(
                        BridgeFrameBufferPlatform.MouseDevice,
                        BridgeFrameBufferPlatform.Timestamp,
                        BridgeFrameBufferPlatform.TopLevel.InputRoot,
                        RawMouseEventType.Move,
                        new Point(scaledX, _height / 2),
                        InputModifiers.LeftMouseButton));
                }
            }

            // Can raise new tap event
            if (_prevTappedSlotIndex == -1)
            {
                for (var slot = 0; slot < currentReports.Length; slot++)
                {
                    if (!currentReports[slot].FingerStatus)
                        continue;

                    var scaledX = currentReports[slot].GetXInPercentage() * _width;
                    Event?.Invoke(new RawMouseEventArgs(
                        BridgeFrameBufferPlatform.MouseDevice,
                        BridgeFrameBufferPlatform.Timestamp,
                        BridgeFrameBufferPlatform.TopLevel.InputRoot,
                        RawMouseEventType.LeftButtonDown,
                        new Point(scaledX, _height / 2),
                        default));

                    _prevTappedSlotIndex = slot;
                    break;
                }
            }
        }
    }
}
