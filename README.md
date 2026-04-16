# WeTacoo Domain Playground

Tool esplorativo per il modello DDD5 di WeTacoo. Niente installazioni: scarica lo zip della tua piattaforma, estrai, doppio click.

## Download

Nella cartella `playground/` trovi un pacchetto self-contained per ciascuna piattaforma:

| Piattaforma | File | Come avviare |
|---|---|---|
| Windows | `playground/dist-win.zip` | Estrai la cartella → doppio click su `Avvia Playground.bat` |
| macOS (Apple Silicon M1/M2/M3/M4) | `playground/dist-mac-arm64.zip` | Vedi `LEGGIMI-MAC.txt` nello zip |
| macOS (Intel) | `playground/dist-mac-x64.zip` | Vedi `LEGGIMI-MAC.txt` nello zip |

Ogni zip pesa ~50 MB, include il runtime .NET: non serve installare nient'altro.

Il playground apre un server locale su `http://localhost:5100`.

## Come chiuderlo

- **Windows:** chiudi la finestra console del server (o taskkill).
- **macOS:** chiudi la finestra Terminale aperta dal `.command`.
