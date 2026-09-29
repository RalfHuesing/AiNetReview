---
status: draft
---

# Begriffe und Grundvertrag für Review-Analysen

## Intention

AiNetReview soll weitere fachliche Auswertungen aufnehmen können, ohne dass deren Namen einen Gesetzesverstoß, einen Defekt oder einen automatischen Änderungsauftrag behaupten. Ein konsistenter Wortschatz soll Entwicklern und Review-Agenten zeigen, was konfiguriert wird, was im Bericht erscheint und welche Entscheidung erst nach Prüfung des Kontexts fällt.

## Ausgangspunkt

- Die ausführbaren Einheiten heißen im aktuellen Code `IReviewRule`, `RuleDescriptor` und `RuleRegistry`; die Konfiguration verwendet `rules`, und Berichte liegen unter `rules/`.
- Die drei registrierten Einheiten suchen Kontrollfluss-Ausreißer, mögliche ungenutzte Deklarationen und ähnliche Codekörper. Ihre Treffer sind Review-Kandidaten, keine bewiesenen Verstöße. Siehe `docs/review/findings.md`.
- Ein Bericht trennt bereits die Einheit mit Zweck und Review-Fragen von einzelnen `FindingDraft`-Fundstellen; die Tabelle nennt die Beobachtung an einer Fundstelle `Signal`.
- Die Repository-Anweisungen unter `.agents/rules/` sind tatsächlich verbindliche Arbeitsregeln. Produktterminologie und Agentenanweisungen sollten nicht verwechselt werden.

## Begriffsmodell – Vorschlag zur Entscheidung

| Ebene | Empfohlener Begriff | Bedeutung |
| --- | --- | --- |
| Konfigurierbare, wiederholbare Auswertung | **Review-Analyse** (`review analysis`) | Definiert Auswahl, Messung, Optionen, Grenzen und Review-Fragen; ihr Lauf kann null oder mehrere Fundstellen ergeben. |
| Einzelnes Ergebnis im aktuellen Lauf | **Fundstelle** (`finding`) | Verweist auf überprüfbaren Code und Evidenz; ist noch kein Defekturteil. |
| Beobachtung an der Fundstelle | **Signal** (`signal`) | Beschreibt, weshalb die Stelle zur Prüfung vorgeschlagen wird; ist kein Schweregrad und keine Handlungsanweisung. |
| Entscheidung nach Kontextprüfung | **Review-Entscheidung** | Änderung oder begründete Akzeptanz liegt beim prüfenden Menschen bzw. Agenten mit Nutzerkontext. |

**Begründung:** „Rule“ passt zum technischen Muster einer ausführbaren Auswertung, klingt für Nutzer und Agenten aber nach einer verbindlichen Norm. „Hint“ ist zu unverbindlich und kann wie ein beiläufiger Tipp wirken. „Signal“ beschreibt den beobachteten Anlass gut, verwechselt als Name der konfigurierbaren Einheit jedoch Ursache und Ergebnis. „Check“ kann ein Bestehen oder Scheitern nahelegen. „Review-Analyse“ benennt die Tätigkeit neutral und lässt sowohl statistische Ausreißer als auch künftige Kontextanalysen zu.

## Scope

### Muss

- Einen konsistenten Produktwortschatz für konfigurierbare Auswertung, einzelne Fundstelle, beobachtetes Signal und anschließende Review-Entscheidung festlegen.
- Den Bedeutungsrahmen für weitere Review-Analysen beschreiben: Eine Fundstelle braucht nachvollziehbare Evidenz und eine konkrete Review-Frage; sie ist für sich weder Defektbeweis noch Refactoring-Auftrag oder Build-Fehler.
- Die gewählte Benennung an den berührten Produktflächen konsistent machen. Welche Flächen dazu gehören, bleibt bis zur Entscheidung über die Umbenennung offen.
- Den bestehenden Konfigurations- und Berichtvertrag bei einer möglichen Umbenennung ausdrücklich behandeln, damit vorhandene Projekte und Review-Abläufe nicht unbemerkt brechen.

### Nicht

- Neue fachliche Review-Analysen, Auswahlmetriken oder Schweregrade entwerfen.
- Review-Fundstellen zu Build-Fehlern machen oder Code automatisch ändern.
- Die verbindlichen Agentenanweisungen unter `.agents/rules/` umbenennen.
- In diesem Konzeptschritt Code, Konfiguration oder aktuelle Produktdokumentation ändern.

## Verifikation

- Der definierte Wortschatz lässt sich auf alle drei vorhandenen Produktanalysen und auf einen Lauf ohne Fundstellen widerspruchsfrei anwenden.
- Konfiguration, öffentliche Texte, Berichte, Dokumentation und interne Typnamen sind hinsichtlich gewählter Benennung und Kompatibilität ausdrücklich abgegrenzt.
- Ein neuer Review-Agent kann aus dem Konzept erkennen, welche Aussage eine Fundstelle erlaubt und welche Entscheidung Kontextprüfung verlangt.

## Arbeitsgedächtnis (nur Draft)

- **Empfehlung:** „Review-Analyse“ für die ausführbare Einheit, „Fundstelle/Finding“ für das einzelne Ergebnis und „Signal“ für die Beobachtung. „Rule“ bleibt den verbindlichen Agentenanweisungen vorbehalten.
- **Offene Entscheidung 1:** Ist „Review-Analyse“ der gewünschte Begriff, oder soll die konfigurierbare Einheit weiter „Rule“ heißen? Kosten der Beibehaltung: Der Produktwortschatz trägt weiter eine Norm-Assoziation, die den Berichtsaussagen widerspricht.
- **Offene Entscheidung 2:** Soll die gewählte Produktterminologie nur in sichtbaren Texten und Dokumentation gelten oder auch API-/Code-Namen, JSON-Schlüssel `rules` und Berichtspfad `rules/` erfassen? Ein vollständiger Wechsel braucht einen ausdrücklich festgelegten Kompatibilitätsvertrag; eine reine Textänderung lässt technische Altbegriffe bestehen.
