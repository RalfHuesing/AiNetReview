---
status: draft
---

# Allgemeine Erweiterungen für C#-Audits

## Intention

AiNetReview soll für unterschiedliche C#-Solutions verlässliche, nachvollziehbare Review-Signale liefern und diese ohne Eingriffe in das untersuchte Repository für Menschen und nachgelagerte Agenten nutzbar machen. Die Fallstudie AiNetCodeNavigator dient als Gegenbeispiel und Testmaterial, nicht als Vorlage für fest eingebaute Projektregeln.

## Empfehlung und Stand der Prüfung

Die fünf vorhandenen Vorschläge sind Problemhypothesen. Das erste zusammenhängende Ergebnis umfasst einen extern nutzbaren Audit-Befehl, einen strukturierten Export der tatsächlichen Findings und gezielte Korrekturen belegter Rauschquellen. Neue Architekturheuristiken benötigen zuerst einen allgemeinen Signalvertrag und Gegenproben an verschiedenartigen C#-Projekten.

- **01 Rauschunterdrückung:** `DeadCodeCandidatesAnalysis` fragt den Roslyn-Einstiegspunkt bereits ab und schützt die Methode; der enthaltende Typ wird vorher als eigener Kandidat geprüft und ist dadurch noch nicht geschützt. `method-control-flow-outliers` verlangt für den Zählpfad bereits mindestens zwei Entscheidungskonstrukte; der Test für einen einzelnen flachen Switch erwartet keinen Befund. Eine pauschale Gewichtung von Guard-Ketten würde die bisherige Entscheidungsmetrik ändern, obwohl Guard-Ketten weiter Verzweigungen und Prüfaufwand enthalten. Die behaupteten Reduktionsprozente sind durch den Einzelfall nicht belegt. Bei `missing-test-evidence-candidates` kann eine kompakte Darstellung hilfreich sein, die zugrunde liegenden Findings müssen dabei vollständig und adressierbar bleiben.
- **02 Strukturelle Duplikate:** Die aktuelle Erkennung vergleicht identifierhaltige Token-N-Gramme. Der Referenzfall zeigt eine mögliche Erkennungslücke, beweist aber keinen allgemein geeigneten Ähnlichkeitsschwellenwert. Gleiche API-Aufrufsequenzen können legitime Adapter oder Standardprotokolle sein; ein vorgeschlagener Komponentenname oder eine Extraktion ist keine automatisch ableitbare Tatsache.
- **03 Schichtentrennung:** Ein Scanner mit Markdown-Rendering ist ein brauchbarer Prüfbeleg. Ein allgemeiner Checker auf `Microsoft.CodeAnalysis` plus `StringBuilder` würde jedoch Roslyn-Werkzeuge und andere legitime Kombinationen systematisch fehlmelden. Schichtgrenzen sind ohne explizite Projektarchitektur oft nicht statisch bestimmbar; Dateilänge allein beweist keine Verantwortungsverletzung.
- **04 Externer Audit:** Die CLI bietet derzeit nur `review` und `baseline`, die Konfiguration und Ausgabe im Zielroot verwenden. Core kann mit `ValidateForAudit` bereits einen separaten absoluten Ausgabepfad validieren; dieser Weg ist nur über den manuellen Auditablauf nutzbar. Der CLI-Befehl ist ein allgemeiner Nutzbarkeitsgewinn. Eine Zusicherung, dass der gesamte Ladevorgang keinerlei Dateien im Ziel anlegt, ist mit dem aktuellen `MSBuildWorkspace`-Aufruf noch nicht belegt.
- **05 Strukturierter Export:** `ReviewRunner` besitzt bereits Findings, Metriken, Belege, `changed-files`-Selektion und Beziehungen über Symbolidentität. Der Markdown-Writer gibt davon nur einen Teil aus. Ein versionierter JSON-Export kann dieselben tatsächlichen Daten für Agenten und andere Werkzeuge bereitstellen. Automatische Qualitätsnoten, Refactoring-Anweisungen und „wahrscheinliche False Positives“ wären unbelegte Urteile.

## Scope

### Muss

- Einen eigenständigen CLI-Audit für eine angegebene C#-Solution (`.sln`/`.slnx`) oder deren Root anbieten. `--output` benennt verpflichtend ein absolutes Verzeichnis außerhalb des kanonischen Zielroots. Ohne explizite Solution muss die Auswahl eindeutig sein. Die Standardkonfiguration wird im Speicher erzeugt; eine ausdrücklich angegebene Konfigurationsdatei wird nur gelesen und darf auch im Ziel liegen. `--baseline-only` aktualisiert ausschließlich die Baseline im externen Ausgabeverzeichnis.
- **Strikte Schreibgrenze:** Der Auditprozess einschließlich MSBuild-Auswertung darf im externen Zielrepository keine Datei anlegen, ändern oder löschen. Konfiguration, Baseline, Reports, Logs und temporäre Buildartefakte liegen außerhalb. Die Prüfung erfolgt vor dem ersten möglichen Schreibzugriff; bei nicht sicher durchsetzbarer Grenze schlägt der Audit geschlossen fehl. Eine isolierte Arbeitsumgebung muss auch benutzerdefinierte MSBuild-Targets und absolute Pfade daran hindern, in den Zielroot zu schreiben. Befunde und Quellverweise zeigen dennoch auf die Originaldateien.
- Einen deterministischen, versionierten maschinenlesbaren Export der tatsächlichen Findings in der veröffentlichten Reportserie anbieten. Er enthält Identitäten, Orte, Messwerte, Belege und Beziehungen. `changed-files` und `all-findings` sind eindeutig unterscheidbar; beide verwenden dieselben Finding-Identitäten und enthalten keine erfundenen Bewertungen oder Handlungsempfehlungen.
- Die verbleibende Compiler-Einstiegspunkt-Lücke bei Dead-Code-Kandidaten schließen. Viele Findings werden im Markdown nach Projekt und Quelldatei gruppiert und gezählt; jeder einzelne Befund bleibt im Detailbericht und JSON-Export adressierbar.
- Die Vorschläge anhand verschiedener C#-Projektarten sowie positiver und negativer Gegenbeispiele prüfen. Ergebnisse dürfen weder Build-Fehler noch automatische Refactorings auslösen.

### Nicht

- Projekt- oder Framework-Namen, Namespace-Muster, bestimmte Dateilängen oder konkrete Refactoring-Ziele aus AiNetCodeNavigator als allgemeine Regeln fest einbauen.
- `decisionCount` durch pauschale Gewichte für Switch-Arme oder Guard-Ketten ersetzen.
- Im ersten Ergebnis einen neuen strukturellen Duplikat- oder Schichtentrennungs-Checker aufnehmen, solange Präzision, Gegenbeispiele und allgemeine Semantik ungeklärt sind.
- Findings aus dem strukturierten Export unterdrücken oder automatisch als Fehler, False Positive oder Refactoring-Auftrag klassifizieren.
- Einen `--read-only`-Schalter anbieten, der die Schutzgarantie abschaltbar macht, oder bei fehlender Isolation direkt im Zielroot laden.
- `.csproj` ohne Solution im ersten Ergebnis als Eingabe anbieten; der bestehende Loader unterstützt `.sln` und `.slnx`.

## Verifikation

- Die Eingangspfade, Ausgabepfade und Schreibwirkungen des externen Audits mit Integrationstests gegen fremde Solutions prüfen. Neben Dateisystem-Snapshots die Schreibgrenze technisch erzwingen und mit einem absichtlich schreibenden MSBuild-Target sowie Junctions/Symlinks und absolut referenzierten Zielpfaden gegenprüfen. Fehler dürfen im Ziel keine Teilprodukte hinterlassen.
- JSON-Schema, stabile Ordnung, vollständige Finding-Zuordnung, beide Sichten und Publication/Fehlerverhalten gegen die bestehenden Runner-Daten prüfen.
- Entry-Point-Fälle für ausführbare Projekte und Gegenbeispiele für Bibliotheken prüfen. Für verdichtete Darstellung muss jedes einzelne Finding wieder auffindbar bleiben.
- Allgemeinheit mit unterschiedlichen C#-Solutions und absichtlich ähnlichen, aber fachlich verschiedenen Mustern prüfen; prozentuale Rauschreduktion nicht aus einem Audit extrapolieren.

## Arbeitsgedächtnis (nur Draft)

- **Festgelegt:** Erster Umfang sind CLI-Audit, strukturierter Export und belegte Rauschkorrekturen. Die Vorschläge 02 und 03 bleiben außerhalb dieses Ergebnisses. Im externen Zielrepository gilt absolute Schreibfreiheit, auch für MSBuild-Nebeneffekte.
- **Noch auszuarbeiten:** Die technische Durchsetzung der Schreibgrenze und das genaue CLI-/JSON-Vertragsschema. Eine bloße Arbeitskopie ohne Zugriffssperre auf den Originalroot erfüllt die Garantie nicht. Diese Verträge müssen vor `status: ready` eindeutig sein.
