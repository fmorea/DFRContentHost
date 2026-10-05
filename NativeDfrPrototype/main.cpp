#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <setupapi.h>
#include <ddk/hidsdi.h>
#include "native_ui.h"

#include <cstdint>
#include <iomanip>
#include <iostream>
#include <string>
#include <vector>

#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "gdi32.lib")

namespace
{
    const int kWidth = 2008;
    const int kHeight = 60;
    const DWORD kPixelFormat = 0x52474241;
    const DWORD kIoctlUpdateFramebuffer =
        ((0x8086UL) << 16) | ((FILE_WRITE_DATA) << 14) | ((0x801UL) << 2);
    const GUID kDfrDisplayInterfaceGuid =
        { 0x2003cacd, 0x9e7c, 0x477c, { 0xab, 0x06, 0xa5, 0xa8, 0xbb, 0xb1, 0xa6, 0x3e } };

#pragma pack(push, 1)
    struct FramebufferHeader
    {
        WORD beginX;
        WORD beginY;
        WORD width;
        WORD height;
        DWORD pixelFormat;
        DWORD requireVerticalFlip;
    };
#pragma pack(pop)

    bool DrawSampleFrame(std::vector<BYTE>& dibPixels, int pressedButton);
    bool SendFrame(HANDLE device, const std::vector<BYTE>& dibPixels);

    int SampleButtonAtX(ULONG touchX)
    {
        const int x = static_cast<int>((static_cast<unsigned long long>(touchX) * kWidth) / 32767);
        const int buttonX[] = { 600, 700, 800 };
        for (int index = 0; index < 3; ++index)
        {
            if (x >= buttonX[index] && x < buttonX[index] + 88)
                return index;
        }
        return -1;
    }

    std::wstring FindDfrDevicePath()
    {
        HDEVINFO devices = SetupDiGetClassDevsW(
            &kDfrDisplayInterfaceGuid,
            NULL,
            NULL,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devices == INVALID_HANDLE_VALUE)
            return std::wstring();

        std::wstring result;
        for (DWORD index = 0; ; ++index)
        {
            SP_DEVICE_INTERFACE_DATA interfaceData = {};
            interfaceData.cbSize = sizeof(interfaceData);
            if (!SetupDiEnumDeviceInterfaces(devices, NULL, &kDfrDisplayInterfaceGuid, index, &interfaceData))
                break;

            DWORD requiredSize = 0;
            SetupDiGetDeviceInterfaceDetailW(devices, &interfaceData, NULL, 0, &requiredSize, NULL);
            if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || requiredSize == 0)
                continue;

            std::vector<BYTE> storage(requiredSize);
            PSP_DEVICE_INTERFACE_DETAIL_DATA_W detail =
                reinterpret_cast<PSP_DEVICE_INTERFACE_DETAIL_DATA_W>(&storage[0]);
            detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
            if (SetupDiGetDeviceInterfaceDetailW(
                    devices, &interfaceData, detail, requiredSize, NULL, NULL))
            {
                result = detail->DevicePath;
                break;
            }
        }

        SetupDiDestroyDeviceInfoList(devices);
        return result;
    }

    int InspectTouchDevices()
    {
        GUID hidGuid = {};
        HidD_GetHidGuid(&hidGuid);
        HDEVINFO devices = SetupDiGetClassDevsW(
            &hidGuid, NULL, NULL, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devices == INVALID_HANDLE_VALUE)
        {
            std::cerr << "Could not enumerate HID interfaces. Win32 error: " << GetLastError() << "\n";
            return 10;
        }

        bool found = false;
        for (DWORD index = 0; ; ++index)
        {
            SP_DEVICE_INTERFACE_DATA interfaceData = {};
            interfaceData.cbSize = sizeof(interfaceData);
            if (!SetupDiEnumDeviceInterfaces(devices, NULL, &hidGuid, index, &interfaceData))
                break;

            DWORD requiredSize = 0;
            SetupDiGetDeviceInterfaceDetailW(devices, &interfaceData, NULL, 0, &requiredSize, NULL);
            if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || requiredSize == 0)
                continue;

            std::vector<BYTE> storage(requiredSize);
            PSP_DEVICE_INTERFACE_DETAIL_DATA_W detail =
                reinterpret_cast<PSP_DEVICE_INTERFACE_DETAIL_DATA_W>(&storage[0]);
            detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
            if (!SetupDiGetDeviceInterfaceDetailW(
                    devices, &interfaceData, detail, requiredSize, NULL, NULL))
                continue;

            HANDLE hid = CreateFileW(detail->DevicePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL, NULL);
            if (hid == INVALID_HANDLE_VALUE)
                continue;

            HIDD_ATTRIBUTES attributes = {};
            attributes.Size = sizeof(attributes);
            const bool isDigitizer = HidD_GetAttributes(hid, &attributes) &&
                attributes.VendorID == 0x05AC && attributes.ProductID == 0x8302;
            if (!isDigitizer)
            {
                CloseHandle(hid);
                continue;
            }

            found = true;
            PHIDP_PREPARSED_DATA preparsedData = NULL;
            if (!HidD_GetPreparsedData(hid, &preparsedData))
            {
                std::cerr << "Could not read digitizer preparsed data.\n";
                CloseHandle(hid);
                continue;
            }

            HIDP_CAPS caps = {};
            const NTSTATUS capsStatus = HidP_GetCaps(preparsedData, &caps);
            std::cout << "Apple digitizer VID=0x" << std::hex << attributes.VendorID
                << " PID=0x" << attributes.ProductID << std::dec << "\n";
            if (capsStatus == HIDP_STATUS_SUCCESS)
            {
                std::cout << "UsagePage=0x" << std::hex << caps.UsagePage
                    << " Usage=0x" << caps.Usage << std::dec
                    << " InputReportBytes=" << caps.InputReportByteLength
                    << " ButtonCaps=" << caps.NumberInputButtonCaps
                    << " ValueCaps=" << caps.NumberInputValueCaps
                    << " DataIndices=" << caps.NumberInputDataIndices << "\n";

                std::vector<HIDP_BUTTON_CAPS> buttonCaps(caps.NumberInputButtonCaps);
                ULONG buttonCapCount = caps.NumberInputButtonCaps;
                if (buttonCapCount > 0 && HidP_GetButtonCaps(
                        HidP_Input, &buttonCaps[0], &buttonCapCount, preparsedData) == HIDP_STATUS_SUCCESS)
                {
                    for (ULONG capIndex = 0; capIndex < buttonCapCount; ++capIndex)
                    {
                        const HIDP_BUTTON_CAPS& button = buttonCaps[capIndex];
                        const USAGE usageMin = button.IsRange ? button.Range.UsageMin : button.NotRange.Usage;
                        const USAGE usageMax = button.IsRange ? button.Range.UsageMax : usageMin;
                        const USHORT dataIndex = button.IsRange ? button.Range.DataIndexMin : button.NotRange.DataIndex;
                        std::cout << "Button Page=0x" << std::hex << button.UsagePage
                            << " Usage=0x" << usageMin;
                        if (usageMax != usageMin)
                            std::cout << "-0x" << usageMax;
                        std::cout << " Link=" << std::dec << button.LinkCollection
                            << " DataIndex=" << dataIndex << "\n";
                    }
                }

                std::vector<HIDP_VALUE_CAPS> valueCaps(caps.NumberInputValueCaps);
                ULONG valueCapCount = caps.NumberInputValueCaps;
                if (valueCapCount > 0 && HidP_GetValueCaps(
                        HidP_Input, &valueCaps[0], &valueCapCount, preparsedData) == HIDP_STATUS_SUCCESS)
                {
                    for (ULONG capIndex = 0; capIndex < valueCapCount; ++capIndex)
                    {
                        const HIDP_VALUE_CAPS& value = valueCaps[capIndex];
                        const USAGE usageMin = value.IsRange ? value.Range.UsageMin : value.NotRange.Usage;
                        const USAGE usageMax = value.IsRange ? value.Range.UsageMax : usageMin;
                        const USHORT dataIndex = value.IsRange ? value.Range.DataIndexMin : value.NotRange.DataIndex;
                        std::cout << "Report=" << static_cast<unsigned>(value.ReportID)
                            << " Page=0x" << std::hex << value.UsagePage
                            << " Usage=0x" << usageMin;
                        if (usageMax != usageMin)
                            std::cout << "-0x" << usageMax;
                        std::cout << " Link=" << std::dec << value.LinkCollection
                            << " DataIndex=" << dataIndex
                            << " Count=" << value.ReportCount
                            << " Bits=" << value.BitSize
                            << " Logical=" << value.LogicalMin << ".." << value.LogicalMax
                            << " Physical=" << value.PhysicalMin << ".." << value.PhysicalMax << "\n";
                    }
                }
            }
            else
            {
                std::cerr << "HidP_GetCaps failed: 0x" << std::hex
                    << static_cast<unsigned long>(capsStatus) << std::dec << "\n";
            }

            HidD_FreePreparsedData(preparsedData);
            CloseHandle(hid);
        }

        SetupDiDestroyDeviceInfoList(devices);
        if (!found)
        {
            std::cerr << "Apple digitizer VID=0x05AC PID=0x8302 not found.\n";
            return 11;
        }

        return 0;
    }

    int WatchTouchInput(HANDLE display)
    {
        GUID hidGuid = {};
        HidD_GetHidGuid(&hidGuid);
        HDEVINFO devices = SetupDiGetClassDevsW(
            &hidGuid, NULL, NULL, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devices == INVALID_HANDLE_VALUE)
        {
            std::cerr << "Could not enumerate HID interfaces. Win32 error: " << GetLastError() << "\n";
            return 20;
        }

        HANDLE digitizer = INVALID_HANDLE_VALUE;
        for (DWORD index = 0; ; ++index)
        {
            SP_DEVICE_INTERFACE_DATA interfaceData = {};
            interfaceData.cbSize = sizeof(interfaceData);
            if (!SetupDiEnumDeviceInterfaces(devices, NULL, &hidGuid, index, &interfaceData))
                break;

            DWORD requiredSize = 0;
            SetupDiGetDeviceInterfaceDetailW(devices, &interfaceData, NULL, 0, &requiredSize, NULL);
            if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || requiredSize == 0)
                continue;

            std::vector<BYTE> storage(requiredSize);
            PSP_DEVICE_INTERFACE_DETAIL_DATA_W detail =
                reinterpret_cast<PSP_DEVICE_INTERFACE_DETAIL_DATA_W>(&storage[0]);
            detail->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
            if (!SetupDiGetDeviceInterfaceDetailW(
                    devices, &interfaceData, detail, requiredSize, NULL, NULL))
                continue;

            HANDLE candidate = CreateFileW(detail->DevicePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL, NULL);
            if (candidate == INVALID_HANDLE_VALUE)
                continue;

            HIDD_ATTRIBUTES attributes = {};
            attributes.Size = sizeof(attributes);
            if (HidD_GetAttributes(candidate, &attributes) &&
                attributes.VendorID == 0x05AC && attributes.ProductID == 0x8302)
            {
                digitizer = candidate;
                break;
            }

            CloseHandle(candidate);
        }
        SetupDiDestroyDeviceInfoList(devices);

        if (digitizer == INVALID_HANDLE_VALUE)
        {
            std::cerr << "Apple digitizer VID=0x05AC PID=0x8302 could not be opened.\n";
            return 21;
        }

        PHIDP_PREPARSED_DATA preparsedData = NULL;
        if (!HidD_GetPreparsedData(digitizer, &preparsedData))
        {
            CloseHandle(digitizer);
            std::cerr << "Could not read digitizer preparsed data.\n";
            return 22;
        }

        HIDP_CAPS caps = {};
        if (HidP_GetCaps(preparsedData, &caps) != HIDP_STATUS_SUCCESS)
        {
            HidD_FreePreparsedData(preparsedData);
            CloseHandle(digitizer);
            std::cerr << "Could not read digitizer capabilities.\n";
            return 23;
        }

        std::vector<BYTE> report(caps.InputReportByteLength);
        bool wasDown[11] = {};
        ULONG previousX[11] = {};
        ULONG previousContactId[11] = {};
        NativeTouchBar* touchBar = NULL;
        if (display != INVALID_HANDLE_VALUE)
        {
            touchBar = new NativeTouchBar(display);
            if (!touchBar->Render())
            {
                std::cerr << "Could not draw the initial interactive frame. Win32 error: " << GetLastError() << "\n";
                delete touchBar;
                HidD_FreePreparsedData(preparsedData);
                CloseHandle(digitizer);
                return 24;
            }
        }

        std::cout << "Native HID touch watcher active. Press Ctrl+C to stop.\n";

        while (true)
        {
            DWORD bytesRead = 0;
            if (!ReadFile(digitizer, &report[0], static_cast<DWORD>(report.size()), &bytesRead, NULL))
            {
                const DWORD error = GetLastError();
                std::cerr << "HID read failed: " << error << "\n";
                break;
            }

            bool anyDown = false;
            ULONG primaryX = 0;
            for (USHORT slot = 0; slot < 11; ++slot)
            {
                USAGE activeUsages[16] = {};
                ULONG usageCount = sizeof(activeUsages) / sizeof(activeUsages[0]);
                const USHORT linkCollection = static_cast<USHORT>(slot + 1);
                const NTSTATUS usageStatus = HidP_GetUsages(HidP_Input, 0x0D,
                    linkCollection, activeUsages, &usageCount, preparsedData,
                    reinterpret_cast<PCHAR>(&report[0]), bytesRead);
                bool isDown = false;
                if (usageStatus == HIDP_STATUS_SUCCESS)
                {
                    for (ULONG usageIndex = 0; usageIndex < usageCount; ++usageIndex)
                    {
                        if (activeUsages[usageIndex] == 0x33)
                        {
                            isDown = true;
                            break;
                        }
                    }
                }

                ULONG contactId = 0;
                ULONG x = 0;
                HidP_GetUsageValue(HidP_Input, 0x0D, linkCollection, 0x38,
                    &contactId, preparsedData, reinterpret_cast<PCHAR>(&report[0]), bytesRead);
                HidP_GetUsageValue(HidP_Input, 0x01, linkCollection, 0x30,
                    &x, preparsedData, reinterpret_cast<PCHAR>(&report[0]), bytesRead);

                if (isDown && !wasDown[slot])
                {
                    const int button = SampleButtonAtX(x);
                    const char* buttonNames[] = { "KEYS", "MEDIA", "STATUS" };
                    std::cout << "Down slot=" << slot << " id=" << contactId << " x=" << x;
                    if (button >= 0)
                        std::cout << " button=" << buttonNames[button];
                    std::cout << "\n";
                }
                else if (!isDown && wasDown[slot])
                    std::cout << "Up slot=" << slot << "\n";
                else if (isDown && (x != previousX[slot] || contactId != previousContactId[slot]))
                    std::cout << "Move slot=" << slot << " id=" << contactId << " x=" << x << "\n";

                wasDown[slot] = isDown;
                previousX[slot] = x;
                previousContactId[slot] = contactId;

                if (isDown && !anyDown)
                {
                    primaryX = x;
                    anyDown = true;
                }
            }

            if (touchBar)
                touchBar->HandleTouch(primaryX, anyDown);
        }

        delete touchBar;
        HidD_FreePreparsedData(preparsedData);
        CloseHandle(digitizer);
        return 0;
    }

    bool DrawSampleFrame(std::vector<BYTE>& dibPixels, int pressedButton)
    {
        BITMAPINFO bitmapInfo = {};
        bitmapInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bitmapInfo.bmiHeader.biWidth = kWidth;
        bitmapInfo.bmiHeader.biHeight = -kHeight;
        bitmapInfo.bmiHeader.biPlanes = 1;
        bitmapInfo.bmiHeader.biBitCount = 32;
        bitmapInfo.bmiHeader.biCompression = BI_RGB;

        HDC memoryDc = CreateCompatibleDC(NULL);
        if (!memoryDc)
            return false;

        void* dibBits = NULL;
        HBITMAP bitmap = CreateDIBSection(memoryDc, &bitmapInfo, DIB_RGB_COLORS, &dibBits, NULL, 0);
        if (!bitmap || !dibBits)
        {
            DeleteDC(memoryDc);
            return false;
        }

        HGDIOBJ previousBitmap = SelectObject(memoryDc, bitmap);
        RECT canvas = { 0, 0, kWidth, kHeight };
        HBRUSH background = CreateSolidBrush(RGB(12, 16, 20));
        FillRect(memoryDc, &canvas, background);
        DeleteObject(background);

        RECT accent = { 0, 0, kWidth, 3 };
        HBRUSH accentBrush = CreateSolidBrush(RGB(67, 184, 220));
        FillRect(memoryDc, &accent, accentBrush);
        DeleteObject(accentBrush);

        HFONT font = CreateFontW(-17, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            ANTIALIASED_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
        HGDIOBJ previousFont = SelectObject(memoryDc, font);
        SetBkMode(memoryDc, TRANSPARENT);
        SetTextColor(memoryDc, RGB(240, 246, 250));
        TextOutW(memoryDc, 20, 21, L"NATIVE C++ FRAMEBUFFER", 22);

        const wchar_t* labels[] = { L"KEYS", L"MEDIA", L"STATUS" };
        const int buttonX[] = { 600, 700, 800 };
        for (int index = 0; index < 3; ++index)
        {
            RECT button = { buttonX[index], 15, buttonX[index] + 88, 47 };
            const COLORREF buttonColor = index == pressedButton
                ? RGB(35, 137, 174)
                : RGB(38, 48, 56);
            HBRUSH buttonBrush = CreateSolidBrush(buttonColor);
            FillRect(memoryDc, &button, buttonBrush);
            DeleteObject(buttonBrush);
            SetTextColor(memoryDc, RGB(215, 232, 240));
            TextOutW(memoryDc, button.left + 12, button.top + 8, labels[index], lstrlenW(labels[index]));
        }

        const BYTE* source = static_cast<const BYTE*>(dibBits);
        dibPixels.assign(source, source + kWidth * kHeight * 4);

        SelectObject(memoryDc, previousFont);
        SelectObject(memoryDc, previousBitmap);
        DeleteObject(font);
        DeleteObject(bitmap);
        DeleteDC(memoryDc);
        return true;
    }

    bool SendFrame(HANDLE device, const std::vector<BYTE>& dibPixels)
    {
        FramebufferHeader header = {};
        header.width = kWidth;
        header.height = kHeight;
        header.pixelFormat = kPixelFormat;

        std::vector<BYTE> request(sizeof(header) + kWidth * kHeight * 3);
        memcpy(&request[0], &header, sizeof(header));

        BYTE* destination = &request[sizeof(header)];
        for (int x = 0; x < kWidth; ++x)
        {
            for (int y = kHeight - 1; y >= 0; --y)
            {
                const BYTE* pixel = &dibPixels[(y * kWidth + x) * 4];
                *destination++ = pixel[2];
                *destination++ = pixel[1];
                *destination++ = pixel[0];
            }
        }

        DWORD returned = 0;
        return DeviceIoControl(device, kIoctlUpdateFramebuffer,
            &request[0], static_cast<DWORD>(request.size()),
            NULL, 0, &returned, NULL) != FALSE;
    }

    LRESULT CALLBACK PreviewWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
    {
        static NativeTouchBar* pBar = NULL;
        if (msg == WM_CREATE)
        {
            CREATESTRUCTW* cs = reinterpret_cast<CREATESTRUCTW*>(lParam);
            pBar = reinterpret_cast<NativeTouchBar*>(cs->lpCreateParams);
            SetTimer(hwnd, 1, 100, NULL);
            return 0;
        }
        if (!pBar) return DefWindowProcW(hwnd, msg, wParam, lParam);

        switch (msg)
        {
            case WM_TIMER:
                pBar->Render();
                InvalidateRect(hwnd, NULL, FALSE);
                return 0;
            case WM_LBUTTONDOWN:
            case WM_MOUSEMOVE:
            {
                if (msg == WM_MOUSEMOVE && !(wParam & MK_LBUTTON)) break;
                RECT client = {};
                GetClientRect(hwnd, &client);
                const int width = client.right - client.left;
                if (width > 0)
                {
                    const int x = LOWORD(lParam);
                    const uint32_t normX = static_cast<uint32_t>(std::max(0, std::min(32767, (x * 32767) / width)));
                    pBar->HandleTouch(normX, true);
                    InvalidateRect(hwnd, NULL, FALSE);
                }
                return 0;
            }
            case WM_LBUTTONUP:
            {
                RECT client = {};
                GetClientRect(hwnd, &client);
                const int width = client.right - client.left;
                if (width > 0)
                {
                    const int x = LOWORD(lParam);
                    const uint32_t normX = static_cast<uint32_t>(std::max(0, std::min(32767, (x * 32767) / width)));
                    pBar->HandleTouch(normX, false);
                    InvalidateRect(hwnd, NULL, FALSE);
                }
                return 0;
            }
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    int RunPreviewWindow()
    {
        std::cout << "Starting Native Touch Bar Desktop Preview Window...\n";
        NativeTouchBar touchBar(INVALID_HANDLE_VALUE);

        const wchar_t CLASS_NAME[] = L"DFRNativePreviewWindow";
        WNDCLASSW wc = {};
        wc.lpfnWndProc = PreviewWndProc;
        wc.hInstance = GetModuleHandle(NULL);
        wc.lpszClassName = CLASS_NAME;
        wc.hCursor = LoadCursor(NULL, IDC_ARROW);
        wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
        RegisterClassW(&wc);

        HWND hwnd = CreateWindowExW(
            WS_EX_TOPMOST,
            CLASS_NAME, L"Native DFR Touch Bar Desktop Preview",
            WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_VISIBLE,
            100, 100, 1016, 69,
            NULL, NULL, GetModuleHandle(NULL), &touchBar);

        MSG msg = {};
        while (GetMessageW(&msg, NULL, 0, 0))
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return 0;
    }
}

int main(int argc, char** argv)
{
    if (argc == 2 && std::string(argv[1]) == "--inspect-touch")
        return InspectTouchDevices();
    if (argc == 2 && std::string(argv[1]) == "--watch-touch")
        return WatchTouchInput(INVALID_HANDLE_VALUE);
    if (argc == 2 && (std::string(argv[1]) == "--preview" || std::string(argv[1]) == "--self-host"))
        return RunPreviewWindow();
    if (argc == 2 && std::string(argv[1]) == "--interactive")
    {
        const std::wstring devicePath = FindDfrDevicePath();
        if (devicePath.empty())
        {
            std::cerr << "DFR display interface not found.\n";
            return 30;
        }

        HANDLE display = CreateFileW(devicePath.c_str(), GENERIC_WRITE, 0, NULL,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
        if (display == INVALID_HANDLE_VALUE)
        {
            std::cerr << "Could not open DFR display. Win32 error: " << GetLastError() << "\n";
            return 31;
        }

        const int result = WatchTouchInput(display);
        CloseHandle(display);
        return result;
    }

    const std::wstring devicePath = FindDfrDevicePath();
    if (devicePath.empty())
    {
        std::cerr << "DFR display interface not found.\n";
        return 1;
    }

    HANDLE device = CreateFileW(devicePath.c_str(), GENERIC_WRITE, 0, NULL,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (device == INVALID_HANDLE_VALUE)
    {
        std::cerr << "Could not open DFR display. Win32 error: " << GetLastError() << "\n";
        return 2;
    }

    std::vector<BYTE> dibPixels;
    const bool drawn = DrawSampleFrame(dibPixels, -1);
    const bool sent = drawn && SendFrame(device, dibPixels);
    const DWORD error = sent ? ERROR_SUCCESS : GetLastError();
    CloseHandle(device);

    if (!sent)
    {
        std::cerr << "Could not send the native sample frame. Win32 error: " << error << "\n";
        return 3;
    }

    std::cout << "Native GDI sample frame sent to the DFR display.\n";
    return 0;
}