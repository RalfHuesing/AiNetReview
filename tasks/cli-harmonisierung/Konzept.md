---
status: ready
---

# CLI-Harmonisierung: Symmetrische Subcommands und Ein-Weg-Prinzip

## Intention

Die Befehlszeilenschnittstelle von AiNetReview wird auf eine strikte, symmetrische und eindeutige Syntax umgestellt. Es gibt für jede Aktion immer genau einen Weg. Die `ainetreview.json` direkt im Projektverzeichnis ist und bleibt die alleinige fachliche Konfigurationsbasis; die CLI dient ausschließlich dazu, die gewünschte Aktion (`review` oder `baseline`) auf das angegebene Projektverzeichnis anzuwenden.

## Ausgangslage und bisherige Bruchstellen

- Bisher existierten drei Aufrufformen: `ainetreview [project-path]`, `ainetreview review [project-path]` und `ainetreview [project-path] --cmd baseline`.
- Das Flag `--cmd baseline` existierte nur auf dem Root-Kommando. `review` war als Subcommand modelliert, führte aber dieselbe Logik aus wie das parameterlose Root-Kommando.
- In `Program.cs` wurde die Logging-Kategorie vor dem CLI-Parsing per String-Heuristik aus den Argumenten geraten (`args.Contains("--cmd")`), weil das Parsing asymmetrisch aufgebaut war.
- `--config` und `project-path` waren redundant: Die Konfigurationsdatei muss laut Core-Spezifikation ohnehin immer `ainetreview.json` heißen und direkt im Projekt-Root liegen.
- Die in `index.md` für den Review-Agenten generierte Handlungsanweisung enthielt bisher den unharmonischen Aufruf mit `--cmd baseline`.

## Modell und Syntax

Jeder Aufruf folgt ohne Ausnahme der Struktur:
```text
ainetreview <command> [project-path]
```

Als Parser-Bibliothek wird das bereits im Projekt referenzierte Paket `System.CommandLine` verwendet.

### 1. `ainetreview review [project-path]`
- Führt den vollständigen Review-Lauf für das angegebene Projekt durch.
- Lädt die Solution gemäß `<project-path>/ainetreview.json`, führt alle aktiven Analysen aus, gleicht mit der optionalen `baseline.json` ab und publiziert den Markdown-Bericht nach `audit-reporting/` (bzw. konfiguriertem Ausgabeverzeichnis).
- **Beispiele:**
  - Im Projektverzeichnis:
    ```bash
    ainetreview review
    ```
  - Mit explizitem Projektpfad:
    ```bash
    ainetreview review C:\Daten\Entwicklung\MeinProjekt
    ```

### 2. `ainetreview baseline [project-path]`
- Erfasst den aktuellen Dateistand des Projekts ohne Ausführung von Review-Analysen.
- Schreibt die `baseline.json` mit SHA-256-Hashes aller Quelldateien direkt in das konfigurierte Ausgabeverzeichnis.
- **Beispiele:**
  - Im Projektverzeichnis:
    ```bash
    ainetreview baseline
    ```
  - Mit explizitem Projektpfad:
    ```bash
    ainetreview baseline C:\Daten\Entwicklung\MeinProjekt
    ```

### 3. Keine impliziten Standard-Aktionen, keine redundanten Pfad-Flags, keine CLI-Overrides
- Ein Aufruf ohne Subcommand (`ainetreview` oder `ainetreview C:\...`) ist ungültig (`INVALID_INPUT`, Exit-Code 2) und zeigt die zulässige Syntax an. Es gibt keinen impliziten Default-Subcommand.
- Die Option `--config` entfällt ersatzlos. Die Konfiguration ist per Definition immer `<project-path>/ainetreview.json` (bzw. `./ainetreview.json`).
- Es gibt keine CLI-Flags zur Übersteuerung von Analyse-Optionen (keine `--analysis`, `--no-baseline` etc.). Was getan wird, bestimmt ausschließlich die `ainetreview.json`.

## Scope

### Muss

- Symmetrische Subcommands `review` und `baseline` unter `RootCommand` mit `System.CommandLine` einrichten.
- Aufruf ohne Subcommand oder mit unbekanntem Subcommand als `INVALID_INPUT` (Exit-Code 2) mit Fehlermeldung auf stderr abweisen.
- Die Optionen `--cmd` und `--config` ersatzlos entfernen.
- Den Pfadparameter `[project-path]` einheitlich als optionales Argument (Default: `.`) an beide Subcommands binden.
- Bootstrapping von `ainetreview.json` bei fehlender Datei im Zielverzeichnis für beide Subcommands unverändert beibehalten.
- Die Generierung des Baseline-Befehls in `index.md` auf die neue Syntax `ainetreview baseline "<projectRoot>"` umstellen.
- Die Serilog-Initialisierung in `Program.cs` auf den eindeutigen ersten Token (`args.FirstOrDefault()`) abstimmen (`review`, `baseline`, sonst `unknown`).
- Alle bestehenden Exit-Codes (`0`, `2`, `3`, `4`, `130`) und das einzeilige JSON-Streaming auf stdout und stderr unverändert beibehalten.
- Dokumentation (`docs/interfaces/cli.md`) und Tests auf die neue Syntax anpassen.

### Nicht

- Zusätzliche CLI-Overrides (wie Filter auf einzelne Analysen, Parameter-Überschreibungen etc.) einführen.
- Eine "intelligente" Pfad-Erkennung (z. B. Übergabe von `.sln`-Dateien oder abweichenden JSON-Dateinamen), die vom Prinzip einer festen `ainetreview.json` pro Projekt abweicht.
- Neue Subcommands wie `init` in diesem Schritt einführen (das automatische Bootstrappen bleibt wie bisher Teil von `review` und `baseline`).
- Neue NuGet-Pakete hinzufügen (das vorhandene `System.CommandLine` reicht vollständig aus).
- Das Format der `ainetreview.json` oder der `baseline.json` verändern.
- Git-Abhängigkeiten oder interaktive Prompts einführen.

## Verifikation

- `ainetreview` ohne Argumente liefert Exit-Code 2 und eine verständliche Fehlermeldung auf stderr.
- `ainetreview review` führt im aktuellen Verzeichnis einen Review durch und liefert die bekannte JSON-Erfolgszeile auf stdout.
- `ainetreview baseline` erzeugt `baseline.json` und liefert die JSON-Erfolgszeile.
- `ainetreview review <pfad>` und `ainetreview baseline <pfad>` lösen relative und absolute Pfade identisch auf.
- `--cmd baseline` und `--config` werden als ungültige Eingabe abgewiesen (Exit-Code 2).
- Der in `index.md` eingebettete Baseline-Befehl verwendet die Form `... baseline ...` und lässt sich fehlerfrei ausführen.
- Alle bestehenden Host- und Integrationstests laufen mit der neuen Syntax erfolgreich durch.
