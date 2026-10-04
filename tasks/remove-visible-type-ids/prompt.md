# Umsetzungsprompt: Sichtbare Typ-IDs aus Agentenreports entfernen

Status: geplanter Implementierungsauftrag. Diese Datei beschreibt das gewünschte Ergebnis, nicht bereits implementiertes Verhalten.

## Auftrag

Setze diesen Auftrag vollständig im Repository **AiNetReview** um. Ändere den Reportgenerator, ergänze Regressionstests und aktualisiere die betroffene Dokumentation. Führe die vorgeschriebenen Prüfungen aus und committe ausschließlich die Änderungen dieses Auftrags, sobald alle Abnahmepunkte erfüllt und alle Prüfungen grün sind. Ein Plan oder eine Empfehlung allein erfüllt den Auftrag nicht.

Entferne die vom Reportgenerator ausgegebenen technischen **Typ-IDs** wie `t-8b500c79424a7b06` aus allen neu erzeugten Markdown-Auditreports. Die Reports müssen Typen anhand semantischer Namen und des vorhandenen Namespace-, Projekt- und Quellortkontexts identifizieren. Ein Agent darf keine Hash-ID auflösen müssen, um einen Typ oder eine Abhängigkeit zu verstehen.

## Verbindliche Entscheidungen

Diese Tabelle legt den Umfang abschließend fest. Entferne nicht pauschal alle IDs oder Hashes.

| Element | Entscheidung |
| --- | --- |
| Vom Renderer sichtbarer Typ-ID-Wert aus `ReviewMapType.Id`, beispielsweise `t-8b500c79424a7b06` | Aus dem Markdown entfernen. |
| Semantische Typnamen, Namespaces, Typarten und Deklarationsorte | Vollständig erhalten. |
| Interne Typ-IDs und ihre Verwendung für Zuordnung, Gruppierung und Graphkanten | Unverändert erhalten. |
| Finding-IDs wie `finding-...` und ihre Querverweise | Unverändert erhalten; sie identifizieren zu klassifizierende Findings. |
| Projektkeys wie `p-AiNetReview-Core-...` und darauf beruhende Verzeichnisse/Routen | Unverändert erhalten. |
| Run-IDs, Analyse-IDs, Quellpfade, Messwerte und Konfiguration | Unverändert erhalten. |
| Bereits vorhandene Reports | Nicht bearbeiten und nicht überschreiben; die Änderung gilt für neue Runs. |

Die bisherige Aussage in `docs/review/findings.md`, dass stabile Typ-IDs in Strukturmaps sichtbar bleiben, wird durch diesen Auftrag ausdrücklich abgelöst. Das ist eine beauftragte Änderung des Ausgabeformats. Die Forderung nach stabilen **Finding-IDs** in den Repository-Regeln bleibt gültig. Dafür ist keine weitere Freigabe nötig.

## Ausgangslage und Dateien

Prüfe den aktuellen Code, bevor du editierst; Zeilennummern können sich ändern.

- `src/AiNetReview.Core/Reporting/MapsReportWriter.cs`: Eigentümer der Projektmaps. In `FormatStructure` wird derzeit neben Typname und Typart auch `type.Id` gerendert. Das ist die bekannte verbleibende Ausgabestelle.
- `src/AiNetReview.Core/Analysis/ReviewMaps.cs`: Datenmodell mit `ReviewMapType.Id`, `ReviewMapSourceFile.TypeIds` und den Kantenfeldern `FromTypeId`/`ToTypeId`. Diese Felder bleiben bestehen.
- `src/AiNetReview.Core/Analysis/ReviewMapBuilder.cs`: bereitet Typnamen, interne Identitäten, Deklarationen und Kanten vor. Die ID-Erzeugung und Graphvorbereitung bleiben unverändert.
- `tests/AiNetReview.FastTests/Reporting/MapsReportWriterTests.cs`: fokussierte Tests für Projektmaps.
- `tests/AiNetReview.IntegrationTests/Reporting/ReviewMapsIntegrationTests.cs`: echte Solution-Vorbereitung und Veröffentlichung der Maps.
- `tests/AiNetReview.FastTests/Reporting/MarkdownReportWriterTests.cs`: bestehende Abdeckung des Gesamtberichts, seiner Routen und Finding-IDs. Nur ändern, wenn eine konkrete Erwartung dieses Auftrags es erfordert.
- `docs/review/findings.md`: kanonischer Vertrag für die Reportausgabe; nach erfolgreicher Umsetzung korrigieren.
- `docs/development/build-and-tests.md`: verbindliche Build- und Testanweisungen.

Die Abhängigkeitsmaps verwenden bereits lesbare, bei Bedarf disambiguierte Typnamen. Sie haben ein zentrales Projektrouten-Verzeichnis, ein einmaliges Konsumenten-Deklarationsverzeichnis und kompakte eingehende Konsumentenlisten nach Zieltyp, Rolle und Besitzerprojekt. Dieses Format beibehalten. Baue diese Funktion nicht noch einmal neu.

## Gewünschtes Ausgabeformat

In einer Strukturmap gilt weiterhin der Projektkontext der Datei und der Namespacekontext des jeweiligen Abschnitts. Eine Typzeile enthält den vorhandenen semantischen Typnamen, die Typart und sämtliche Deklarationsorte. Nur die sichtbare Typ-ID und die dadurch überflüssige Interpunktion entfallen.

Vorher:

```markdown
### `AiNetCodeNavigator.Mcp.Tools.Relationships`

- `AssemblyIdentityMatcher` (Class, `t-8b500c79424a7b06`): `src/AiNetCodeNavigator/Mcp/Tools/Relationships/AssemblyIdentityMatcher.cs:7`
```

Nachher:

```markdown
### `AiNetCodeNavigator.Mcp.Tools.Relationships`

- `AssemblyIdentityMatcher` (Class): `src/AiNetCodeNavigator/Mcp/Tools/Relationships/AssemblyIdentityMatcher.cs:7`
```

Für einen partiellen Typ müssen alle vorhandenen Orte auf derselben Typzeile erhalten bleiben, beispielsweise:

```markdown
- `Widget` (Class): `src/Domain/First.cs:2`, `src/Domain/Second.cs:5`
```

Behalte die vorhandene Sortierung und den bestehenden Code-Span-Formatter bei. Verschachtelte Typen und generische Typparameter dürfen nicht gekürzt oder verloren gehen. Gleichnamige Typen bleiben durch ihre Namespaceabschnitte und Projektmaps unterscheidbar. Für Abhängigkeitsmaps bleibt die bereits implementierte Namensdisambiguierung unverändert gültig.

Falls du beim Prüfen eine weitere Ausgabe eines internen Typ-ID-Werts findest: Entferne nur diese sichtbare Ausgabe. Wenn sie bisher die einzige Typbezeichnung war, verwende die vorhandenen semantischen Typinformationen und den erforderlichen Namespace-/Projektkontext. Eine Beziehung, einen Typ oder einen Beleg zur Vermeidung der ID einfach wegzulassen ist nicht zulässig.

## Grenzen der Umsetzung

- Ändere die Erzeugung der Texte, nicht nachträglich den fertigen Markdown-Text. Keine globale Regex-Ersetzung und keine neue Bereinigungsstufe über Reports legen.
- Ändere weder das Map-Datenmodell noch die internen Typ-IDs, Dictionary-Schlüssel, Graphkanten oder Analyseergebnisse. Insbesondere `CreateTypeId` und die Verwendung von IDs für interne Zuordnung bleiben bestehen.
- Erhalte sämtliche Typen, alle Partial-Deklarationen, ausgehende Witnesses, eingehende Konsumenten, Projektrollen, Graphgrenzen und Scope-/Unsicherheitshinweise.
- Erhalte das kompakte Dependency-Format. Keine Hash-Legende, Ersatz-IDs, zusätzlichen JSON-Exporte, HTML-Anker oder Markdown-Links in generierten Reports einführen.
- Ändere keine Analyseschwellen, keine Finding-Auswahl, keine Konfiguration, keine CLI-Ausgaben und keine Publikations-, Fehler- oder Cancellation-Semantik.
- Sanitisiere keine Benutzerinhalte. Falls ein echter Dateipfad zufällig `t-` oder sogar eine hashähnliche Zeichenfolge enthält, muss der Pfad erhalten bleiben. Verboten ist die Ausgabe interner Typ-ID-Felder als Metadaten, nicht eine zufällige Zeichenfolge in einem echten Pfad.
- Installiere keine Pakete und führe keine sonstigen Refactorings aus.
- Arbeite nur in AiNetReview. Der Name AiNetCodeNavigator im Formatbeispiel ist keine Aufforderung, ein anderes Repository zu ändern oder zu auditieren.

## Arbeitsfolge

Führe die Schritte in dieser Reihenfolge aus.

1. Lies `AGENTS.md`, `.agents/rules/README.md` und die dort verlinkten Regeln. Lies die beiden oben genannten Dokumentationsseiten und die betroffenen Renderer-/Teststellen. Für C#-Navigation gilt Regel 09; bekannte Dateien und Zeilenbereiche dürfen direkt gelesen werden. Starte keinen zusätzlichen Schritt aus `.agents/agent-workflow/`.
2. Notiere `git rev-parse HEAD` und `git status --short`. Identifiziere fremde oder bereits vorhandene Änderungen und halte sie aus deinem Commit heraus.
3. Suche gezielt nach sichtbaren Typ-ID-Ausgaben im Reporting-Code. Unterscheide `.Append(type.Id)` für Ausgabetext von einer weiterhin notwendigen Verwendung als interner Schlüssel. Eine Textsuche allein rechtfertigt kein Löschen interner ID-Verwendungen.
4. Ergänze zunächst einen fokussierten FastTest: Schreibe Maps mit kontrollierten Typ-IDs und prüfe, dass die Strukturmaps Namen, Typarten und alle Orte enthalten, aber die tatsächlichen IDs dieser Fixture nicht ausgeben. Der Test muss gegen den bisherigen Renderer wegen der sichtbaren Typ-ID fehlschlagen, nicht wegen eines Kompilierungsfehlers oder einer falschen Formatannahme.
5. Führe den fokussierten Testlauf aus und halte den erwarteten roten Befund fest. Bei unverändertem Klassennamen lautet der Befehl:

   ```powershell
   pwsh -File ./scripts/test-fast.ps1 -Filter 'FullyQualifiedName~MapsReportWriterTests'
   ```

   Wenn die Änderung beim Start bereits vollständig umgesetzt ist, erzeuge keine künstliche Regression. Prüfe stattdessen die Abdeckung, dokumentiere den vorhandenen Zustand und ergänze nur noch fehlende Anforderungen.
6. Entferne die sichtbare Typ-ID im zuständigen Renderer und bereinige die zugehörige Interpunktion. Bewahre alle übrigen Informationen. Passe nur tatsächlich betroffene Erwartungen in bestehenden Tests an.
7. Ergänze den bestehenden IntegrationTest so, dass auch die Produktions- und Test-Strukturmaps keine tatsächlichen `ReviewMapType.Id`-Werte ausgeben. Prüfe dabei ausdrücklich weiterhin Namespace-/Typnamen und alle Partial-Deklarationen. Prüfe die vollständige neue Markdown-Veröffentlichung anhand der kontrollierten Map-Typen ebenfalls auf versehentlich ausgegebene Typ-ID-Werte. Wähle Fixture-Namen und -Pfade, die nicht zufällig selbst einen dieser ID-Werte enthalten.
8. Erhalte oder ergänze gezielte Abdeckung für gleichnamige Typen in unterschiedlichen Namespaces, gleichnamige Typen in unterschiedlichen Projekten, verschachtelte/generische Typen und deterministische Ausgabe. Nutze vorhandene Tests, soweit sie diese Anforderungen bereits verifizieren; dupliziere keine Tests nur für zusätzliche Testzahlen. Die neue Determinismusprüfung muss die geänderte Strukturmap einschließen.
9. Aktualisiere `docs/review/findings.md` auf den nachweislich umgesetzten Vertrag: interne Typ-IDs bleiben technische Schlüssel; die Markdown-Maps zeigen semantische Namen und Quellorte. Entferne die Aussage über sichtbare stabile Typ-IDs in Strukturmaps. Ändere weitere Dokumentation nur, wenn sie nachweislich denselben überholten Vertrag beschreibt.
10. Führe sämtliche unten vorgeschriebenen Abschlussprüfungen aus. Wenn ein Fehler auftritt, behebe die Ursache und wiederhole die betroffenen Prüfungen. Schwäche keine Tests oder Analyzer ab, um grün zu erreichen.
11. Prüfe jeden Abnahmepunkt, den finalen Diff und die vorgesehenen Commit-Dateien. Committe nur bei vollständig grünem Ergebnis. Berichte anschließend Commit-Hash, tatsächlich ausgeführte Prüfungen und gegebenenfalls verbliebene Einschränkungen auf Deutsch.

## Abnahmepunkte

Alle Punkte sind verbindlich. Prüfe sie am Ergebnis, nicht nur durch Lesen dieser Aufgabenbeschreibung.

- [x] **A1 – Sichtbare Typ-IDs entfernt:** Keine neue Markdown-Reportdatei gibt einen internen `ReviewMapType.Id`-Wert als Typbezeichnung, Typmetadatum oder Lookup-Eintrag aus. Die automatisierten Prüfungen vergleichen mit den tatsächlichen IDs einer kontrollierten Fixture; ein pauschales Verbot der Teilzeichenfolge `t-` ist nicht der Vertrag.
- [x] **A2 – Strukturzeilen vollständig:** Das neue Beispiel `(Class):` ist ohne leeres Komma oder leere Klammer umgesetzt. Semantischer Typname, Typart, Namespace- und Projektkontext sowie jeder Deklarationspfad mit korrekter einbasierter Zeile bleiben erhalten, einschließlich aller Partial-Deklarationen.
- [x] **A3 – Eindeutige Namen:** Gleichnamige Typen in unterschiedlichen Namespaces oder Projekten bleiben unterscheidbar. Verschachtelte Typnamen und generische Typparameter bleiben sichtbar. Die bestehende Dependency-Namensdisambiguierung wird nicht verändert.
- [x] **A4 – Identitäten und Daten erhalten:** Die internen Typ-IDs, ihre Erzeugung und alle ID-basierten Zuordnungen bleiben unverändert. Keine Typen, Graphkanten, Witnesses, Konsumenten oder Findings werden entfernt. Finding-IDs, Projektkeys und sämtliche bestehenden Report-Routen bleiben gültig.
- [x] **A5 – Kompaktes Format und Publikation erhalten:** Consumer-Deklarationen werden weiterhin einmal pro Typ angegeben. Das bisherige Dependency-Layout, Markdown-Escaping, UTF-8-/LF-Ausgabe und atomare Publikation samt Fehler-/Cancellation-Verhalten bleiben erhalten. Alte Runs werden nicht verändert.
- [x] **A6 – Regression automatisiert geprüft:** Ein neuer Test hat die bisher sichtbare Typ-ID reproduzierbar nachgewiesen, sofern sie beim Start noch vorhanden war. FastTests und IntegrationTests verifizieren das neue Ausgabeformat sowie die bewahrten Informationen. Die Strukturmap-Ausgabe ist auch bei permutierter Eingabe deterministisch.
- [x] **A7 – Dokumentation korrekt:** Der sichtbare Typ-ID-Vertrag ist in der kanonischen Dokumentation korrigiert. Die Dokumentation behauptet keine Entfernung interner Identitäten und verwechselt Typ-IDs nicht mit Finding-IDs.
- [x] **A8 – Grün und commitbereit:** Alle Abschlussprüfungen sind erfolgreich ausgeführt. Die für den Commit vorgesehenen Dateien enthalten ausschließlich Änderungen dieses Auftrags. Nach dieser Abnahme folgen der verpflichtende Commit und der Abschlussbericht gemäß der folgenden Anleitung.

## Abschlussprüfungen und Commit

Führe im Repositoryroot nacheinander aus. Jeder Befehl muss erfolgreich enden:

```powershell
pwsh -File ./scripts/build.ps1
pwsh -File ./scripts/test-fast.ps1
pwsh -File ./scripts/test-integration.ps1
git diff --check
```

Die Build- und Testskripte schreiben ihre vollständigen Logs unter `temp/`. Diese Dateien helfen bei Fehlerdiagnosen, gehören aber nicht in den Commit. Verwende die normalen Testsuiten; Performance-Tests und gesonderte Auditprofile sind für diese reine Rendereränderung keine zusätzlichen Abnahmegates. Aktiviere kein deaktiviertes Auditprofil.

Für einen zusätzlichen Stichprobenblick darfst du einen von den normalen IntegrationTests neu erzeugten lokalen Report unter `audit-reporting/` verwenden. Identifiziere den neuen Run anhand seines tatsächlichen Ergebnisses. Ein älterer Run mit sichtbaren Typ-IDs ist kein Nachweis für einen Fehler im geänderten Renderer. Erfinde keine Run-IDs und bearbeite keine generierten Dateien von Hand.

Vor dem Commit:

1. Prüfe `git status --short` und `git diff --cached --name-only`. Bei bereits gestagten fremden Änderungen: nicht mitcommitten und nicht eigenmächtig unstagen; melde den Konflikt.
2. Stage nur einzeln benannte, tatsächlich geänderte Auftragsdateien. Verwende niemals `git add .`, `git add -A` oder `git commit -a`.
3. Prüfe `git diff --cached --check` und `git diff --cached --stat`.
4. Committe mit einer englischen Conventional-Commit-Nachricht, beispielsweise `fix(reporting): omit internal type IDs from markdown maps`.
5. Pushe nicht, ändere keinen vorhandenen Commit und erstelle keinen Release oder Tag.

Bei fehlgeschlagenen Prüfungen gilt: weiter an der Ursache arbeiten und nicht als grün berichten. Falls ein externer Blocker die Prüfung tatsächlich verhindert, melde den konkreten Blocker und die ungeprüften Punkte; dann gibt es keinen Implementierungscommit nach dieser Grün-Bedingung.
