# Skeptische Prüfung des Structural-Duplication-Performance-Befunds

## Ergebnis

Der Performance-Befund ist material und sollte als **P2, hohe technische Sicherheit / mittlere Impact-Sicherheit** geführt werden. Die Messdateien zeigen, dass zwei syntaktisch identische Methoden mit nur 11.922 Quellbytes und 400 einfachen `value += i;`-Anweisungen insgesamt 37.257.520.200 verwaltete Bytes während der Analyse allokieren und 32,07 Sekunden laufen. Das Ergebnis schrumpft nach Containment-Unterdrückung auf genau ein Finding. Für 200 Anweisungen (5.922 Bytes) waren es 4,83 GB und 4,42 Sekunden; für 100 (2.922 Bytes) 631 MB und 0,84 Sekunden. Die verdoppelten Eingaben verursachen etwa 7,6–7,7-fache kumulative Allokationen. Das passt zu kubischer Gesamtarbeit über alle Kandidatenfragmente.

Die Messung ist kumulative Allokation (`GC.GetTotalAllocatedBytes`), nicht maximaler Heap/RAM-Verbrauch. Die 37,3 GB dürfen daher nicht als 37,3 GB gleichzeitiger Speichernutzung beschrieben werden. Die Dauer und das Allokationsvolumen belegen dennoch erheblichen CPU-/GC-Druck auf einer einzelnen relativ kleinen Methode. Eine endgültige Produktionsauswirkung hängt vom Vorkommen solcher langer doppelter Methoden im analysierten Repository ab; dies wurde hier nicht gemessen.

## Stärkste gutartige Erklärung und Grenze

Der Scanner muss seinen Vertrag erhalten: exakte normalisierte Syntax-Gleichheit, Aussagen- und Token-Mindestwerte, Unterscheidung gebundener Member sowie korrekte Vorkommensmengen und Quellorte. Der hash-basierte Fingerprint darf nicht allein zur exakten Gleichheit werden. Ferner darf eine kleinere Gruppe nur unterdrückt werden, wenn alle ihre Vorkommen in einer größeren Gruppe liegen. Ein einfacher Vorab-Cutoff könnte legitime disjunkte, überlappende oder nur teilweise enthaltene Gruppen verwerfen und wäre daher keine akzeptable Abhilfe.

Diese Anforderungen erklären, weshalb der Scanner erst Kandidaten erzeugt und danach gruppiert. Sie erklären nicht die wiederholte vollständige Traversierung jedes Fragments. Das gemessene Beispiel enthält zwei identische Methoden mit `n` nebeneinanderstehenden einfachen Statements. Jeder passende zusammenhängende Teilabschnitt existiert in beiden Methoden; beim finalen `BuildGroups` bleibt trotzdem nur der maximale Block übrig. Die hohen Kosten entstehen somit aus einer klar begrenzten, vorhersehbaren Eingabeklasse und nicht aus Milliarden Statement-Eingaben oder einem Hash-Kollisionsangriff.

## Evidenz und Ursache

- `tasks/audit-20261003-2245/performance/Program.cs` baut mittels Roslyn eine Adhoc-Kompilierung mit zwei Methoden; jede Methode enthält `n` einfache `value += i;`-Statements plus `return value;`. Kompilierung und Analyse sind getrennt; der Zeit- und Allokationszähler umschließt `StructuralDuplicationCandidatesAnalysis.ExecuteAsync`.
- Gemessene Ausgaben: `probe-100.log`, `probe-200.log`, `probe-400.log`. Alle drei aktuellen Dateien melden `findings: 1` und `status: completed`. Insbesondere meldet die vorhandene 400er-Datei **32,0658436 Sekunden** und **37.257.520.200** kumulative allokierte Bytes. Dies unterscheidet sich von der früheren Statusmeldung „läuft, nach 45s Cancellation“; die finale Artefaktdatei zeigt, dass dieser Lauf abgeschlossen wurde. Ich habe keine neue Probe gestartet.
- In `src/AiNetReview.Core/Analysis/StructuralDuplicateDetector.cs:190-230` wird pro Startposition ein inkrementeller Hash über alle zusammenhängenden Fragmente erzeugt. Nach dem owner-übergreifenden Hash-Treffer macht `CollectFromList` dann für jedes Fragment erneut eine volle Normalisierung: `:233-286`, insbesondere `:277` ruft `NormalizeFragment` mit Start und Fragmentlänge auf.
- `NormalizeFragment` (`:472-495`) allokiert einen neuen `StringBuilder` und durchläuft jede Syntax jedes Kandidaten neu. Bei `n` möglichen Start-/Endintervallen und mittlerer Fragmentlänge proportional zu `n` summiert sich diese erneute Traversierung zu ungefähr O(n³); die Messverdopplungen stützen diesen Verlauf.
- Erst nachdem Occurrences einschließlich normalisierter Strings gesammelt und nach exakter String-Gleichheit gruppiert wurden (`:310-325`), findet die Containment-Prüfung statt (`:327-349`). So wird der gemessene einzelne maximale Ergebnisblock erst nach dem Aufwand für alle enthaltenen Fragmente erhalten.
- Die Analysebeschreibung bestätigt, dass keine konfigurierbaren Schwellen existieren und die festen semantischen Regeln sowie Containment erhalten werden müssen (`src/AiNetReview.Core/ReviewAnalyses/StructuralDuplicationCandidates/StructuralDuplicationCandidatesAnalysis.cs:12-18`).

## Einordnung und Korrekturrichtung

**Impact:** Bei einer Quellmethode von ungefähr 6 KB dauert ein einzelner Durchlauf im Probe bereits 4,4 Sekunden und allokiert kumulativ 4,8 GB; bei ungefähr 12 KB sind es 32 Sekunden und 37,3 GB. Mehrere solcher Owner können Review-Laufzeiten und GC-Arbeit spürbar steigern. Die Verarbeitung bleibt abbrechbar, und die 400er-Probe lief im vorhandenen Artefakt bis zum Abschluss; dies begrenzt den aktuellen Schweregrad gegenüber einem Hänger oder einer unbeschränkten Prozessspeicherbelegung.

**Ursache:** `NormalizeFragment` wird bei jedem hash-passenden Intervall als kompletter Syntaxbaum-Teilbaum neu traversiert. Die spätere Containment-Suppression hat keinen Einfluss auf diese Arbeit.

**Feature-erhaltende Absicht:** Kandidaten-Normalisierung innerhalb eines Startpunkts inkrementell fortsetzen oder exakte Normalisierungsarbeit teilen, damit Syntax und SemanticModel-Bindings nicht für jedes längere Suffix erneut abgefragt werden. Containment darf Kandidatenmaterialisierung nur dann überspringen, wenn exakt bewiesen ist, dass dieselbe vollständige Menge von Occurrences später unterdrückt würde. Den exakten Ordinal-String-Vergleich, sämtliche überlebenden Vorkommensorte und die bestehenden festen Mindestwerte erhalten. Keine neue Größenobergrenze und keine Teilprüfung einführen.

**Akzeptanz für spätere Änderung:** Die vorhandenen Probe-Fälle sowie Fälle mit getrennten maximalen Gruppen, teilweiser Containment, unterschiedlichen Literalen, lokalen/Parameter-Renamings, Binding-Barrieren und gleichen Owners müssen semantisch dieselben Findings, Occurrences und Evidence erzeugen. Die strukturelle Laufzeit/Allokationskurve soll bei wachsenden identischen Methoden deutlich unter dem hier beobachteten kubischen Wachstum liegen; es ist kein willkürliches Performance-Limit aus diesen drei Punkten abzuleiten.

## Verbleibende Unsicherheit

Die Probe ist eine gezielte synthetische Form und misst keine Repository-Häufigkeit oder echte Laufzeitverteilung. Sie ist aber nicht extrem groß: 400 einfache Zeilen in jeder von zwei Methoden, bei unter 12 KB Quelltext insgesamt, genügen für mehr als eine halbe Minute. Der Release-/Performance-Gate-Wert aus `docs/development/build-and-tests.md` ist ein separater 180.000-Zeilen-Test mit 10-Minuten-/6-GiB-Akzeptanz; dieser Befund stammt aus dem Structural-Duplication-Analysepfad und darf nicht als Überschreitung dieses Gates bezeichnet werden.
