# DFR Content Host Application for Windows

This is a modified Host Application for MacBook Pro models with a Touch Bar, based on [imbushuo](https://github.com/imbushuo)'s [DFRContentHost](https://github.com/imbushuo/DFRContentHost).

Target hardware: MacBook Pro 2020 with Intel Core i5.

What have changed:
- Change resolution from 2170 * 60 to 2008 * 60 to make it work (the touch bar is shorter than the old model, 2008 is the maximum support width)
- Remove virtual esc button (There's exist esc key on the new macbooks pro)
- Specific font for support language like Japanese and Chinese
- Zero width in columnDefinition to prevent the f12 key from disappear out of touch bar after removing the esc key
- Add Margin in both MediaTitle and MediaArtist to keep the space between MediaThumbnail and MediaTitle/MediaArtist
- Add limit to prevent contents beyond from MediaTitle
- Keeps apply CornerRadius even though the button is pressed
- Remove foreground-app and media-title text from the status strip
- Replace the Wi-Fi network name with a white signal-strength indicator
- Use a grayscale battery indicator with a readable percentage
- Replace volume step buttons and percentage text with a touch-controlled slider and live percentage
- Add a touch-controlled screen-brightness slider using Windows brightness APIs
- Add a font-independent Windows logo button
- Keep application-specific shortcut presets and disable Win32 runtime menu extraction to avoid host crashes

=---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------=
# Original readme document

This application implements the user-mode host application for [DFRDisplayKm](https://github.com/imbushuo/DFRDisplayKm).

![High Level Overview Topology](docs/DFR%20High%20Level%20Topology.jpg)

# License

Copyright (c) Bingxing Wang. All rights reserved.

Licensed under the [MIT License](https://github.com/imbushuo/DFRContentHost/blob/master/LICENSE.txt)
