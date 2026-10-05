# Stato del Porting C++ Nativo 1:1 (Native DFR Prototype)

## Parità Raggiunta (1:1 con DFRContentHost C#)

1. **Rendering & IOCTL Direct Display 2008x60**:
   - Generazione nativa GDI dei buffer ARGB top-down a 32bpp con invio diretto al driver tramite IOCTL `0x80862004`.

2. **Apple HID Touch Digitizer**:
   - Enumerazione dinamica dell'interfaccia HID Digitizer Apple (VID `0x05AC`, PID `0x8302`) via SetupAPI/HID.
   - Decodifica report multi-touch con mappatura coordinate 32767 -> 2008x60 e hit-testing immediato.

3. **Integrazione Audio & Brightness**:
   - Lettura e regolazione master volume di sistema tramite `waveOutGetVolume` / `waveOutSetVolume` e API Audio Endpoint.
   - Icona del volume dinamica che riflette fedelmente lo stato (Mute/0% `\xE74F`, <33% `\xE993`, <66% `\xE994`, >=66% `\xE995`).
   - Pannello e slider popover a tocco per regolazione fine di volume e luminosità.
   - Sincronizzazione della luminosità di sistema via `powrprof.dll` (`PowerWriteACValueIndex`, `PowerWriteDCValueIndex`, `PowerSetActiveScheme`).

4. **Tastiera Virtuale Nativa ("Az")**:
   - Attivazione/disattivazione rapida mediante tasto dedicato.
   - Supporto a set completo di lettere (A-Z) e simboli (`1-0`, `-=`, `[]\`, `;',./`).
   - Paginazione a finestra scorrevole (8 tasti su schermo per volta) con tasti nav `<` e `>`.
   - Inserimento Unicode reale tramite `SendInput` (supporta tasti di controllo: Space, Backspace, Enter, Tab, Esc).

5. **Barra Tasti Funzione (F1-F12)**:
   - Espansione/contrazione dinamica con tasto `Fn`.
   - Rilevamento immediato della pressione fisica del tasto `Fn` (stato temporaneo visivo F1-F12).

6. **Pulsanti Contestuali Applicativi (Smart Context Strip)**:
   - Monitoraggio del processo e del titolo della finestra in primo piano (`GetForegroundWindow` / `GetWindowTextW`).
   - Mappatura completa 1:1 per tutte le famiglie applicative:
     - **Notepad**: Nuovo, Apri, Salva, Trova, Taglia, Copia, Incolla, Seleziona Tutto.
     - **Browser** (Chrome, Edge, Firefox, Brave, Opera):
       - *YouTube*: Rewind 10s (`J`), Play/Pause (`K`), Forward 10s (`L`), Mute (`M`), Fullscreen (`F`), Sottotitoli (`C`).
       - *Streaming* (Netflix, Disney, Prime Video): Seek Back/Fwd, Play/Pause, Fullscreen.
       - *Developer* (GitHub, GitLab, Bitbucket): Back, Forward, Trova, Trova File (`Ctrl+T`).
       - *Documenti* (Google Docs, Word Online, Google Sheets, Excel Online): Salva, Annulla, Ripristina, Grassetto, Corsivo, Edit.
       - *Mail* (Gmail, Outlook, Mail): Scrivi (`C`), Rispondi (`R`), Indietro, Trova.
       - *Navigazione Generica*: Indietro, Avanti, Ricarica (`F5`), Nuova Scheda, Chiudi Scheda, Trova.
     - **IDE & Code**: CodeBlocks (`F9` Build, Salva, Annulla/Ripristina, Edit), VS Code / Visual Studio / Rider / IDEA (`F5` Run, Salva, Annulla/Ripristina, Edit).
     - **Terminali**: Copy, Paste, Nuova Scheda (`Ctrl+Shift+T`), Clear (`L`), Freccia Su, Freccia Giù.
     - **Esplora File**: Indietro, Avanti, Cartella Superiore (`Alt+Up`), Ricarica, Trova, Taglia, Copia, Incolla, Seleziona Tutto.
     - **Office** (Word, Excel, PowerPoint, OneNote): Salva, Stampa, Annulla, Ripristina, Grassetto, Corsivo, Sottolineato, Trova, Edit.
     - **Media Players** (VLC, MPC, WMPlayer, Spotify, Groove): Traccia Precedente, Play/Pause, Traccia Successiva, Mute.
     - **Fallback Generico**: Annulla, Ripristina, Taglia, Copia, Incolla, Seleziona Tutto, Grassetto, Corsivo, Salva.

7. **Area di Stato di Sistema**:
   - Orologio in tempo reale (formato `HH:mm`).
   - Percentuale batteria e livello di carica (`GetSystemPowerStatus`).
   - Indicatore del segnale Wi-Fi (4 barre di potenza).
   - Nome del processo attivo in primo piano sulla Touch Bar.

---

### Istruzioni per la Compilazione ed Esecuzione

```cmd
:: Compilazione nativa C++
build.cmd

:: Esecuzione interattiva (Touch Bar Nativa C++)
bin\DFRNativePrototype.exe --interactive
```
