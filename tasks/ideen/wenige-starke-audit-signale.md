# Wenige starke Signale für agentische Code-Audits

Status: ergänzende Ideennotiz, keine verbindliche Spezifikation und kein implementiertes Produktverhalten. Die statistische Kandidatenauswahl ist als Analyse [method-control-flow-outliers](../../docs/review/findings.md) umgesetzt; die [Ideensammlung](erste-fachliche-review-signale.md) diskutiert weitere Ansätze.

## Grundgedanke

AiNetReview braucht wahrscheinlich keine große Sammlung kleinteiliger Qualitätsanalysen. Wenige überprüfbare Signale können einem Agenten die Stellen zeigen, an denen ein Audit sinnvoll ist. Ein Treffer ist eine Frage an den Code und seinen Kontext, keine Anweisung zur sofortigen Änderung. Der Agent soll die Absicht, Aufrufer, Verträge und Tests prüfen und anschließend begründet ändern oder den Fall akzeptieren. Automatisches Aufteilen einer Methode nur zur Senkung eines Messwerts kann das Verständnis verschlechtern.

## Kognitive Komplexität und tiefe Verschachtelung

Kognitive Komplexität ist als Maß für die Verständlichkeit des Kontrollflusses durch Menschen gedacht. Sie gewichtet unter anderem verschachtelte Entscheidungen stärker als eine einfache Folge von Entscheidungen; sie ist kein validierter Grenzwert für die Fähigkeiten eines LLM. [SonarSource beschreibt die Zielsetzung der Metrik](https://www.sonarsource.com/resources/cognitive-complexity/).

Zwanzig ineinanderliegende `if`-Blöcke wären ein sehr starker Anlass, den Ablauf zu prüfen. Eine allgemeingültige Aussage „LLMs versagen ab Verschachtelungstiefe X“ ist daraus nicht ableitbar. Entscheidend können ebenso viele abhängige Bedingungen, veränderlicher Zustand, Ausnahmen und der nötige Kontext über Methodengrenzen hinweg sein.

Aktuelle Benchmarks zeigen Schwierigkeiten beim Verstehen komplexen Codes, belegen aber keinen isolierten C#-Grenzwert für Verschachtelung. In [LongCodeU](https://aclanthology.org/2025.acl-long.1324/) waren Beziehungen zwischen Codeeinheiten für die untersuchten Modelle besonders schwierig. Die [SWE-Flux-Vorabveröffentlichung](https://arxiv.org/abs/2609.28449) untersucht Laufzeitfragen in Python-Repositories und berichtet größere Probleme bei Datenfluss, Ausführung über Methoden hinweg und präzisem Zustand als bei lokalem Kontrollfluss. Das ist ein Hinweis gegen rein kosmetisches Verteilen verschachtelter Logik auf viele Hilfsmethoden, kein Beweis, dass Verschachtelung harmlos wäre. Ergebnisse hängen von Aufgaben, Sprachen, Modellen und Werkzeugunterstützung ab.

Für einen ersten Audit erscheinen deshalb **separate, erklärte Messwerte** sinnvoll: Anzahl von Entscheidungen, maximale Verschachtelung, kognitive Komplexität und Methodenumfang. Die statistische Relevanzauswahl (wie in [method-control-flow-outliers](../../docs/review/findings.md) umgesetzt) kann darauf angewandt werden. Der Bericht sollte alle relevanten Kandidaten und ihre Rohwerte zeigen, ohne eine feste Höchstzahl und ohne einen automatischen Refactoring-Auftrag.

## Drei mögliche Blickrichtungen

1. **Lokale Nachvollziehbarkeit:** Ist der Kontrollfluss innerhalb einer Methode schwer zu verfolgen? Umfang, Entscheidungen, Verschachtelung und kognitive Komplexität liefern gemeinsam Hinweise. Das ist der einfachste erste Versuch.
2. **Verstreuter Kontext:** Wie viele Methoden, Dateien und Zustandsübergänge muss ein Agent für einen fachlichen Ablauf zusammenführen? Konkrete Beziehungen sind wichtiger als ein abstrakter Graphwert. Diese Analyse könnte für agentische Entwicklung besonders wertvoll sein, ist aber schwieriger zuverlässig umzusetzen.
3. **Änderungsrisiko:** Welche Verträge, Aufrufer, Fehlerpfade und Tests berührt eine Änderung? Ein Audit direkt nach einem Entwicklungstask sollte nach möglichen unbeabsichtigten Auswirkungen fragen. Diese Sicht ist voraussichtlich wertvoll, verlangt aber eine verlässliche Zuordnung von Änderungen und Auswirkungen; sie muss nicht von Git als Laufzeitvoraussetzung abhängen.

Diese drei Blickrichtungen sind keine Forderung nach drei sofortigen Produktanalysen. Zunächst die lokale Auswertung an echten Audits erproben und festhalten, welche Kandidaten zu einer nützlichen Frage, einer begründeten Akzeptanz oder einer sinnvollen Änderung geführt haben. Weitere Signale erst aufnehmen, wenn sie einen zusätzlichen Nutzen zeigen.
