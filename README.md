# DFR Content Host Application for Windows

Host Application for MacBook Pro models with Touch Bar under Windows, based on [imbushuo](https://github.com/imbushuo)'s [DFRContentHost](https://github.com/imbushuo/DFRContentHost).

Target Hardware: MacBook Pro models with Apple DFR Display (VID 0x05AC, PID 0x8302 / IOCTL `0x80862004`).

---

## Stato Attuale del Progetto (Current Project Status)

Il progetto include due implementazioni per la gestione della Touch Bar su Windows:

### 1. Version Standard Avalonia C# (`DFRContentHost`)
- **Risoluzione DFR 2008x60**: Adattata per i modelli MacBook Pro 2000+ a 2008 pixel di larghezza max.
- **Interfaccia e Controlli**:
  - Tasto **Fn** dedicato per l'espansione F1-F12 (con intercettazione del tasto fisico `VK_F23`).
  - Tastiera Virtuale **Az** a finestra scorrevole per l'immissione di caratteri alfanumerici e simboli.
  - Striscia **Smart Context** con scorciatoie dinamiche per Notepad, Browser (YouTube, Netflix, GitHub, Docs, Gmail), IDE (CodeBlocks, VS Code, Visual Studio), Terminali, Esplora File e Office.
  - Slider a tocco per **Volume** (CoreAudio) e **Luminosità Schermo** (Power Schemes / WMI).
  - Striscia di stato con Orologio (`HH:mm`), percentuale/icona della Batteria e indicatore Wi-Fi a 4 barre.
  - *Rimosso il pulsante Windows / Start per massimizzare lo spazio utile della barra*.

### 2. Versione Nativa C++ Nuda Win32/GDI (`NativeDfrPrototype`)
Un'implementazione nativa ad alte prestazioni a zero dipendenze .NET/Avalonia:
- **Direct Framebuffer IOCTL**: Comunicazione diretta con il driver via IOCTL `0x80862004` (buffer ARGB 32bpp).
- **Apple HID Touch Digitizer**: Decodifica nativa dei report multi-touch HID (VID `0x05AC`, PID `0x8302`).
- **Piena Compatibilità MinGW**: Supporto a toolchain MinGW datate (es. GCC 4.7.1) mediante dichiarazioni compatibili ed elaborazione runtime PSAPI (`GetModuleFileNameExW`).
- **Modalità Preview Desktop**: Esecuzione in modalità finestra per testare l'interfaccia direttamente su desktop PC (`--preview`).

---

## Istruzioni per la Compilazione ed Esecuzione

### Versione Nativa C++ (`NativeDfrPrototype`)
Compilare eseguendo lo script inclusivo della toolchain MinGW:
```cmd
cd NativeDfrPrototype
build.cmd
```
Esecuzione interattiva con l'hardware Touch Bar reale:
```cmd
bin\DFRNativePrototype.exe --interactive
```
Esecuzione in modalità anteprima desktop (senza hardware DFR):
```cmd
bin\DFRNativePrototype.exe --preview
```

---

# License

Copyright (c) Fernando Morea. All rights reserved.
Licensed under the [MIT License](LICENSE.txt).
