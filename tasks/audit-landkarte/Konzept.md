---
status: draft
---

# Audit-Landkarte für fokussierte Agentenaufträge

## Intention

Ein auditierender Agent soll einen zusammenhängenden Arbeitsbereich mit seinen unterschiedlichen Signalen und seinem Testkontext untersuchen können, ohne dieselben Dateien aus mehreren Analyseberichten zusammensuchen zu müssen. AiNetReview ergänzt dafür die bestehenden Berichte um eine nachvollziehbare Audit-Landkarte mit eindeutig adressierbaren Arbeitspaketen.

Die Landkarte organisiert den Prüfauftrag. Sie bewertet keine Findings, behauptet keine gemeinsamen Fehlerursachen und verlangt keine Refaktorierung. Ein Paket kann Größen-, Kontrollfluss-, Dead-Code-, Duplikations- und Testpfadsignale gemeinsam enthalten. Seine Bezeichnung beschreibt den untersuchten Codebereich, nicht die Analyseart.

## Entscheidung und Vorgehen

Wir entwickeln eine kleine erste Produktversion mit deterministischer technischer Gruppierung. Die erste reale Landkarte entsteht während der Umsetzung und ist ein Abnahmebeispiel. Eine separate PoC-, Benchmark- oder längere Messphase ist keine Voraussetzung für den Beginn der Umsetzung.

Die erste Version nutzt Projekt- und Verzeichnisgrenzen sowie belegte statische Beziehungen zu Tests. Das ergibt technische Arbeitsbereiche, die einem Agenten als Ausgangspunkt für eine fachliche Untersuchung dienen. Echte Geschäftsdomänen oder Verantwortungen werden daraus nicht als erwiesene Tatsache abgeleitet. Der Agent kann den Zuschnitt bei der Untersuchung hinterfragen, muss aber die ursprünglichen Finding-IDs vollständig nachvollziehbar halten.

Ein reiner Prompt würde Zuordnung, Vollständigkeitskontrolle und Navigation bei jedem Audit erneut dem Agenten überlassen. Diese drei Aufgaben werden im Produkt gelöst; der Prompt steuert die kontextbezogene Untersuchung und Bewertung.

## Gelesene Ausgangsbasis

- [Current findings](../../docs/review/findings.md) und die Implementierungen von [FindingDraft](../../src/AiNetReview.Core/Findings/FindingDraft.cs), [ReviewFinding](../../src/AiNetReview.Core/Analysis/ReviewFinding.cs), [ReviewFindingBuilder](../../src/AiNetReview.Core/Analysis/ReviewFindingBuilder.cs) und [MarkdownReportWriter](../../src/AiNetReview.Core/Reporting/MarkdownReportWriter.cs) wurden gelesen.
- Validierte Findings enthalten Symbolidentitäten, Quellen, Belege und getrennte Kontext- und Subjektvorkommen. Bereits vorhandene Beziehungen verbinden Findings über Projekt- und Symbolidentität. Diese Beziehungen ergeben noch keine Arbeitspakete nach Verantwortung; Markdown-Related-Links zeigen derzeit auf Analyseberichte.
- Der [SolutionReferenceIndex](../../src/AiNetReview.Core/Analysis/SolutionReferenceIndex.cs) bietet semantische Referenzen einschließlich Testherkunft. Der [Testpfad-Graph](../../src/AiNetReview.Core/ReviewAnalyses/MissingTestEvidenceCandidates/MissingTestEvidenceSemanticGraphBuilder.cs) ist eine vorhandene spezialisierte Implementierung, kein bereits exportierter allgemeiner Modulgraph.
- Der strukturell gelesene Lauf `20261001T105806Z-db0d277a` enthält in `changed-files` 284 Findings: 234 production und 50 tests. Mehrere Analyseberichte enthalten Signale zu denselben Quellen. Die Findings wurden für dieses Konzept nicht inhaltlich bewertet.

## Scope

### Muss

#### Zusätzliche Sicht und unveränderter Prüfauftrag

- Jeder neue Review-Lauf veröffentlicht zusätzlich `audit-map/changed-files/index.md`, Paketberichte und ein JSON-Manifest. Eine getrennte `audit-map/all-findings/`-Sicht verwendet denselben Vertrag für den vollständigen Findings-Bestand.
- Der bestehende Root-Index verlinkt die zusätzliche Navigation. Bestehende Analyseberichte, Herkunftsbereiche, CLI-Antwort und Baseline-Verhalten bleiben gültig. Die Landkarte ist eine weitere Sicht auf dieselben validierten Findings, keine neue Review-Analyse.
- `changed-files` verwendet exakt die vorhandene Reporting-Auswahl, einschließlich der snapshotweiten Auswahl für `missing-test-evidence-candidates`. Die vollständige Sicht benötigt weiterhin einen ausdrücklich beauftragten vollständigen Audit.
- Quellen ohne eigenes Finding dürfen als Prüfkontext erscheinen. Das erweitert den Finding-Auftrag nicht. Verweise auf Findings außerhalb der gewählten Sicht müssen als außerhalb des Auftrags erkennbar sein und dürfen deren Details nicht in die begrenzte Sicht übernehmen.
- Ein fehlerhafter oder abgebrochener Lauf veröffentlicht keine unvollständige Landkarte. Bestehende atomare Veröffentlichung, Pfadgrenzen, externe Auditziele und Erhaltung älterer Läufe gelten auch für die neuen Dateien.

#### Nachvollziehbare Gruppierung

- Ein technischer Produktionsbereich wird durch Projektpfad und den unmittelbar enthaltenden Quellordner bestimmt. Dateien direkt am Projektwurzelverzeichnis bilden dessen Wurzelbereich. Die Ausgabe verwendet daraus abgeleitete lesbare Namen und behauptet keine automatisch erkannte Geschäftsdomäne.
- Eine nichtgenerierte Testdatei wird einem Produktionsbereich zugeordnet, wenn ihre semantisch aufgelösten Referenzen auf nichtgenerierte Produktionsquellen genau einen solchen Bereich benennen. Die Begründung und konkreten Referenzorte bleiben sichtbar. Das ist eine Zuordnungshilfe, kein Nachweis von Testausführung, Assertion-Qualität oder Laufzeitabdeckung.
- Bei Referenzen auf mehrere Produktionsbereiche, fehlenden aufgelösten Referenzen oder erkannter Bindungsunsicherheit bleibt die Testdatei in ihrem eigenen technischen Testbereich aus Testprojekt und Quellordner. Erkannte Produktionsbezüge werden als Kontextbeziehungen gezeigt. Eine fehlende Zuordnung darf keine fehlenden Tests behaupten.
- Testzuordnungen werden aus dem geladenen Quellkontext ermittelt, nicht nur aus Findings. Dadurch können relevante Testdateien ohne eigene Signale sichtbar werden. Der Kontext umfasst die zugeordneten Testdateien und die direkt für die Paket-Findings ausgewiesenen Beleg- und Symbolorte; es erfolgt keine rekursive Aufnahme aller erreichbaren Quellen.
- Findings werden anhand ihrer Subjektvorkommen den technischen Bereichen zugeordnet. Kontextsymbole und Testpfadbelege ändern die Finding-Herkunft nicht. Dateifindings und zusätzliche Partial-Deklarationen behalten ihre nachgewiesenen Orte.
- Liegen die Subjekte eines Findings in einem Bereich, gehört es zu dessen Paket. Liegen sie in mehreren Bereichen, erhält es ein gemeinsames Paket für diese sortierte Bereichsmenge, mit Verweisen aus den beteiligten Bereichen. Das gilt insbesondere für Duplikate, Fragmente und Weiterleitungsketten; jedes beteiligte Vorkommen bleibt zugänglich.
- Jedes Finding besitzt je Sicht genau ein primär zuständiges Paket. Verweise aus anderen Paketen erzeugen keinen zweiten Prüfauftrag. Beziehungen zwischen Paketen führen nicht zu deren transitiver Zusammenlegung; ein gemeinsamer Helfer darf nicht die gesamte Anwendung zu einem Cluster verbinden.
- Für identische validierte Ergebnisse, Quellen und Sicht ist die Zuordnung unabhängig von Enumerationsreihenfolgen deterministisch. Jede Zuordnung nennt ihren Grund. Es gibt keine Top-N-Auswahl, keine künstliche Höchstzahl von Findings und keine ausgeblendeten Restmengen. Große Pakete erhalten Navigation nach Dateien und Symbolen.

#### Agententaugliche Ausgabe

- Jedes Finding erhält eine eindeutige ID aus seiner vorhandenen Identität: Analyse, Projekt, repräsentative Quelle, Subject-ID und Discriminator. Paket-IDs ergeben sich aus der Bereichsidentität beziehungsweise der sortierten Bereichsmenge. IDs bleiben bei identischen Eingaben gleich; Umbenennungen und Verschiebungen versprechen keine historische Identität.
- Paketberichte zeigen ID, Name, Sicht, zuständige Finding-IDs, Analysearten, Quellen, Testkontext, Zuordnungsgründe und Beziehungen zu anderen Paketen. Sie behalten die Originalsignale, Messwerte, Belege, Herkunft und Unsicherheiten bei. Links beziehungsweise Anker führen zu konkreten Findings, nicht ausschließlich zu einer gesamten Analysedatei.
- Das versionierte JSON-Manifest enthält Lauf und Sicht, die Findings samt ihren Originalidentitäten und relevanten Belegen, die Paketzuordnung, ausgewiesenen Kontext und Paketbeziehungen. Markdown und JSON entstehen aus derselben Aufbereitung; ein Agent muss Markdown nicht zur Vollständigkeitskontrolle parsen.
- Die Paketübersicht nennt eindeutige Findings-Zahlen und macht eine leere Sicht ausdrücklich sichtbar. Die Summe der primär zugeordneten Findings entspricht exakt der Findings-Zahl der Sicht. Überlappender Kontext wird nicht als zusätzlicher Finding-Bestand gezählt.
- Die gemeinsame Audit-Anleitung enthält einen konkreten Paketauftrag, etwa: „Prüfe im Lauf X, Sicht changed-files, Paket Y alle zuständigen Findings anhand von Implementierung, Aufrufern, Verträgen und Tests. Begründe jede Einstufung und benenne offenen Kontext sowie nicht geprüfte Pakete.“ Ein Paketauftrag ist keine automatische Freigabe zu Änderungen.
- Die vorhandenen Fragen und Einstufungen gelten weiter: false positive, acceptable design, needs clarification oder actionable. Der Agent untersucht verwandte Signale gemeinsam, hält seine Begründung aber für jedes Finding nachvollziehbar. Ein teilweise bearbeiteter Paketauftrag darf keinen vollständigen Audit behaupten.

### Nicht

- Keine separate Vorstudie, kein wegwerfbarer PoC und keine quantitativ belegte Zeitersparnis als Umsetzungsvoraussetzung.
- Keine LLM-Abhängigkeit, externe Dienste, MCP-Laufzeitabhängigkeit oder automatische Deutung von Geschäftsdomänen in der Berichtserzeugung.
- Keine neue Modulkonfiguration, frei konfigurierbare Clusterregeln oder manuelle Paketpflege in dieser ersten Version.
- Keine Bewertung, Prioritäts- oder Gesamtscores, neuen Analysesignale, Änderung der bisherigen Schwellen oder Einschränkung der vorhandenen Finding-Auswahl.
- Keine automatische Refaktorierung, Agentenausführung oder parallele Umsetzung mehrerer Pakete. Ein gemeinsamer Kontext ist keine Garantie unabhängiger Änderungen.
- Keine persistierten Review-Entscheidungen, Bearbeitungsstatus, historische Wiederöffnung oder neue Baseline-Semantik. Der Agent berichtet die Untersuchungsergebnisse; die Landkarte bleibt ein Laufartefakt.
- Keine automatische Ableitung von Dokumentationszuständigkeiten oder vollständige Modellierung dynamischer Aufrufe, Reflection, DI und Laufzeit-Testabdeckung.
- Keine Migration oder nachträgliche Bearbeitung vorhandener veröffentlichter Auditläufe.

## Verifikation und erste reale Anwendung

Die Umsetzung prüft die Verträge mit automatisierten Tests und erzeugt anschließend eine erste reale Landkarte beim regulären Repository-Audit. Der gelesene Lauf dient als strukturelles Beispiel; die neue Funktion wird gegen einen neuen Snapshot ausgeführt. Die frühere Anzahl von 284 Findings ist kein festgeschriebener erwarteter Wert.

Die automatisierte Abnahme deckt mindestens ab:

- Vollständige, eindeutige Zuordnung aller Findings je Sicht, einschließlich leerer Ergebnisse, einzelner Dateien, mehrfach betroffener Symbole und bereichsübergreifender Subjekte.
- Gleiche IDs und Zuordnung bei identischen Eingaben mit unterschiedlicher Enumerationsreihenfolge; unveränderte Originalsignale und Projektrollen.
- Eindeutige Testzuordnung sowie separate Testbereiche bei mehreren Bezügen, keinem Bezug und Bindungsunsicherheit; Testkontext ohne eigenes Finding.
- Keine transitive Verschmelzung durch gemeinsame Testhelfer oder andere Paketbeziehungen; vollständige Vorkommen in gemeinsamen Paketen.
- Unveränderte Baseline- und Scope-Auswahl, insbesondere snapshotweite Testpfadsignale und keine Übernahme ausgeschlossener Finding-Details in `changed-files`.
- Übereinstimmung zwischen Manifest, Übersicht und Paketberichten; auflösbare Finding-Links, sichere Pfade und vollständige Veröffentlichung auch bei externen Auditwurzeln.
- Keine veröffentlichte Teilmenge bei Fehler oder Abbruch und Erhaltung bisheriger Berichtspfade sowie älterer Läufe.

Bei der ersten realen Anwendung wird für ein repräsentatives Paket geprüft, ob ein Agent vom Paketbericht aus die zuständigen Findings, Quellen und zugeordneten Tests untersuchen und jede Einstufung unter ihrer ID berichten kann. Diese begrenzte Untersuchung ist Teil der Umsetzung und ihrer Abnahme, kein Auftrag zur Behebung sämtlicher Repository-Findings.

Die Abnahme verlangt korrekte Zuordnung, bedienbare Navigation und einen konkret ausführbaren Paketauftrag. Sie verlangt weder eine Vergleichsstudie noch ein inhaltlich abgeschlossenes Audit des gesamten Repositories. Sichtbare Fehlzuordnungen werden innerhalb dieser ersten Version korrigiert, ohne die Nicht-Ziele still zu erweitern.
