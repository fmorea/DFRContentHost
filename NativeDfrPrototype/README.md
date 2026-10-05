# Native DFR framebuffer prototype

This is an isolated Win32 C++ prototype for the existing 2008x60 DFR display path. It uses SetupAPI to find the display interface, GDI to draw a sample frame into a top-down DIB, and the existing DFR framebuffer IOCTL to send it to the driver.

It does not replace the current Avalonia host. Running the executable without arguments overwrites the Touch Bar with a static test frame. `--inspect-touch` prints the digitizer descriptor; `--watch-touch` reads touch reports and prints contact down/move/up without writing to the display. `--interactive` opens the framebuffer, draws the sample buttons, and highlights the button under the touch; close the Avalonia host first because the DFR display handle is exclusive. This prototype only tests rendering and hit-testing; buttons do not trigger application commands yet.

## Build

Install a MinGW-w64 or MinGW compiler with Windows headers and the SetupAPI/GDI/HID libraries, then run `build.cmd` from this directory. Set `DFR_CXX` to override the compiler path.