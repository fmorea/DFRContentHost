#define WIN32_LEAN_AND_MEAN
#include "native_ui.h"
#include <mmsystem.h>
#include <powrprof.h>

#include <algorithm>
#include <cmath>
#include <cwchar>
#include <cwctype>
#include <cstring>

#ifndef PROCESS_QUERY_LIMITED_INFORMATION
#define PROCESS_QUERY_LIMITED_INFORMATION 0x1000
#endif

extern "C" BOOL WINAPI QueryFullProcessImageNameW(
    HANDLE process, DWORD flags, LPWSTR imageName, PDWORD size);

namespace
{
    const int kWidth = 2008;
    const int kHeight = 60;
    const int kSliderPanelWidth = 712;
    const int kSliderCanvasWidth = 560;
    const int kSliderTravel = 520;
    const int kThumbWidth = 40;
    const DWORD kPixelFormat = 0x52474241;
    const DWORD kIoctlUpdateFramebuffer =
        ((0x8086UL) << 16) | ((FILE_WRITE_DATA) << 14) | ((0x801UL) << 2);
    const GUID kDisplaySubgroup =
        { 0x7516b95f, 0xf776, 0x4464, { 0x8c, 0x53, 0x06, 0x17, 0xf4, 0x0c, 0x99, 0 } };
    const GUID kBrightnessSetting =
        { 0xaded5e82, 0xb909, 0x4619, { 0x99, 0x49, 0xf5, 0xd7, 0x1d, 0xac, 0x0b, 0xcb } };

    HMODULE GetPowerModule()
    {
        static HMODULE module = LoadLibraryW(L"powrprof.dll");
        return module;
    }

    template <typename Function>
    Function ResolvePowerFunction(const char* name)
    {
        return reinterpret_cast<Function>(GetProcAddress(GetPowerModule(), name));
    }

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

    const wchar_t* kDigits = L"ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    const wchar_t* kSymbols = L"1234567890-=[]\\;',./";

    void QueryAudioEndpointVolume(double& outLevel, bool& outMuted)
    {
        DWORD waveVolume = 0;
        if (waveOutGetVolume(NULL, &waveVolume) == MMSYSERR_NOERROR)
        {
            const DWORD left = LOWORD(waveVolume);
            const DWORD right = HIWORD(waveVolume);
            outLevel = ((left + right) * 100.0) / (2.0 * 65535.0);
            outMuted = (outLevel <= 0);
        }
    }

    void SetAudioEndpointVolume(double level)
    {
        const DWORD scalar = static_cast<DWORD>(std::round(std::max(0.0, std::min(100.0, level)) * 65535.0 / 100.0));
        const DWORD packedVolume = scalar | (scalar << 16);
        waveOutSetVolume(NULL, packedVolume);
    }
}

NativeTouchBar::NativeTouchBar(HANDLE displayDevice)
    : _displayDevice(displayDevice),
      _viewMode(ViewMode::Desktop),
      _fnPressed(false),
      _fnExpanded(false),
      _keyboardVisible(false),
      _capsActive(true),
      _numericMode(false),
      _pointerDown(false),
      _batteryAvailable(false),
      _activeButton(-1),
      _pressedButton(-1),
      _keyboardOffset(0),
      _batteryLevel(0),
      _wifiSignalBars(4),
      _volumeLevel(50),
      _brightnessLevel(50),
      _volumeMuted(false)
{
    RefreshSystemState();
    BuildButtons();
}

void NativeTouchBar::SetFnPressed(bool pressed)
{
    if (_fnPressed != pressed)
    {
        _fnPressed = pressed;
        BuildButtons();
        Render();
    }
}

void NativeTouchBar::AddButton(int x, int width, const std::wstring& label, const std::wstring& glyph,
    Action action, USHORT key, bool active, bool enabled)
{
    Button button = {};
    button.bounds.left = x;
    button.bounds.top = 5;
    button.bounds.right = x + width;
    button.bounds.bottom = 55;
    button.label = label;
    button.glyph = glyph;
    button.action = action;
    button.key = key;
    button.active = active;
    button.enabled = enabled;
    _buttons.push_back(button);
}

void NativeTouchBar::BuildButtons()
{
    _buttons.clear();
    RefreshSystemState();
    AddButton(8, 60, L"Fn", L"", Action::ToggleFn, 0, _fnExpanded || _fnPressed);
    AddButton(74, 62, L"Az", L"", Action::ToggleKeyboard, 0, _keyboardVisible);

    int x = 144;
    if (_viewMode == ViewMode::Volume)
    {
        AddButton((kWidth - kSliderPanelWidth) / 2, kSliderPanelWidth,
            L"", L"", Action::SliderVolume);
    }
    else if (_viewMode == ViewMode::Brightness)
    {
        AddButton((kWidth - kSliderPanelWidth) / 2, kSliderPanelWidth,
            L"", L"", Action::SliderBrightness);
    }
    else if (_fnExpanded || _fnPressed)
    {
        for (int key = 0; key < 12; ++key)
        {
            wchar_t functionLabel[4] = { L'F', 0, 0, 0 };
            const int functionNumber = key + 1;
            if (functionNumber < 10)
            {
                functionLabel[1] = static_cast<wchar_t>(L'0' + functionNumber);
            }
            else
            {
                functionLabel[1] = L'1';
                functionLabel[2] = L'0';
            }
            AddButton(x, 62, functionLabel, L"", Action::Key,
                static_cast<USHORT>(VK_F1 + key));
            x += 66;
        }
    }
    else if (_keyboardVisible)
    {
        AddButton(x, 46, L"<", L"", Action::PageLeft, 0, false, _keyboardOffset > 0);
        x += 50;
        AddButton(x, 54, L"Aa", L"", Action::ToggleCaps, 0, _capsActive);
        x += 58;
        AddButton(x, 64, _numericMode ? L"ABC" : L"?123", L"", Action::ToggleNumbers, 0, _numericMode);
        x += 70;

        const int total = _numericMode ? static_cast<int>(wcslen(kSymbols)) : 26;
        const int end = std::min(_keyboardOffset + 8, total);
        for (int index = _keyboardOffset; index < end; ++index)
        {
            wchar_t character = _numericMode ? kSymbols[index] : kDigits[index];
            if (!_capsActive && !_numericMode)
                character = static_cast<wchar_t>(towlower(character));
            AddButton(x, 56, std::wstring(1, character), L"", Action::Character,
                static_cast<USHORT>(character));
            x += 60;
        }
        AddButton(x, 46, L">", L"", Action::PageRight, 0, false, end < total);
        x += 52;
        AddButton(x, 62, L"Space", L"", Action::Character, L' '); x += 68;
        AddButton(x, 58, L"Bksp", L"", Action::Key, VK_BACK); x += 64;
        AddButton(x, 52, L"Enter", L"", Action::Key, VK_RETURN); x += 58;
        AddButton(x, 48, L"Tab", L"", Action::Key, VK_TAB); x += 54;
        AddButton(x, 48, L"Esc", L"", Action::Key, VK_ESCAPE);
    }
    else
    {
        AddContextButtons(x);
    }

    const int statusX = 1550;
    AddButton(statusX, 104, _clockText, L"", Action::None);
    wchar_t batteryText[5] = { L'-', L'-', L'%', 0, 0 };
    if (_batteryAvailable)
    {
        if (_batteryLevel >= 100)
        {
            batteryText[0] = L'1'; batteryText[1] = L'0'; batteryText[2] = L'0'; batteryText[3] = L'%';
        }
        else if (_batteryLevel >= 10)
        {
            batteryText[0] = static_cast<wchar_t>(L'0' + _batteryLevel / 10);
            batteryText[1] = static_cast<wchar_t>(L'0' + _batteryLevel % 10);
            batteryText[2] = L'%';
        }
        else
        {
            batteryText[0] = static_cast<wchar_t>(L'0' + _batteryLevel);
            batteryText[1] = L'%';
            batteryText[2] = 0;
        }
    }
    AddButton(statusX + 110, 82, batteryText, L"", Action::None);
    AddButton(statusX + 198, 62, L"", L"\xE701", Action::None);
    const wchar_t* volumeGlyph = _volumeLevel < 1 ? L"\xE74F" :
        (_volumeLevel < 33 ? L"\xE993" : (_volumeLevel < 66 ? L"\xE994" : L"\xE995"));
    AddButton(1790, 60, L"", volumeGlyph, Action::ToggleVolume, 0, _viewMode == ViewMode::Volume);
    AddButton(1858, 60, L"", L"\xE706", Action::ToggleBrightness, 0, _viewMode == ViewMode::Brightness);
    AddButton(1926, 64, L"", L"\xE1F6", Action::Lock);
}

void NativeTouchBar::RefreshSystemState()
{
    HWND foreground = GetForegroundWindow();
    DWORD processId = 0;
    GetWindowThreadProcessId(foreground, &processId);
    HANDLE process = processId ? OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId) : NULL;
    if (!process && processId)
    {
        process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, processId);
    }
    if (process)
    {
        wchar_t imagePath[1024] = {};
        DWORD pathLength = sizeof(imagePath) / sizeof(imagePath[0]);
        BOOL success = FALSE;

        typedef BOOL (WINAPI *QueryFullProcessImageNameW_t)(HANDLE, DWORD, LPWSTR, PDWORD);
        static QueryFullProcessImageNameW_t pQueryFullProcessImageNameW =
            reinterpret_cast<QueryFullProcessImageNameW_t>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "QueryFullProcessImageNameW"));

        if (pQueryFullProcessImageNameW)
        {
            success = pQueryFullProcessImageNameW(process, 0, imagePath, &pathLength);
        }
        else
        {
            // Direct call fallback using declared symbol
            success = QueryFullProcessImageNameW(process, 0, imagePath, &pathLength);
        }

        if (!success)
        {
            // PSAPI fallback (GetModuleFileNameExW) for older Win32/MinGW compatibility
            typedef DWORD (WINAPI *GetModuleFileNameExW_t)(HANDLE, HMODULE, LPWSTR, DWORD);
            static HMODULE hPsapi = LoadLibraryW(L"psapi.dll");
            static GetModuleFileNameExW_t pGetModuleFileNameExW = hPsapi ?
                reinterpret_cast<GetModuleFileNameExW_t>(GetProcAddress(hPsapi, "GetModuleFileNameExW")) : NULL;
            if (pGetModuleFileNameExW)
            {
                DWORD ret = pGetModuleFileNameExW(process, NULL, imagePath, sizeof(imagePath) / sizeof(imagePath[0]));
                if (ret > 0)
                {
                    pathLength = ret;
                    success = TRUE;
                }
            }
        }

        if (success)
        {
            std::wstring path(imagePath, pathLength);
            const std::size_t slash = path.find_last_of(L"\\/");
            _foregroundProcess = slash == std::wstring::npos ? path : path.substr(slash + 1);
            std::transform(_foregroundProcess.begin(), _foregroundProcess.end(), _foregroundProcess.begin(), towlower);
        }
        CloseHandle(process);
    }

    wchar_t title[256] = {};
    if (foreground && GetWindowTextW(foreground, title, sizeof(title) / sizeof(title[0])) > 0)
    {
        _foregroundTitle = title;
        std::transform(_foregroundTitle.begin(), _foregroundTitle.end(), _foregroundTitle.begin(), towlower);
    }

    SYSTEMTIME localTime = {};
    GetLocalTime(&localTime);
    wchar_t timeText[6] = {};
    timeText[0] = static_cast<wchar_t>(L'0' + localTime.wHour / 10);
    timeText[1] = static_cast<wchar_t>(L'0' + localTime.wHour % 10);
    timeText[2] = L':';
    timeText[3] = static_cast<wchar_t>(L'0' + localTime.wMinute / 10);
    timeText[4] = static_cast<wchar_t>(L'0' + localTime.wMinute % 10);
    _clockText.assign(timeText, 5);

    SYSTEM_POWER_STATUS powerStatus = {};
    if (GetSystemPowerStatus(&powerStatus) && powerStatus.BatteryLifePercent != 255)
    {
        _batteryAvailable = true;
        _batteryLevel = powerStatus.BatteryLifePercent;
    }
    else
    {
        _batteryAvailable = false;
    }

    if (_viewMode != ViewMode::Volume)
    {
        QueryAudioEndpointVolume(_volumeLevel, _volumeMuted);
    }

    typedef DWORD (WINAPI *GetActiveSchemeFunction)(HKEY, GUID**);
    typedef DWORD (WINAPI *ReadAcValueFunction)(HKEY, const GUID*, const GUID*, const GUID*, LPDWORD);
    GetActiveSchemeFunction getActiveScheme = ResolvePowerFunction<GetActiveSchemeFunction>("PowerGetActiveScheme");
    ReadAcValueFunction readAcValue = ResolvePowerFunction<ReadAcValueFunction>("PowerReadACValueIndex");
    GUID* activeScheme = NULL;
    if (_viewMode != ViewMode::Brightness && getActiveScheme && readAcValue &&
        getActiveScheme(NULL, &activeScheme) == ERROR_SUCCESS && activeScheme)
    {
        DWORD brightness = 0;
        if (readAcValue(NULL, activeScheme, &kDisplaySubgroup,
                &kBrightnessSetting, &brightness) == ERROR_SUCCESS)
            _brightnessLevel = std::min<DWORD>(100, brightness);
        LocalFree(activeScheme);
    }
}

int NativeTouchBar::HitTest(int x) const
{
    for (std::size_t index = 0; index < _buttons.size(); ++index)
    {
        const RECT& bounds = _buttons[index].bounds;
        if (x >= bounds.left && x < bounds.right && _buttons[index].enabled)
            return static_cast<int>(index);
    }
    return -1;
}

void NativeTouchBar::UpdateSlider(int x)
{
    const int left = (kWidth - kSliderPanelWidth) / 2 + 32;
    const double level = std::max(0.0, std::min(1.0, (x - left - kThumbWidth / 2.0) / kSliderTravel));
    if (_viewMode == ViewMode::Volume)
        _volumeLevel = level * 100.0;
    else if (_viewMode == ViewMode::Brightness)
        _brightnessLevel = level * 100.0;
}

void NativeTouchBar::ApplyVolume()
{
    SetAudioEndpointVolume(_volumeLevel);
    _volumeMuted = (_volumeLevel <= 0);
}

void NativeTouchBar::ApplyBrightness()
{
    typedef DWORD (WINAPI *GetActiveSchemeFunction)(HKEY, GUID**);
    typedef DWORD (WINAPI *WriteValueFunction)(HKEY, const GUID*, const GUID*, const GUID*, DWORD);
    typedef DWORD (WINAPI *SetActiveSchemeFunction)(HKEY, const GUID*);
    GetActiveSchemeFunction getActiveScheme = ResolvePowerFunction<GetActiveSchemeFunction>("PowerGetActiveScheme");
    WriteValueFunction writeAcValue = ResolvePowerFunction<WriteValueFunction>("PowerWriteACValueIndex");
    WriteValueFunction writeDcValue = ResolvePowerFunction<WriteValueFunction>("PowerWriteDCValueIndex");
    SetActiveSchemeFunction setActiveScheme = ResolvePowerFunction<SetActiveSchemeFunction>("PowerSetActiveScheme");
    if (!getActiveScheme || !writeAcValue || !writeDcValue || !setActiveScheme)
        return;

    GUID* activeScheme = NULL;
    if (getActiveScheme(NULL, &activeScheme) != ERROR_SUCCESS || !activeScheme)
        return;

    const DWORD brightness = static_cast<DWORD>(std::round(std::max(0.0, std::min(100.0, _brightnessLevel))));
    writeAcValue(NULL, activeScheme, &kDisplaySubgroup, &kBrightnessSetting, brightness);
    writeDcValue(NULL, activeScheme, &kDisplaySubgroup, &kBrightnessSetting, brightness);
    setActiveScheme(NULL, activeScheme);
    LocalFree(activeScheme);
}

void NativeTouchBar::HandleTouch(std::uint32_t normalizedX, bool pressed)
{
    const int x = static_cast<int>((static_cast<unsigned long long>(normalizedX) * kWidth) / 32767);
    if (pressed)
    {
        if (!_pointerDown)
        {
            _pointerDown = true;
            _activeButton = HitTest(x);
            _pressedButton = _activeButton;
            if (_activeButton >= 0 && (_buttons[_activeButton].action == Action::SliderVolume ||
                _buttons[_activeButton].action == Action::SliderBrightness))
                UpdateSlider(x);
        }
        else if (_viewMode == ViewMode::Volume || _viewMode == ViewMode::Brightness)
        {
            UpdateSlider(x);
        }
        Render();
        return;
    }

    if (!_pointerDown)
        return;

    _pointerDown = false;
    if (_activeButton >= 0)
    {
        const int releasedButton = HitTest(x);
        const Action action = _buttons[_activeButton].action;
        if (releasedButton == _activeButton || action == Action::SliderVolume || action == Action::SliderBrightness)
            InvokeButton(_activeButton);
    }
    _activeButton = -1;
    _pressedButton = -1;
    BuildButtons();
    Render();
}

void NativeTouchBar::InvokeButton(int index)
{
    if (index < 0 || index >= static_cast<int>(_buttons.size()))
        return;

    const Button button = _buttons[index];
    switch (button.action)
    {
        case Action::Key: SendKey(button.key, button.key == VK_LWIN); break;
        case Action::Chord: SendChord(button.modifiers, button.modifierCount, button.key); break;
        case Action::Character:
        {
            INPUT input[2] = {};
            input[0].type = INPUT_KEYBOARD;
            input[0].ki.wScan = button.key;
            input[0].ki.dwFlags = KEYEVENTF_UNICODE;
            input[1].type = INPUT_KEYBOARD;
            input[1].ki.wScan = button.key;
            input[1].ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            SendInput(2, input, sizeof(INPUT));
            break;
        }
        case Action::ToggleFn: _fnExpanded = !_fnExpanded; _keyboardVisible = false; break;
        case Action::ToggleKeyboard: _keyboardVisible = !_keyboardVisible; _fnExpanded = false; break;
        case Action::ToggleCaps: _capsActive = !_capsActive; break;
        case Action::ToggleNumbers: _numericMode = !_numericMode; _keyboardOffset = 0; break;
        case Action::PageLeft: _keyboardOffset = std::max(0, _keyboardOffset - 8); break;
        case Action::PageRight:
        {
            const int total = _numericMode ? static_cast<int>(wcslen(kSymbols)) : 26;
            if (_keyboardOffset + 8 < total) _keyboardOffset += 8;
            break;
        }
        case Action::ToggleVolume:
            if (_viewMode != ViewMode::Volume)
            {
                if (_viewMode == ViewMode::Brightness) ApplyBrightness();
                _viewMode = ViewMode::Volume;
                _keyboardVisible = false;
            }
            else
            {
                ApplyVolume();
                _viewMode = ViewMode::Desktop;
            }
            break;
        case Action::ToggleBrightness:
            if (_viewMode != ViewMode::Brightness)
            {
                if (_viewMode == ViewMode::Volume) ApplyVolume();
                _viewMode = ViewMode::Brightness;
                _keyboardVisible = false;
            }
            else
            {
                ApplyBrightness();
                _viewMode = ViewMode::Desktop;
            }
            break;
        case Action::SliderVolume: ApplyVolume(); break;
        case Action::SliderBrightness: ApplyBrightness(); break;
        case Action::Lock: LockWorkStation(); break;
        default: break;
    }
}

void NativeTouchBar::SendKey(USHORT key, bool extended)
{
    INPUT input[2] = {};
    input[0].type = INPUT_KEYBOARD;
    input[0].ki.wVk = key;
    input[0].ki.dwFlags = extended ? KEYEVENTF_EXTENDEDKEY : 0;
    input[1].type = INPUT_KEYBOARD;
    input[1].ki.wVk = key;
    input[1].ki.dwFlags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP;
    SendInput(2, input, sizeof(INPUT));
}

void NativeTouchBar::SendChord(const USHORT* modifiers, int modifierCount, USHORT key)
{
    INPUT inputs[10] = {};
    if (modifierCount < 0 || modifierCount > 4)
        return;
    for (int index = 0; index < modifierCount; ++index)
    {
        inputs[index].type = INPUT_KEYBOARD;
        inputs[index].ki.wVk = modifiers[index];
    }
    const int keyDown = modifierCount;
    inputs[keyDown].type = INPUT_KEYBOARD;
    inputs[keyDown].ki.wVk = key;
    inputs[keyDown + 1].type = INPUT_KEYBOARD;
    inputs[keyDown + 1].ki.wVk = key;
    inputs[keyDown + 1].ki.dwFlags = KEYEVENTF_KEYUP;
    for (int index = 0; index < modifierCount; ++index)
    {
        const int releaseIndex = keyDown + 2 + index;
        inputs[releaseIndex].type = INPUT_KEYBOARD;
        inputs[releaseIndex].ki.wVk = modifiers[modifierCount - index - 1];
        inputs[releaseIndex].ki.dwFlags = KEYEVENTF_KEYUP;
    }
    SendInput(static_cast<UINT>((modifierCount + 1) * 2), inputs, sizeof(INPUT));
}

bool NativeTouchBar::Render()
{
    RefreshSystemState();
    BuildButtons();
    return DrawFrame(_buttons);
}

bool NativeTouchBar::DrawFrame(const std::vector<Button>& buttons)
{
    BITMAPINFO info = {};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = kWidth;
    info.bmiHeader.biHeight = -kHeight;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    HDC dc = CreateCompatibleDC(NULL);
    if (!dc) return false;
    void* bits = NULL;
    HBITMAP bitmap = CreateDIBSection(dc, &info, DIB_RGB_COLORS, &bits, NULL, 0);
    if (!bitmap || !bits) { DeleteDC(dc); return false; }
    HGDIOBJ oldBitmap = SelectObject(dc, bitmap);

    RECT full = { 0, 0, kWidth, kHeight };
    HBRUSH background = CreateSolidBrush(RGB(0, 0, 0));
    FillRect(dc, &full, background);
    DeleteObject(background);

    HFONT font = CreateFontW(-21, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
        DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
    HGDIOBJ oldFont = SelectObject(dc, font);
    SetBkMode(dc, TRANSPARENT);

    for (std::size_t index = 0; index < buttons.size(); ++index)
    {
        const Button& button = buttons[index];
        if (button.action == Action::SliderVolume || button.action == Action::SliderBrightness)
        {
            const COLORREF panelColor = RGB(189, 235, 250);
            HBRUSH panelBrush = CreateSolidBrush(panelColor);
            SetBkColor(dc, panelColor);
            FillRect(dc, &button.bounds, panelBrush);
            DeleteObject(panelBrush);

            const int trackStart = button.bounds.left + 32;
            const int trackEnd = trackStart + kSliderTravel;
            const double level = button.action == Action::SliderVolume ? _volumeLevel : _brightnessLevel;
            const int thumbCenter = trackStart + static_cast<int>(std::round(kSliderTravel * level / 100.0));
            RECT track = { trackStart, 28, trackEnd, 32 };
            HBRUSH trackBrush = CreateSolidBrush(RGB(115, 154, 170));
            FillRect(dc, &track, trackBrush);
            DeleteObject(trackBrush);

            RECT fill = { trackStart, 28, thumbCenter, 32 };
            HBRUSH fillBrush = CreateSolidBrush(RGB(49, 127, 156));
            FillRect(dc, &fill, fillBrush);
            DeleteObject(fillBrush);

            RECT thumb = { thumbCenter - 20, 20, thumbCenter + 20, 40 };
            HBRUSH thumbBrush = CreateSolidBrush(RGB(255, 255, 255));
            HGDIOBJ oldBrush = SelectObject(dc, thumbBrush);
            HGDIOBJ oldPen = SelectObject(dc, GetStockObject(NULL_PEN));
            Ellipse(dc, thumb.left, thumb.top, thumb.right, thumb.bottom);
            SelectObject(dc, oldPen);
            SelectObject(dc, oldBrush);
            DeleteObject(thumbBrush);

            wchar_t percentage[8] = {};
            const int value = static_cast<int>(std::round(level));
            percentage[0] = static_cast<wchar_t>(L'0' + (value / 100) % 10);
            percentage[1] = static_cast<wchar_t>(L'0' + (value / 10) % 10);
            percentage[2] = static_cast<wchar_t>(L'0' + value % 10);
            percentage[3] = L'%';
            RECT percentageRect = { trackEnd + 22, 10, trackEnd + 112, 50 };
            SetTextColor(dc, RGB(38, 57, 66));
            DrawTextW(dc, percentage, 4, &percentageRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            continue;
        }

        const bool pressed = static_cast<int>(index) == _pressedButton;
        COLORREF color = !button.enabled ? RGB(32, 32, 34) :
            (pressed || button.active ? RGB(0, 120, 215) : RGB(40, 40, 40));
        HBRUSH brush = CreateSolidBrush(color);
        RoundRect(dc, button.bounds.left, button.bounds.top, button.bounds.right,
            button.bounds.bottom, 14, 14);
        SetBkColor(dc, color);
        FillRect(dc, &button.bounds, brush);
        DeleteObject(brush);
        SetTextColor(dc, button.enabled ? RGB(245, 245, 245) : RGB(110, 110, 110));

        if (button.key == VK_LWIN)
        {
            // Draw 2x2 Windows Logo squares matching MainView.xaml
            const int centerX = (button.bounds.left + button.bounds.right) / 2;
            const int centerY = (button.bounds.top + button.bounds.bottom) / 2;
            HBRUSH whiteBrush = CreateSolidBrush(RGB(255, 255, 255));
            RECT sq1 = { centerX - 9, centerY - 9, centerX - 1, centerY - 1 };
            RECT sq2 = { centerX + 1, centerY - 9, centerX + 9, centerY - 1 };
            RECT sq3 = { centerX - 9, centerY + 1, centerX - 1, centerY + 9 };
            RECT sq4 = { centerX + 1, centerY + 1, centerX + 9, centerY + 9 };
            FillRect(dc, &sq1, whiteBrush);
            FillRect(dc, &sq2, whiteBrush);
            FillRect(dc, &sq3, whiteBrush);
            FillRect(dc, &sq4, whiteBrush);
            DeleteObject(whiteBrush);
            continue;
        }

        if (button.glyph == L"\xE701")
        {
            // Draw 4-bar Wi-Fi signal strength indicator matching MainView.xaml
            const int centerX = (button.bounds.left + button.bounds.right) / 2;
            const int bottomY = (button.bounds.top + button.bounds.bottom) / 2 + 10;
            const int heights[4] = { 6, 11, 16, 21 };
            const int startX = centerX - 10;
            HBRUSH activeBrush = CreateSolidBrush(RGB(255, 255, 255));
            HBRUSH dimBrush = CreateSolidBrush(RGB(64, 64, 68));
            for (int bar = 0; bar < 4; ++bar)
            {
                RECT barRect = { startX + bar * 6, bottomY - heights[bar], startX + bar * 6 + 4, bottomY };
                FillRect(dc, &barRect, (bar < _wifiSignalBars) ? activeBrush : dimBrush);
            }
            DeleteObject(activeBrush);
            DeleteObject(dimBrush);
            continue;
        }

        const std::wstring& text = button.glyph.empty() ? button.label : button.glyph;
        if (!text.empty())
        {
            RECT textRect = button.bounds;
            DrawTextW(dc, text.c_str(), static_cast<int>(text.size()), &textRect,
                DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
        }
    }

    SelectObject(dc, oldFont);
    SelectObject(dc, oldBitmap);
    std::vector<BYTE> pixels(static_cast<BYTE*>(bits), static_cast<BYTE*>(bits) + kWidth * kHeight * 4);
    DeleteObject(font);
    DeleteObject(bitmap);
    DeleteDC(dc);
    return SendFramebuffer(pixels);
}

bool NativeTouchBar::SendFramebuffer(const std::vector<BYTE>& dibPixels)
{
    FramebufferHeader header = {};
    header.width = kWidth;
    header.height = kHeight;
    header.pixelFormat = kPixelFormat;
    std::vector<BYTE> request(sizeof(header) + kWidth * kHeight * 3);
    memcpy(&request[0], &header, sizeof(header));
    BYTE* output = &request[sizeof(header)];
    for (int x = 0; x < kWidth; ++x)
    {
        for (int y = kHeight - 1; y >= 0; --y)
        {
            const BYTE* pixel = &dibPixels[(y * kWidth + x) * 4];
            *output++ = pixel[2];
            *output++ = pixel[1];
            *output++ = pixel[0];
        }
    }
    DWORD returned = 0;
    return DeviceIoControl(_displayDevice, kIoctlUpdateFramebuffer, &request[0],
        static_cast<DWORD>(request.size()), NULL, 0, &returned, NULL) != FALSE;
}

void NativeTouchBar::AddContextKey(int& x, const wchar_t* glyph, USHORT key,
    USHORT modifier1, USHORT modifier2, int width)
{
    AddButton(x, width, L"", glyph, modifier1 ? Action::Chord : Action::Key, key);
    if (modifier1)
    {
        _buttons.back().modifiers[0] = modifier1;
        _buttons.back().modifierCount = 1;
        if (modifier2)
        {
            _buttons.back().modifiers[1] = modifier2;
            _buttons.back().modifierCount = 2;
        }
    }
    x += width + 5;
}

void NativeTouchBar::AddContextButtons(int& x)
{
    const std::wstring proc = _foregroundProcess;
    const std::wstring title = _foregroundTitle;
    const auto contains = [](const std::wstring& text, const wchar_t* value)
    {
        return text.find(value) != std::wstring::npos;
    };
    const auto addEditing = [&](bool formatting)
    {
        AddContextKey(x, L"\xE8C8", 'C', VK_CONTROL);
        AddContextKey(x, L"\xE8C6", 'X', VK_CONTROL);
        AddContextKey(x, L"\xE77F", 'V', VK_CONTROL);
        AddContextKey(x, L"\xE8B3", 'A', VK_CONTROL);
        if (formatting)
        {
            AddContextKey(x, L"\xE8DD", 'B', VK_CONTROL);
            AddContextKey(x, L"\xE8DB", 'I', VK_CONTROL);
        }
    };

    if (contains(proc, L"notepad"))
    {
        AddContextKey(x, L"\xE710", 'N', VK_CONTROL);
        AddContextKey(x, L"\xE8E5", 'O', VK_CONTROL);
        AddContextKey(x, L"\xE74E", 'S', VK_CONTROL);
        AddContextKey(x, L"\xE721", 'F', VK_CONTROL);
        addEditing(false);
    }
    else if (contains(proc, L"chrome") || contains(proc, L"msedge") ||
        contains(proc, L"firefox") || contains(proc, L"brave") || contains(proc, L"opera"))
    {
        if (contains(title, L"youtube"))
        {
            AddContextKey(x, L"\xE892", 'J'); AddContextKey(x, L"\xE768", 'K');
            AddContextKey(x, L"\xE893", 'L'); AddContextKey(x, L"\xE74F", 'M');
            AddContextKey(x, L"\xE740", 'F'); AddContextKey(x, L"\xE7F4", 'C');
        }
        else if (contains(title, L"netflix") || contains(title, L"disney") || contains(title, L"prime video"))
        {
            AddContextKey(x, L"\xE892", VK_LEFT); AddContextKey(x, L"\xE768", VK_SPACE);
            AddContextKey(x, L"\xE893", VK_RIGHT); AddContextKey(x, L"\xE740", 'F');
        }
        else if (contains(title, L"google docs") || contains(title, L"word online"))
        {
            AddContextKey(x, L"\xE74E", 'S', VK_CONTROL); AddContextKey(x, L"\xE7A7", 'Z', VK_CONTROL);
            AddContextKey(x, L"\xE7A6", 'Y', VK_CONTROL); AddContextKey(x, L"\xE8DD", 'B', VK_CONTROL);
            AddContextKey(x, L"\xE8DB", 'I', VK_CONTROL); AddContextKey(x, L"\xE721", 'F', VK_CONTROL);
            addEditing(false);
        }
        else if (contains(title, L"github") || contains(title, L"gitlab") || contains(title, L"bitbucket"))
        {
            AddContextKey(x, L"\xE72B", VK_BROWSER_BACK); AddContextKey(x, L"\xE72A", VK_BROWSER_FORWARD);
            AddContextKey(x, L"\xE721", 'F', VK_CONTROL); AddContextKey(x, L"Tab", 'T', VK_CONTROL);
        }
        else if (contains(title, L"gmail") || contains(title, L"outlook") || contains(title, L"mail"))
        {
            AddContextKey(x, L"\xE710", 'C'); AddContextKey(x, L"\xE8CA", 'R');
            AddContextKey(x, L"\xE72B", VK_BROWSER_BACK); AddContextKey(x, L"\xE721", 'F', VK_CONTROL);
        }
        else
        {
            AddContextKey(x, L"\xE72B", VK_BROWSER_BACK); AddContextKey(x, L"\xE72A", VK_BROWSER_FORWARD);
            AddContextKey(x, L"\xE72C", VK_F5); AddContextKey(x, L"Tab+", 'T', VK_CONTROL);
            AddContextKey(x, L"TabX", 'W', VK_CONTROL); AddContextKey(x, L"\xE721", 'F', VK_CONTROL);
        }
    }
    else if (contains(proc, L"codeblocks"))
    {
        AddContextKey(x, L"\xE768", VK_F9); AddContextKey(x, L"\xE74E", 'S', VK_CONTROL);
        AddContextKey(x, L"\xE7A7", 'Z', VK_CONTROL); AddContextKey(x, L"\xE7A6", 'Y', VK_CONTROL);
        AddContextKey(x, L"\xE721", 'F', VK_CONTROL); addEditing(true);
    }
    else if (contains(proc, L"code") || contains(proc, L"devenv") ||
        contains(proc, L"rider") || contains(proc, L"idea"))
    {
        AddContextKey(x, L"\xE768", VK_F5); AddContextKey(x, L"\xE74E", 'S', VK_CONTROL);
        AddContextKey(x, L"\xE7A7", 'Z', VK_CONTROL); AddContextKey(x, L"\xE7A6", 'Y', VK_CONTROL);
        AddContextKey(x, L"\xE721", 'F', VK_CONTROL); addEditing(true);
    }
    else if (contains(proc, L"explorer"))
    {
        AddContextKey(x, L"\xE72B", VK_BROWSER_BACK); AddContextKey(x, L"\xE72A", VK_BROWSER_FORWARD);
        AddContextKey(x, L"\xE74A", VK_UP, VK_MENU); AddContextKey(x, L"\xE72C", VK_F5);
        AddContextKey(x, L"\xE721", 'F', VK_CONTROL); addEditing(false);
        AddContextKey(x, L"\xE8C6", 'X', VK_CONTROL);
    }
    else if (contains(proc, L"winword") || contains(proc, L"excel") ||
        contains(proc, L"powerpnt") || contains(proc, L"onenote"))
    {
        AddContextKey(x, L"\xE74E", 'S', VK_CONTROL); AddContextKey(x, L"\xE749", 'P', VK_CONTROL);
        AddContextKey(x, L"\xE7A7", 'Z', VK_CONTROL); AddContextKey(x, L"\xE7A6", 'Y', VK_CONTROL);
        AddContextKey(x, L"\xE8DD", 'B', VK_CONTROL); AddContextKey(x, L"\xE8DB", 'I', VK_CONTROL);
        AddContextKey(x, L"\xE8DC", 'U', VK_CONTROL); AddContextKey(x, L"\xE721", 'F', VK_CONTROL);
        addEditing(false);
    }
    else if (contains(proc, L"vlc") || contains(proc, L"wmplayer") || contains(proc, L"spotify"))
    {
        AddContextKey(x, L"\xE892", VK_MEDIA_PREV_TRACK); AddContextKey(x, L"\xE768", VK_MEDIA_PLAY_PAUSE);
        AddContextKey(x, L"\xE893", VK_MEDIA_NEXT_TRACK); AddContextKey(x, L"\xE74F", VK_VOLUME_MUTE);
    }
    else
    {
        AddContextKey(x, L"\xE7A7", 'Z', VK_CONTROL); AddContextKey(x, L"\xE7A6", 'Y', VK_CONTROL);
        addEditing(true); AddContextKey(x, L"\xE74E", 'S', VK_CONTROL);
    }
}