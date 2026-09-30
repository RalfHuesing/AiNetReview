---
status: draft
---

# Allgemeine Erweiterungen für C#-Audits

## Intention

AiNetReview soll für unterschiedliche C#-Solutions verlässliche, nachvollziehbare Review-Signale liefern und diese ohne Eingriffe in das untersuchte Repository für Menschen und nachgelagerte Agenten nutzbar machen. Die Fallstudie AiNetCodeNavigator dient als Gegenbeispiel und Testmaterial, nicht als Vorlage für fest eingebaute Projektregeln.

## Empfehlung und Stand der Prüfung

Die fünf vorhandenen Vorschläge sind Problemhypothesen. Für das erste zusammenhängende Ergebnis empfehle ich einen extern nutzbaren Audit-Befehl, einen strukturierten Export der tatsächlichen Findings und gezielte Korrekturen belegter Rauschquellen. Neue Architekturheuristiken benötigen zuerst einen allgemeinen Signalvertrag und Gegenproben an verschiedenartigen C#-Projekten.

- **01 Rauschunterdrückung:** `DeadCodeCandidatesAnalysis` fragt den Roslyn-Einstiegspunkt bereits ab und schützt die Methode; der enthaltende Typ wird vorher als eigener Kandidat geprüft und ist dadurch noch nicht geschützt. `method-control-flow-outliers` verlangt für den Zählpfad bereits mindestens zwei Entscheidungskonstrukte; der Test für einen einzelnen flachen Switch erwartet keinen Befund. Eine pauschale Gewichtung von Guard-Ketten würde die bisherige Entscheidungsmetrik ändern, obwohl Guard-Ketten weiter Verzweigungen und Prüfaufwand enthalten. Die behaupteten Reduktionsprozente sind durch den Einzelfall nicht belegt. Bei `missing-test-evidence-candidates` kann eine kompakte Darstellung hilfreich sein, die zugrunde liegenden Findings müssen dabei vollständig und adressierbar bleiben.
- **02 Strukturelle Duplikate:** Die aktuelle Erkennung vergleicht identifierhaltige Token-N-Gramme. Der Referenzfall zeigt eine mögliche Erkennungslücke, beweist aber keinen allgemein geeigneten Ähnlichkeitsschwellenwert. Gleiche API-Aufrufsequenzen können legitime Adapter oder Standardprotokolle sein; ein vorgeschlagener Komponentenname oder eine Extraktion ist keine automatisch ableitbare Tatsache.
- **03 Schichtentrennung:** Ein Scanner mit Markdown-Rendering ist ein brauchbarer Prüfbeleg. Ein allgemeiner Checker auf `Microsoft.CodeAnalysis` plus `StringBuilder` würde jedoch Roslyn-Werkzeuge und andere legitime Kombinationen systematisch fehlmelden. Schichtgrenzen sind ohne explizite Projektarchitektur oft nicht statisch bestimmbar; Dateilänge allein beweist keine Verantwortungsverletzung.
- **04 Externer Audit:** Die CLI bietet derzeit nur `review` und `baseline`, die Konfiguration und Ausgabe im Zielroot verwenden. Core kann mit `ValidateForAudit` bereits einen separaten absoluten Ausgabepfad validieren; dieser Weg ist nur über den manuellen Auditablauf nutzbar. Der CLI-Befehl ist ein allgemeiner Nutzbarkeitsgewinn. Eine Zusicherung, dass der gesamte Ladevorgang keinerlei Dateien im Ziel anlegt, ist mit dem aktuellen `MSBuildWorkspace`-Aufruf noch nicht belegt.
- **05 Strukturierter Export:** `ReviewRunner` besitzt bereits Findings, Metriken, Belege, `changed-files`-Selektion und Beziehungen über Symbolidentität. Der Markdown-Writer gibt davon nur einen Teil aus. Ein versionierter JSON-Export kann dieselben tatsächlichen Daten für Agenten und andere Werkzeuge bereitstellen. Automatische Qualitätsnoten, Refactoring-Anweisungen und „wahrscheinliche False Positives“ wären unbelegte Urteile.

## Scope

### Muss

- Einen eigenständigen CLI-Audit für eine angegebene C#-Solution oder deren Root mit Ausgabe außerhalb des Zielroots konzipieren; Konfiguration und Baseline dürfen für diesen Modus nicht im Ziel angelegt werden. Eingaben, Ausgabepfad, Fehlerfälle und Baseline-Verhalten müssen eindeutig sein.
- Einen deterministischen, versionierten maschinenlesbaren Export derselben aktuellen Findings konzipieren, mit Identitäten, Orten, Messwerten, Belegen, Beziehungen und klar getrennter `changed-files`- und `all-findings`-Sicht. Keine zusätzliche inhaltliche Bewertung durch den Export.
- Die verbleibende Compiler-Einstiegspunkt-Lücke bei Dead-Code-Kandidaten schließen und die Darstellung großer Finding-Mengen so strukturieren, dass einzelne Befunde und ihr vollständiger Satz erhalten bleiben.
- Die Vorschläge anhand verschiedener C#-Projektarten sowie positiver und negativer Gegenbeispiele prüfen. Ergebnisse dürfen weder Build-Fehler noch automatische Refactorings auslösen.

### Nicht

- Projekt- oder Framework-Namen, Namespace-Muster, bestimmte Dateilängen oder konkrete Refactoring-Ziele aus AiNetCodeNavigator als allgemeine Regeln fest einbauen.
- `decisionCount` durch pauschale Gewichte für Switch-Arme oder Guard-Ketten ersetzen.
- Im ersten Ergebnis einen neuen strukturellen Duplikat- oder Schichtentrennungs-Checker aufnehmen, solange Präzision, Gegenbeispiele und allgemeine Semantik ungeklärt sind.
- Findings aus dem strukturierten Export unterdrücken oder automatisch als Fehler, False Positive oder Refactoring-Auftrag klassifizieren.

## Verifikation

- Die Eingangspfade, Ausgabepfade und Schreibwirkungen des externen Audits mit Integrationstests gegen eine fremde Solution prüfen; bei beanspruchter strikter Schreibfreiheit auch die Wirkungen des MSBuild-Ladevorgangs nachweisen.
- JSON-Schema, stabile Ordnung, vollständige Finding-Zuordnung, beide Sichten und Publication/Fehlerverhalten gegen die bestehenden Runner-Daten prüfen.
- Entry-Point-Fälle für ausführbare Projekte und Gegenbeispiele für Bibliotheken prüfen. Für verdichtete Darstellung muss jedes einzelne Finding wieder auffindbar bleiben.
- Allgemeinheit mit unterschiedlichen C#-Solutions und absichtlich ähnlichen, aber fachlich verschiedenen Mustern prüfen; prozentuale Rauschreduktion nicht aus einem Audit extrapolieren.

## Arbeitsgedächtnis (nur Draft)

- **Entscheidung 1:** Soll das erste Ergebnis auf die drei empfohlenen Bereiche (CLI-Audit, strukturierter Export, belegte Rauschkorrekturen) begrenzt werden? Die Vorschläge 02 und 03 blieben zunächst eigene spätere Konzepte.
- **Entscheidung 2:** Welche Garantie ist für den externen Audit erforderlich: keine von AiNetReview gezielt angelegten Dateien im Zielroot, oder nachweislich keinerlei Schreibzugriff einschließlich möglicher MSBuild-Nebeneffekte? Letzteres kann eine isolierte Arbeitskopie oder eine andere technische Grenze erfordern.
- Danach zu definieren: CLI-Eingabe- und Konfigurationsvertrag sowie genaue JSON-Felder und Aggregationsform. Diese Entscheidungen gehören vor `status: ready` in das Konzept.
