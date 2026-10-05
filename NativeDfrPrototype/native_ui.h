#pragma once

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#include <windows.h>

#include <cstdint>
#include <string>
#include <vector>

class NativeTouchBar
{
public:
    explicit NativeTouchBar(HANDLE displayDevice);

    bool Render();
    void HandleTouch(std::uint32_t normalizedX, bool pressed);
    void SetFnPressed(bool pressed);

private:
    enum class Action
    {
        None,
        Key,
        Chord,
        Character,
        ToggleFn,
        ToggleKeyboard,
        ToggleCaps,
        ToggleNumbers,
        PageLeft,
        PageRight,
        ToggleVolume,
        ToggleBrightness,
        Lock,
        SliderVolume,
        SliderBrightness
    };

    struct Button
    {
        RECT bounds;
        std::wstring label;
        std::wstring glyph;
        Action action;
        USHORT key;
        USHORT modifiers[3];
        int modifierCount;
        bool enabled;
        bool active;
    };

    enum class ViewMode
    {
        Desktop,
        Keyboard,
        Volume,
        Brightness
    };

    HANDLE _displayDevice;
    std::vector<Button> _buttons;
    ViewMode _viewMode;
    bool _fnPressed;
    bool _fnExpanded;
    bool _keyboardVisible;
    bool _capsActive;
    bool _numericMode;
    bool _pointerDown;
    bool _batteryAvailable;
    int _activeButton;
    int _pressedButton;
    int _keyboardOffset;
    int _batteryLevel;
    int _wifiSignalBars;
    double _volumeLevel;
    double _brightnessLevel;
    bool _volumeMuted;
    std::wstring _clockText;
    std::wstring _foregroundProcess;
    std::wstring _foregroundTitle;

    void BuildButtons();
    void AddContextButtons(int& x);
    void AddContextKey(int& x, const wchar_t* glyph, USHORT key,
        USHORT modifier1 = 0, USHORT modifier2 = 0, int width = 58);
    void RefreshSystemState();
    void AddButton(int x, int width, const std::wstring& label, const std::wstring& glyph,
        Action action, USHORT key = 0, bool active = false, bool enabled = true);
    int HitTest(int x) const;
    void UpdateSlider(int x);
    void InvokeButton(int index);
    void SendKey(USHORT key, bool extended = false);
    void SendChord(const USHORT* modifiers, int modifierCount, USHORT key);
    void ApplyVolume();
    void ApplyBrightness();
    bool DrawFrame(const std::vector<Button>& buttons);
    bool SendFramebuffer(const std::vector<BYTE>& dibPixels);
};