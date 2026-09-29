---
status: ready
---

# Gemeinsame Code-Messungen und Test-Roots im Core

## Ziel und Einordnung

Dieser Task stellt drei zustandslose Bausteine unter `AiNetReview.Core.Analysis` bereit: die vorhandene Entscheidungs-Messung als wiederverwendbare Funktion, eine tokenbasierte Codezeilen-Messung und die semantische Erkennung aktiver Test-Roots. Die bestehenden und geplanten Review-Analysen verwenden dieselben Messregeln, treffen ihre Auswahl- und Berichtsentscheidungen aber jeweils selbst.

Konkrete Verbraucher sind `method-control-flow-outliers`, [missing-test-evidence-candidates](../analyse-test-abdeckung/Konzept.md) und [code-size-candidates](../analyse-groessen/Konzept.md). Die beiden geplanten Analysen werden hier nicht implementiert. `code-size-candidates` ist noch ein Draft; seine Größen-Grenzen und die Behandlung flacher Mapper gehören in dessen eigenen Task. Ein gemeinsamer `BranchTrivialityDetector` wird deshalb hier nicht gebaut.

Die Namen unten bezeichnen die fachlichen Core-Einstiege. Kleine private Hilfstypen sind erlaubt; keine Analyse darf wegen dieser Extraktion zusätzliche Konfiguration oder ein anderes Ergebnis bekommen. Die Bausteine halten keinen veränderlichen Zustand zwischen Aufrufen und lesen ausschließlich übergebene Roslyn-Knoten, Symbole und Projekte. Sie laden keine Dateien selbst.

## 1. Entscheidungs-Messung: `ControlFlowMetrics`

`ControlFlowMetrics.Measure(SyntaxNode executableBody)` erhält **nur** den ausführbaren Rumpf: `Body` als `BlockSyntax` oder `ExpressionBody.Expression` als `ExpressionSyntax`. Die aufrufende Analyse wählt ausführbare Deklarationen und wendet Projekt-, Dokument- und Symbolausschlüsse an. Deklarationen ohne Rumpf werden nicht gemessen. Die Messung benötigt kein `SemanticModel`.

Das unveränderliche Ergebnis enthält `DecisionCount`, `DecisionConstructCount`, `MaxDecisionNesting` und `DeepestDecision` (`SyntaxNode?`). Für einen Rumpf ohne gezählte Entscheidung gelten `0`, `0`, `0` und `null`. Gezählt wird exakt wie im bisherigen `DecisionVisitor` in [MethodControlFlowOutliersAnalysis](../../src/AiNetReview.Core/ReviewAnalyses/MethodControlFlowOutliers/MethodControlFlowOutliersAnalysis.cs):

| Syntax | `DecisionCount` | `DecisionConstructCount` | Tiefe |
| --- | ---: | ---: | --- |
| `if`, auch jedes `else if` | 1 je `if` | 1 je `if` | `else if` bleibt auf der Tiefe des ersten `if`; Entscheidungen in Bedingung und Zweigen liegen eine Ebene tiefer. |
| `switch`-Statement | 1 je Abschnitt, auch `default`; mehrere Labels desselben Abschnitts zusammen 1 | 1 für das ganze Statement | Jeder Abschnitt bildet eine Ebene. Der Governing-Ausdruck wird mit der bisherigen Tiefe besucht; darin gezählte Entscheidungen erhöhen sie selbst. |
| `switch`-Expression | 1 je Arm, auch `_` | 1 für die ganze Expression | Jeder Arm bildet eine Ebene. Der Governing-Ausdruck wird mit der bisherigen Tiefe besucht; darin gezählte Entscheidungen erhöhen sie selbst. |
| `?:`, `for`, beide `foreach`-Syntaxarten, `while`, `do`, `catch` | 1 je Vorkommen | 1 je Vorkommen | Das Konstrukt und Entscheidungen in seinen Kindern liegen auf aufeinanderfolgenden Ebenen. |

Die erste gezählte Entscheidung hat Tiefe 1. Bei gleicher maximaler Tiefe bleibt der zuerst im bisherigen Walker besuchte Knoten `DeepestDecision`; die aufrufende Analyse wählt daraus wie bisher das Evidenz-Token. Lokale Funktionen, Lambdas, anonyme Methoden und darin enthaltene Entscheidungen werden beim Messen des umgebenden Rumpfs vollständig übersprungen. Sie werden hier auch nicht separat gemessen. `&&`, `||`, `??`, `??=`, Pattern-Kombinatoren und Exception-Filter sind keine zusätzlichen Entscheidungen. Dies ist die bestehende **Entscheidungsmetrik**, keine McCabe- oder allgemeine kognitive Komplexitätszahl.

`Measure` akzeptiert ausschließlich `BlockSyntax` oder `ExpressionSyntax` und wirft für andere Knotentypen `ArgumentException`; `null` ist bei allen drei Bausteinen ein Argumentfehler. Damit muss kein Aufrufer erraten, ob eine übergebene vollständige Deklaration inklusive lokaler Funktionen mitgemessen würde.

`MethodControlFlowOutliersAnalysis` nutzt den neuen Einstieg für dieselben bisherigen Methodendeklarationen. Die Kandidatenmenge, Perzentile, Mindestwerte, `DecisionConstructCount >= 2`-Bedingung, Evidenz, Reihenfolge, Descriptor-Texte und Behavior-Version bleiben unverändert. Konstruktoren, Accessors, Operatoren und Conversions werden durch diesen Task **nicht** zu Kandidaten dieser bestehenden Analyse. Der neue Einstieg kann deren Rümpfe später für die Testanalyse messen.

## 2. Codezeilen-Messung: `CodeLineMetrics`

Ein Codezeilenwert zählt unterschiedliche physische **Startzeilen** von vorhandenen C#-Tokens im geladenen Syntaxbaum. Die Implementierung verwendet `DescendantTokens(descendIntoTrivia: false)` und die nullbasierte Startzeile des Token-Spans. Fehlende/Nullbreiten-Tokens und `EndOfFileToken` zählen nicht. Signaturen, Attribute, Klammern, Pfeile und Semikolons zählen, sofern ihre Zeile einen solchen Token enthält. Mehrere Tokens auf derselben Zeile zählen einmal. Leerzeilen, Kommentare, XML-Dokumentation und Direktiven bestehen aus Trivia und zählen nicht. Ein mehrzeiliges Literal zählt nur auf der Startzeile seines Tokens; Dateigröße einschließlich Literalinhalt ist ein separates Maß der Größenanalyse.

Die Core-API bietet diese drei klar getrennten Operationen:

1. `CountTokenStartLines(SyntaxNode node)` zählt die Tokens eines beliebigen Knotens nach der obigen Regel. Für einen Block zählen seine Klammern mit. Diese primitive Operation hat **keine** Sonderregel für rumpflose Deklarationen.
2. `CountExecutableDeclaration(SyntaxNode declaration)` gibt 0 zurück, wenn die übergebene Methoden-, Konstruktor-, Accessor-, Operator-, Conversion-, Property- oder Indexer-Deklaration keinen Body und keinen Expression-Body hat. Bei vorhandener Implementierung zählt sie die Tokens der **ganzen Deklaration**, also auch Signatur und Attribute. Bei Property/Indexer mit Accessor-Liste werden die einzelnen Accessors gemessen; die Property/der Indexer selbst wird nur bei eigenem Expression-Body als ausführbare Einheit gemessen. Andere Knotenarten sind für diesen Einstieg ungültig und lösen `ArgumentException` aus. Die deklarierende Hälfte einer partiellen Methode ohne Body ergibt 0; die implementierende Hälfte wird einmal gemessen.
3. `CountOwnTypePart(BaseTypeDeclarationSyntax declaration)` zählt die Tokens eines Typdeklarationsteils, steigt aber **nicht** in geschachtelte Typ- oder Delegatdeklarationen ab. Der äußere Typ behält seine eigenen Signatur-, Klammer- und Member-Zeilen. Ein geschachtelter Typ kann separat gemessen werden. Liegen Tokens beider Typen auf derselben physischen Zeile, darf diese Zeile in beiden eigenständigen Messungen vorkommen.

Diese Metrik zählt lokale Funktionen und Lambdas als Tokens ihres umgebenden Members mit. Das ist bewusst anders als die Entscheidungs-Messung und entspricht dem vorläufigen Größenkonzept. Sie aggregiert `partial`-Typen **nicht** selbst: Die Größenanalyse ordnet Typteile semantisch demselben Symbol zu und addiert ihre Teilwerte; sie bestimmt auch, welche Typarten Kandidaten sind. Physische Dateizeilen und UTF-8-Bytes werden aus dem geladenen `SourceText` in der Größenanalyse gemessen, nicht aus `CodeLineMetrics`.

## 3. Test-Root-Erkennung: `TestFrameworkClassifier`

`TestFrameworkClassifier.IsActiveTestRoot(Project project, IMethodSymbol method)` entscheidet nur, ob ein **bereits ausgewähltes ausführbares Quellmethoden-Symbol** ein Root für einen gewöhnlichen, ungefilterten Testlauf sein kann. Er verwendet für die Projektgrenze ausschließlich `ReviewSourceClassifier.IsTestProject(project)`. Die spätere Testanalyse iteriert `.cs`-Quellmethoden mit Body bzw. Expression-Body, verwendet die bestehende Prüfung für generierte Dokumente und Symbole und übergibt deren deklarierte Symbole. Das Ergebnis ist ein `bool`; weder Call-Graph noch Testfall-Daten werden hier ausgewertet. Ein parametrisierter Test liefert genau ein Root-Symbol.

Attribute werden über Roslyn-Symbole, Basistypen und gegebenenfalls implementierte Interfaces erkannt, nicht über Schreibweisen im Quelltext. Verglichen werden die Framework-Typen aus referenzierten Metadaten mit ihren vollständigen Metadatennamen; gleichnamige, im Zielprojekt selbst deklarierte Attribute sind keine Framework-Attribute. Core erhält dafür **keine** direkte Paket- oder Projektabhängigkeit zu den Testframeworks.

| Framework | Aktives Test-Root, sofern nicht statisch ausgeschlossen |
| --- | --- |
| xUnit v2/v3 | Methodenattribut, das von `Xunit.FactAttribute` oder `Xunit.TheoryAttribute` erbt, oder ein Methodenattribut, das das xUnit-v3-Interface `Xunit.v3.IFactAttribute` implementiert. |
| NUnit | Methodenattribut vom Typ `NUnit.Framework.TestAttribute`, `NUnit.Framework.TestCaseAttribute` oder `NUnit.Framework.TestCaseSourceAttribute`, jeweils einschließlich abgeleiteter Attribute. |
| MSTest | Methodenattribut vom Typ `Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute` oder `DataTestMethodAttribute`, jeweils einschließlich abgeleiteter Attribute, und `TestClassAttribute` oder ein davon abgeleitetes Attribut auf dem enthaltenden Typ. |

Die Framework-Regeln folgen den jeweiligen [xUnit-v3-Attributverträgen](https://api.xunit.net/v3/3.2.2/Xunit.v3.IFactAttribute.html), [NUnit-TestCaseSource-Regeln](https://docs.nunit.org/articles/nunit/writing-tests/attributes/testcasesource.html), [NUnit-Fixture-Metadaten](https://docs.nunit.org/api/NUnit.Framework.TestFixtureAttribute.html) und [MSTest-Testklassenregeln](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests). Diese Links sind Implementierungsreferenzen; der oben definierte statische AiNetReview-Vertrag bleibt maßgeblich.

Ein Root wird nur bei **statisch erkennbarem Ausschluss der ganzen Methode oder Fixture** verworfen:

- xUnit: `Skip` ist am Testattribut als konstanter, nicht-null String gesetzt oder `Explicit = true`. Wenn `SkipWhen` oder `SkipUnless` gesetzt ist, ist der Skip dynamisch und das Root bleibt erhalten. Werte, die ein benutzerdefiniertes Attribut erst zur Laufzeit berechnet, werden nicht ausgeführt; das Root bleibt erhalten.
- NUnit: `IgnoreAttribute` oder `ExplicitAttribute` auf Methode oder enthaltendem Typ, einschließlich abgeleiteter Attribute; außerdem `Ignore` als nicht-leerer String oder `Explicit = true` am `TestFixtureAttribute` des enthaltenden Typs. `Ignore`/`Explicit` an einzelnen `TestCase`-Attributen werden nicht als Ausschluss des gesamten Roots behandelt. Ein `TestCaseSource` wird nicht ausgeführt und seine Datenzeilen werden nicht ausgewertet.
- MSTest: `IgnoreAttribute` auf Methode oder enthaltendem Typ, einschließlich abgeleiteter Attribute. Datenzeilen und bedingte Laufzeitregeln werden nicht ausgewertet.

Ein `Ignore`-/`Explicit`-Ausschluss auf Methoden- oder Fixture-Ebene hat Vorrang vor allen Testattributen. Andernfalls ist ein Root aktiv, wenn mindestens eines seiner erkannten Framework-Testattribute nicht selbst statisch ausgeschlossen ist; einzelne NUnit-`TestCase`-Ausschlüsse verwerfen das gemeinsame Root nicht. Ohne bekanntes Framework-Attribut oder außerhalb eines erkannten Testprojekts ist das Ergebnis `false`. Methodennamen, Klassennamen, Kommentare, `typeof`, `nameof`, bloße Testprojekt-Zugehörigkeit und eine Framework-Referenz allein begründen kein Root. Ungewöhnlich benannte, vom gemeinsamen Projektklassifikator nicht erkannte Testprojekte sowie dynamische Discovery bleiben Grenzen der späteren Review-Analyse; der Classifier ändert dafür nicht die bestehende Projektklassifikation.

## Verhältnis zu AiNetLinter

AiNetLinter ist eine **read-only technische Referenz**, keine Abhängigkeit und keine Quelle für Produktregeln:

| Referenz | Nutzbarer Mechanismus | Hier bewusst nicht übernommen |
| --- | --- | --- |
| `Metrics/MethodLineCounter.cs` | Token-Startzeilen ohne Trivia und 0 für Methoden ohne Implementierung | Beschränkung auf `MethodDeclarationSyntax`, feste Größenlimits und Linter-Diagnosen. |
| `Metrics/SwitchDispatcherDetector.cs` | Beispiele für syntaktische Dispatcher-Formen für den späteren Größen-Task | Pauschales Absenken einer Komplexitätszahl auf 1, dessen Schwellwerte und eine frühe Core-Trivialitäts-API. |
| `Core/TestDetector.cs` | Testprojekt-Konventionen als Vergleichsmaterial | Namensbasierte Testmethoden-Erkennung, Dateinamen-Heuristiken für Roots, `StaticTestSentinel` und Kategorie-Heuristiken. |
| Bestehender `DecisionVisitor` in AiNetReview | Unveränderte Zähl- und Tiefenregeln | Neue Entscheidungsarten oder eine Änderung des bestehenden Review-Signals. |

Implementierung und Tests werden für AiNetReview geschrieben. Aus AiNetLinter können kleine Roslyn-Muster adaptiert werden; ganze Klassen, Linter-Semantik und Konfiguration werden nicht übernommen.

## Scope-Grenzen

- Keine Implementierung oder Registrierung von `code-size-candidates` oder `missing-test-evidence-candidates`, kein Call-Graph und keine Änderung des Report-Formats.
- Kein `BranchTrivialityDetector` in diesem Task. Ob und wie flache Mapper die **Auswahl** der Größenanalyse beeinflussen, wird mit deren noch offenen Auswahlregeln entschieden. Die rohen `ControlFlowMetrics` bleiben in jedem Fall unverändert.
- Keine Schwellenwerte, Review-Findings, Linter-Diagnosen, Exit-Codes, Quality-Gates oder automatischen Codeänderungen aus diesen Core-Bausteinen.
- Keine neuen Framework-Pakete im Core, keine AiNetLinter-Abhängigkeit, keine globale veränderliche Mess-Cache-Struktur.

## Verifikation und Abnahme

1. **Kontrollfluss:** FastTests prüfen jede Tabellenzeile, gruppierte Switch-Labels, leere Switches, `else if`, innere Entscheidungen in Bedingungen/Arms, Gleichstand bei tiefster Entscheidung, Expression-Bodies und das vollständige Überspringen lokaler Funktionen/Lambdas. Bestehende `MethodControlFlowOutliersAnalysisTests` vergleichen Kandidaten, Metriken und Evidenz vor/nach Extraktion anhand repräsentativer Fälle; Descriptor und Behavior-Version bleiben gleich.
2. **Codezeilen:** FastTests prüfen Einzeiler, mehrzeilige Signaturen, getrennte Klammerzeilen, Kommentar-/Leerzeilen-Invarianz, Direktiven, mehrzeilige Literale, leere und rumpflose Deklarationen, Expression-Bodies, geschachtelte Typen auf eigenen und gemeinsamen Zeilen sowie `partial`-Deklaration versus Implementierung. Der Test für Typteile stellt sicher, dass innere Typ-Tokens nicht zur äußeren Teilmessung gehören.
3. **Test-Roots:** FastTests verwenden echte Framework-Referenzen oder isoliert als Metadaten referenzierte Test-Fixtures statt bloßer Attributnamen im selben Quellprojekt. Sie prüfen xUnit v2/v3 einschließlich abgeleiteter Attribute und `IFactAttribute`, NUnit `Test`/`TestCase`/`TestCaseSource`, MSTest mit und ohne `TestClass`, statische Skip-/Ignore-/Explicit-Fälle, dynamische oder fallbezogene Skips, mehrere Testattribute, gleichnamige fremde Attribute, normale Test-Helpers und Produktionsprojekte. Die vorhandenen Filter für generierte Quellen werden in der späteren Analyse verwendet, nicht im Classifier dupliziert.
4. **Projekt-Gates:** Betroffene FastTests und IntegrationTests, `dotnet test AiNetReview.slnx`, `dotnet build AiNetReview.slnx` ohne Warnungen und `git diff --check` laufen. Für die Extraktion werden keine bestehenden relevanten Tests abgeschwächt. Änderungen an implementiertem Verhalten oder öffentlich beschriebenen Verträgen würden die betroffenen `docs/`-Seiten im selben Commit erfordern; bei der hier geforderten verhaltensgleichen Extraktion wird der Dokumentationsabgleich festgehalten.

Abgenommen ist der Task, wenn die drei Bausteine isoliert getestet sind, `MethodControlFlowOutliersAnalysis` sie ohne beobachtbare Änderung nutzt und die beiden Folgetasks ihre Messungen beziehungsweise Test-Roots ohne erneute Framework- oder Entscheidungslogik verwenden können.
