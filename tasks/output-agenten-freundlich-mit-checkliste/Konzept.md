---
status: ready
---

# Agentenfreundliche Auditberichte

## Intention

Ein Agent soll auch viele Review-Befunde über mehrere Arbeitssitzungen hinweg geordnet bearbeiten können. Der temporäre Bericht bietet dafür eine knappe Landkarte und eine kleiner werdende Liste offener Befunde, ohne denselben Inhalt in mehreren Formaten oder leere Berichte mitzuschleppen.

## Ziel

Ein Auditlauf veröffentlicht einen `index.md` als Einstieg und genau eine Markdown-Datei pro Regel mit Befunden. Jede Regeldatei gibt dem Agenten eine kurze Richtung für die Prüfung und listet ihre Befunde kompakt in einer Tabelle. Der Agent entfernt bearbeitete Befunde aus dem Report, sodass nur der noch offene Arbeitsvorrat stehen bleibt.

## Scope

### Muss

- `index.md` enthält die Lauf-ID, den absoluten Pfad des analysierten Repositorys und den relativen Solutionpfad. Sie verlinkt ausschließlich Regeldateien mit offenen Befunden. Sie enthält keine Befundzahlen, Start-/Endzeiten, Regelversionen oder Optionen.
- Wenn aktive Regeln beim Audit keine Befunde liefern, meldet der Index dies in einem kurzen Satz. Sind alle Regeln deaktiviert, meldet er stattdessen, dass keine Prüfung stattfand. Für diese Fälle werden keine Regeldateien erzeugt.
- Der Index erklärt den Arbeitsablauf kurz: Jeden Befund am Quellcode prüfen; nach Behebung oder begründeter Ignorierung die zugehörige Tabellenzeile löschen. Die Begründung für eine Ignorierung teilt der Agent dem Nutzer bei der Bearbeitung mit. Sobald die letzte Zeile einer Regel entfernt ist, entfernt er auch die Regeldatei und ihren Indexlink. Wenn alle Befunde abgearbeitet sind, meldet der Index „Alle Befunde bearbeitet“ statt „Keine Befunde gefunden“. Es gibt keine Checkboxen oder zweite Fortschrittsliste.
- Eine Regeldatei enthält oben nur den Regelzweck, die effektiven Optionen und wenige Prüffragen. Ausführliche technische Messbeschreibungen, Regelversionen und statische Befundzahlen werden nicht ausgegeben.
- Darunter steht genau eine Tabellenzeile pro Befund mit den Spalten `Quelle`, `Signal` und `Weitere Stellen`. `Quelle` verlinkt den repräsentativen Quellort und nennt bei Bedarf das Projekt zur eindeutigen Zuordnung. `Signal` nennt knapp den Prüfungsanlass: bei Dead Code Art des Kandidaten und fehlende bekannte Nutzung, bei Control-Flow die auslösenden Werte und Grenzwerte, bei Duplicate Code Clustergröße und Ähnlichkeitswert. `Weitere Stellen` verlinkt zusätzliche Evidenz ohne den Quelllink zu wiederholen; bei Duplicate-Code-Clustern bleiben alle betroffenen Methoden erreichbar.
- Der Markdown-Bericht verzichtet auf rohe `Metrics`-Zeilen, lange wiederholte Begründungen, Quellcode-Snippets und ausführliche Evidenztexte. Er erfindet weder Schweregrad noch Wahrscheinlichkeit, da die Regeln dafür keinen belastbaren Wert liefern. Befundmodell und Erkennungslogik bleiben unverändert; beschreibende Regeltexte dürfen für den Bericht redaktionell gekürzt werden.
- Der manuelle Auditlauf erzeugt keine `findings.json`. CLI- und manueller Auditlauf verwenden dieselbe schlanke Markdown-Struktur. Betroffene Tests, Dokumentation und bestehende Verweise auf Berichte werden angepasst.

### Nicht

- Keine automatische Behebung, Einstufung als Buildfehler oder automatische Entscheidung über einen Befund.
- Keine Zustandsübernahme zwischen Auditläufen, zusätzliche Fortschrittsdatei oder zweite maschinenlesbare Befundausgabe.
- Keine Änderung an Erkennungslogik oder Audit-Profilformat.

## Verifikation

- Automatisierte Report-Tests prüfen Index und Dateimenge bei gemischten Ergebnissen, null Befunden mit aktiven Regeln und null aktiven Regeln. Sie prüfen pro Befund genau eine Tabellenzeile, knappe Signale und gültige Links zu allen nötigen Quellorten, besonders bei Duplicate-Code-Clustern.
- Tests prüfen den kurzen Regelkontext, das Fehlen redundanter Felder und den Löschhinweis im Index. Ein Integrationstest prüft den manuellen Auditlauf ohne `findings.json` und mit absolutem Repositorypfad im Index.
- Die aktuelle Dokumentation beschreibt nur den tatsächlich implementierten Zustand; für Dokumentationsänderungen werden Diff und `git diff --check` geprüft.
