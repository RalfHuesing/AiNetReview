# Wenige starke Signale für agentische Code-Audits

Status: ergänzende Ideennotiz, keine verbindliche Spezifikation und kein implementiertes Produktverhalten. Lokale Auswertungen (`method-control-flow-outliers`, `code-size-candidates`) und das Änderungsrisiko (Baseline-gestützte `changed-files`-Audits, `missing-test-evidence-candidates`) sind bereits umgesetzt; die [Ideensammlung](erste-fachliche-review-signale.md) diskutiert weitere Ansätze.

## Grundgedanke

AiNetReview braucht keine große Sammlung kleinteiliger Qualitätsanalysen. Wenige überprüfbare Signale können einem Agenten die Stellen zeigen, an denen ein Audit sinnvoll ist. Ein Treffer ist eine Frage an den Code und seinen Kontext, keine Anweisung zur sofortigen Änderung. Der Agent soll die Absicht, Aufrufer, Verträge und Tests prüfen und anschließend begründet ändern oder den Fall akzeptieren. Automatisches Aufteilen einer Methode nur zur Senkung eines Messwerts kann das Verständnis verschlechtern.

## Offene Blickrichtung: Verstreuter Kontext

Wie viele Methoden, Dateien und Zustandsübergänge muss ein Agent für einen fachlichen Ablauf zusammenführen? Konkrete Beziehungen sind wichtiger als ein abstrakter Graphwert. Diese Analyse könnte für agentische Entwicklung besonders wertvoll sein, ist aber schwieriger zuverlässig umzusetzen.

Aktuelle Benchmarks zeigen Schwierigkeiten beim Verstehen komplexen Codes, belegen aber keinen isolierten C#-Grenzwert für Verschachtelung. In [LongCodeU](https://aclanthology.org/2025.acl-long.1324/) waren Beziehungen zwischen Codeeinheiten für die untersuchten Modelle besonders schwierig. Die [SWE-Flux-Vorabveröffentlichung](https://arxiv.org/abs/2609.28449) untersucht Laufzeitfragen in Python-Repositories und berichtet größere Probleme bei Datenfluss, Ausführung über Methoden hinweg und präzisem Zustand als bei lokalem Kontrollfluss. Das ist ein Hinweis gegen rein kosmetisches Verteilen verschachtelter Logik auf viele Hilfsmethoden, kein Beweis, dass Verschachtelung harmlos wäre. Ergebnisse hängen von Aufgaben, Sprachen, Modellen und Werkzeugunterstützung ab.

Weitere Signale erst aufnehmen, wenn sie einen zusätzlichen Nutzen zeigen.
