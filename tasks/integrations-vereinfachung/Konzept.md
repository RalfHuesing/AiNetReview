---
status: ready
---

# Integrations-Vereinfachung & Zero-Config Review

## Intention

AiNetReview soll ohne manuelle Konfigurationserstellung sofort einsatzbereit sein: Wird das Tool in einem Projektordner ohne vorhandene `ainetreview.json` aufgerufen, erkennt es automatisch die zuständige Projektmappe (`.sln`/`.slnx`), erzeugt eine vollständige `ainetreview.json` mit den Standardwerten der registrierten Regeln im Projektverzeichnis und führt den Review unmittelbar aus. Regeln definieren ihre Default-Werte und Aktivierungszustände autoritativ und gekapselt an einer Stelle (an der Rule).

## Scope

### Muss

- **Vereinfachte CLI-Syntax:**
  - `ainetreview` (ohne Argumente) und `ainetreview review` führen die Analyse im aktuellen Arbeitsverzeichnis (`Environment.CurrentDirectory`) durch.
  - Optionaler Positions-Pfadparameter (z. B. `ainetreview [project-path]` oder `ainetreview review [project-path]`) analysiert das angegebene Verzeichnis.
  - Der Aufruf `ainetreview review --config <pfad>` bleibt erhalten. Zeigt `<pfad>` auf eine noch nicht existierende Datei, wird die Datei an diesem Zielpfad automatisch mit den Default-Werten angelegt und der Review sofort ausgeführt.
- **Dateisystem-Persistierung beim Initiallauf:**
  - Fehlt die Konfigurationsdatei am Zielort (im aktuellen Ordner oder am übergebenen Pfad), wird sie vor der Analyse physisch angelegt (Bootstrap) und der Review unmittelbar gestartet.
  - Die Generierung erfolgt vor dem Start der Analyse, sodass der Lauf die gerade erzeugte Datei wie gewohnt validiert und lädt.
- **Solution-Erkennung & Heuristik:**
  - Fehlt die Konfiguration, durchsucht das Tool das Zielverzeichnis (nur Top-Level, nicht rekursiv) nach `.slnx`- und `.sln`-Dateien.
  - Bei mehreren Treffern greift folgende feste Priorisierung:
    1. Namensübereinstimmung mit dem Projektordner (z. B. `Foo.slnx`/`Foo.sln` im Ordner `Foo`).
    2. Format-Präferenz: `.slnx` vor `.sln`.
    3. Alphabetische Sortierung (Ordinal).
  - Wird keine Solution gefunden, bricht der Lauf mit Exit-Code 2 (`INVALID_INPUT`) und Fehlermeldung auf `stderr` ab.
- **Autoritative Rule-Defaults an der Rule:**
  - Jede `IReviewRule` bzw. ihr `RuleDescriptor` definiert ihren Default-Zustand vollständig lokal (inkl. `defaultEnabled: true` und den Standardwerten aller `RuleOptionDescriptor`s).
  - Keine verstreuten Default-Werte in Host-Code, Validator oder CLI-Layern.
- **Konfigurations-Assembler (Generator):**
  - Eine zentrale Komponente im Core (`ReviewConfigGenerator` / `DefaultConfigProvider`) setzt aus Projekt-Root, gefundener Solution, Standard-Output (`audit-reporting`) und allen aus der `RuleRegistry` abgefragten Rules ein valides JSON-Dokument (`schemaVersion: 1`) zusammen.
  - Formatierung: Sauberes, eingerücktes JSON mit allen Standard-Optionen der registrierten Regeln, damit die Datei für den Entwickler selbsterklärend ist.
- **Stream- & Protokoll-Integrität:**
  - Das Anlegen der Konfigurationsdatei erzeugt keine zusätzlichen Ausgaben auf `stdout` oder `stderr`. `stdout` bleibt exklusiv dem Erfolgs-JSON vorbehalten. Das Anlegen wird ausschließlich im Host-Log erfasst.

### Nicht

- **Kein interaktiver Modus:** Keine Prompts, Nachfragen oder Konsolen-Auswahldialoge.
- **Keine rekursive Solution-Suche:** Es wird strikt nur direkt im Zielverzeichnis gesucht, um nicht unbeabsichtigt verschachtelte Test- oder Beispiellösungen zu aktivieren.
- **Keine dynamische Regel-Erkennung:** Keine Plugins oder Assembly-Scanning zur Laufzeit; es gelten die statisch registrierten Produktionsregeln.
- **Keine In-Memory-Only Ausführung ohne Datei:** Der Zero-Config-Lauf legt stets die `ainetreview.json` an, um Transparenz und Versionierbarkeit für den Anwender sicherzustellen.
- **Keine Modifikation von C# Quellcode oder Projektdateien:** Es wird ausschließlich die Konfigurationsdatei angelegt und der Review ausgeführt.

## Verifikation

- **FastTests (`AiNetReview.FastTests`):**
  - *Solution-Erkennung:* Unit-Tests für `SolutionDiscovery` mit 0 Solutions (Fehlerfall), 1 Solution, mehreren Solutions mit Ordner-Match sowie mehreren Solutions mit `.slnx` vs. `.sln` Heuristik.
  - *Rule-Defaults & Generator:* Test, dass der Generator alle in `RuleRegistry` registrierten Regeln mit ihren deklarierten Default-Optionen und `enabled: true` in ein valides Schema-v1-JSON überführt.
  - *Validator-Kompatibilität:* Sicherstellen, dass das vom Generator erzeugte JSON fehlerfrei durch `ReviewConfigValidator.Validate` akzeptiert wird.
- **IntegrationTests (`AiNetReview.IntegrationTests`):**
  - *End-to-End Zero-Config:* Ausführung von `ReviewCommand.InvokeAsync` in einem Verzeichnis ohne `ainetreview.json`:
    - Prüfen, dass `ainetreview.json` erzeugt wurde.
    - Prüfen, dass `stdout` die erwartete Erfolgszeile enthält (Exit-Code 0).
    - Prüfen, dass Berichte in `audit-reporting/` publiziert wurden.
  - *CLI-Argument-Varianten:* Tests für Aufrufe mit Verzeichnispfad, `review` Subcommand und Default-Kombinationen.
  - *Fehlerfall:* Aufruf in einem Verzeichnis ohne `.sln`/`.slnx` liefert Exit-Code 2 (`INVALID_INPUT`) und keine generierte Konfigurationsdatei.
  - *Expliziter Pfad:* Aufruf mit `--config <pfad>` auf eine noch nicht existierende Datei legt diese an und führt den Review erfolgreich aus.
  - *Rückwärtskompatibilität:* Aufruf mit `--config` und bestehender Konfiguration funktioniert exakt wie zuvor.
