---
status: ready
---

# Dead-Code-Prüfkandidaten als Review-Regel

## Intention

AiNetReview soll auf Basis des vorhandenen AiNetLinter-Kerns mögliche ungenutzte C#-Deklarationen in einer geladenen Solution zur menschlichen Prüfung melden. Ein Befund bedeutet, dass im untersuchten Kontext keine relevante Nutzung nachgewiesen wurde; er ist weder ein Beweis für toten Code noch eine Aufforderung zum automatischen Löschen.

## Ausgangspunkt

AiNetLinter untersucht derzeit explizite Typen und gewöhnliche Methoden, berücksichtigt solutionweite statische Referenzen und erkannte indirekte Bindungen und unterdrückt Kandidaten bei unsicherer Abdeckung. Die dortige MCP-Ausgabe, Snapshot-Fortsetzung und Zeitbudgets gehören zur Linter-Hülle. AiNetReview führt dagegen konfigurierte `IReviewRule`-Regeln auf einem geladenen Roslyn-Stand aus und veröffentlicht vollständige Markdown-Berichte; Abbruch oder unvollständige Analyse lässt den Lauf scheitern. Die Übertragung benötigt daher eine eigene Review-Regel statt eines unveränderten MCP-Scanners.

Die **ausschließlich lesende Referenz** liegt unter `C:\Daten\Entwicklung\Ralf\AiNetLinter` (untersuchter Commit `bc47e0875bc807075fa4c1591c40ac381c8365b8`). Einstieg: `src\AiNetLinter\Mcp\Tools\Verify\DeadCode\DeadCodeAdvisoryScanner.cs`; für die fachlichen Mechanismen insbesondere `DeadCodeUsageIndex.cs`, `DeadCodeIndirectUsage.cs`, `DeadCodeMarkupUsage.cs`, `DeadCodeWhitelist.cs` und `DeadCodeApiSurfacePolicy.cs` im selben Ordner. Die vorhandenen Verhaltensbelege stehen unter `src\AiNetLinter.FastTests\Mcp\Tools\DeadCode\` und in `Docs\mcp\dead-code.md`. AiNetReview entlehnt passende Algorithmen und Schutzsignale mit eigenen Verträgen, keine Klassenstruktur oder MCP-Hülle eins zu eins. Bei Abweichungen gelten die in diesem Konzept festgelegten AiNetReview-Verträge.

## Scope

### Muss

- Eine explizit registrierte, konfigurierbare Produktionsregel analysiert den geladenen Solution-Stand ohne erneutes Lesen der Quelldateien während der Regel. Kandidaten sind nur explizit deklarierte C#-Typen und gewöhnliche Methoden einschließlich Extension-Methoden aus produktiven Projekten. Sie liefert aktuelle, reproduzierbar sortierte `FindingDraft`-Prüfkandidaten mit Deklarationsort, Quellausschnitt, nachvollziehbarer Begründung und konkreten Gegenprüffragen.
- Der fachliche Kern berücksichtigt solutionweit semantisch aufgelöste Referenzen einschließlich Testreferenzen, Methodengruppen und Referenzen aus generiertem C#-Code. Die Nutzung eines Members schützt auch seinen deklarierenden Typ; reine Selbstreferenzen innerhalb eines Typs sind kein Beleg für dessen Nutzung von außen. Erkannte indirekte Einstiege und Bindungen, etwa Entry-Point-Attribute, Reflection, DI und Markup, schützen betroffene Symbole. Symbolbezogen unaufgelöste Bindungen unterdrücken den jeweiligen Kandidaten; eine global unvollständige Referenzabdeckung lässt den Lauf scheitern.
- Wenn `dead-code-candidates` konfiguriert ist, erfasst der Solution-Lader relevante `.razor`-, `.xaml`- und `.js`-Dateien aus den analysierten Projekten vor dem Regellauf als Teil desselben unveränderlichen Snapshots, einschließlich nicht als Roslyn-`AdditionalDocuments` eingebundener Projektdateien. Die Markup-Suche bleibt auf die Projektwurzel begrenzt und folgt keinen Reparse Points; Build-Ausgaben, Abhängigkeitsordner und fremde verschachtelte Projekte werden ausgespart. Die AiNetLinter-Grenzen von insgesamt 2.000 Markup-Dateien und 1 MiB pro Datei dienen als feste Sicherheitsgrenzen. Nicht lesbare Dateien, überschrittene Grenzen und nicht auswertbares relevantes Markup sind fehlende Abdeckung und führen zu einem fehlgeschlagenen Lauf statt zu einem scheinbar vollständigen Bericht. Läufe ohne diese Regel benötigen keine Markup-Erfassung.
- Die Regel verwendet die bestehende Quellklassifikation für Testprojekte und generierten Code. Ein als unreferenziert erkannter Typ erscheint einmal als Gruppe; seine Methoden werden nicht zusätzlich einzeln gemeldet. Interface-Verträge und deren Implementierungen, Overrides, Compiler-Einstiege und generierte Deklarationen werden nicht als einzelne Methoden gemeldet.
- Die Regel-ID ist `dead-code-candidates`. Ihre Option `apiSurface` hat den Default `external_library` und akzeptiert auch `closed_solution`; öffentlich sichtbare API wird im Default geschützt. `entryPointAttributes` ist eine Liste zusätzlicher vollqualifizierter Attributtypen; die festen Defaults `System.Runtime.CompilerServices.ModuleInitializerAttribute` und `Microsoft.JSInterop.JSInvokableAttribute` bleiben aktiv. Die Typidentität wird semantisch verglichen. Der Kommentar `// ainetreview-disable dead-code -- <Begründung>` unmittelbar vor einer Deklaration unterdrückt nur deren Kandidatenbefund; ohne nichtleere Begründung wirkt er nicht. Die bestehende Projektklassifikation gilt; eine neue Projektrollenoption gehört nicht zum Vorhaben.
- Allgemein verwendbare Snapshot-, Projektklassifikations- und Roslyn-Referenzfunktionen, die diese Regel konkret benötigt, werden in der gemeinsamen Analyseinfrastruktur gebündelt und vorhandenen Regeln zugänglich gemacht, statt Dead-Code-spezifisch im Regelordner oder mehrfach implementiert zu werden. Die Regel-spezifische Kandidatenentscheidung bleibt bei der Regel. Es entsteht keine abstrakte Regelbasis und kein neues dynamisches Erweiterungssystem.
- Ein erfolgreich leerer Bericht bedeutet: Die definierte Kandidatenmenge wurde vollständig geprüft und ergab keinen Befund. Abbruch oder unvollständige Auswertung führt gemäß bestehendem Runner-Vertrag zu einem fehlgeschlagenen Lauf statt zu einem scheinbar vollständigen Nullbericht.
- Die bereits umfangreichen AiNetLinter-Tests werden als Verhaltensreferenz gelesen, nicht als zweite Testsuite kopiert. Gezielte AiNetReview-Tests prüfen die hier neu entstehenden Verträge: repräsentative echte und geschützte Kandidaten, Optionen und Suppression, Markup-/Generat-Nutzung aus dem Snapshot, vollständige Fehlerbehandlung bei Abdeckungslücken sowie Registrierung und Markdown-Befund. Jede neue beobachtbare Produktionsfunktion erhält einen passenden automatisierten Nachweis; die bestehende AiNetLinter-Fallmatrix wird nicht dupliziert. Aktuelle `docs/`-Seiten werden erst mit der Umsetzung angepasst.

### Nicht

- Keine automatische Entfernung oder Änderung analysierten Codes; kein Build- oder Analyzer-Fehler aus einem Prüfkandidaten.
- Keine Übernahme der AiNetLinter-MCP-Tools, `verify`-Gate-Daten, `h:`-IDs, Zeitbudgets, Snapshot-Caches oder Pagination in AiNetReview.
- Keine Änderung am AiNetLinter-Repository.
- Keine Kandidaten für Felder, Konstanten, Properties, Record-Komponenten, Events, Indexer, Enum-Werte, Konstruktoren, Accessoren, Operatoren, Finalizer, lokale Funktionen oder Variablen und Testprojekt-Deklarationen.
- Kein mechanisches Kopieren der AiNetLinter-Architektur und keine zweite Vollabdeckung ihrer vorhandenen Testmatrix.
