# Epic 4 – Aufbau, Tests und Abnahme

Dieses Epic legt Repository-Struktur, Abhängigkeitsrichtung und prüfbare Abnahme für das [Konzept](../AiNetReview-Konzept.md) fest. [Epic 1](01-Eingaben-und-Host.md) definiert Eingaben und CLI, [Epic 2](02-Regel-und-Findings.md) die aktuellen Regelresultate und [Epic 3](03-Berichte.md) die Markdown-Berichte.

## Projekt- und Dateistruktur

Das Repository enthält weiterhin diese fünf Projekte; weitere Projekte brauchen einen konkreten Grund:

```text
AiNetReview.slnx
global.json
Directory.Build.props
Directory.Packages.props
README.md
docs/
scripts/
  build.ps1
  test-fast.ps1
  test-integration.ps1
src/
  AiNetReview.Core/
    AiNetReview.Core.csproj
    Configuration/      ReviewConfig, Validator
    Analysis/           SolutionLoader, ReviewRunner, ReviewContext
    Rules/              IReviewRule, RuleDescriptor, RuleRegistry
      TemplateNoOp/     TemplateNoOpRule
    Findings/           FindingDraft, Evidence, Validierung aktueller Findings
    Reporting/          MarkdownReportWriter
  AiNetReview/
    AiNetReview.csproj
    Program.cs
    Bootstrap/          ServiceRegistration
    Cli/                ReviewCommand
tests/
  AiNetReview.TestKit/
    AiNetReview.TestKit.csproj
    TestTempDirectory, SolutionRootLocator, IsolatedFixtureLease, TestWaiter
  AiNetReview.FastTests/
    AiNetReview.FastTests.csproj
    Configuration/ Analysis/ Rules/ Findings/ Reporting/
  AiNetReview.IntegrationTests/
    AiNetReview.IntegrationTests.csproj
    FixtureRules/       FixtureFindingRule
    Fixtures/           kleine generierte C#-Solutions
    Cli/ Reporting/ Performance/
```

Die Struktur beschreibt den **Zielzustand**, nicht den bereits implementierten Stand. C#-Namespaces spiegeln ihre Pfade. Pro Datei steht normalerweise ein Haupttyp. Die Fixture-Regel bleibt ausschließlich im Testprojekt. `AiNetReview.Core` referenziert weder den ausführbaren Host noch CLI. Der Host referenziert Core und ist die einzige Composition Root. TestKit enthält eigenständige Testhilfen ohne Core-Abhängigkeit; FastTests referenzieren Core und TestKit; IntegrationTests referenzieren Core, Host und TestKit.

`global.json` pinnt das .NET-10-SDK; `Directory.Build.props` aktiviert Nullable und Warnungen als Fehler. Testdateien und generierte Fixture-Solutions liegen in isolierten `TestTempDirectory`-Unterverzeichnissen unter `temp/`; Tests räumen sie nach dem Lauf auf. Build und Tests laufen über `scripts/build.ps1`, `scripts/test-fast.ps1` und `scripts/test-integration.ps1`. CI baut die Solution und führt FastTests sowie kleine IntegrationTests aus.

## Pakete und Wiederverwendung

Allgemeine Infrastruktur kommt aus etablierten, aktiv gepflegten NuGet-Paketen, sofern sie den benötigten Vertrag erfüllen. Der Host nutzt `System.CommandLine` für CLI-Parsing sowie `Serilog`, `Serilog.Sinks.File` und `Serilog.Extensions.Logging` für EXE-relative Dateilogs. Es gibt kein MCP-Paket, keinen selbstgebauten Protokollparser und keinen Review-Store. Die fachlichen Review-Verträge bleiben Projektcode.

Bei Aufnahme oder Aktualisierung eines Pakets wird die neueste stabile, mit .NET 10 und den benötigten Verträgen kompatible Version gewählt und exakt in `Directory.Packages.props` fixiert. API-Eignung, Wartungsstand, Lizenz und bekannte Sicherheitsprobleme werden vor Übernahme geprüft; relevante Build- und Testsuiten laufen danach. Eine ältere Version oder Eigenimplementierung braucht einen konkreten, im zuständigen Task dokumentierten Grund.

Build-Analyzer erzwingen nur eindeutige technische Korrektheit. Metriken für Dateilänge, Komplexität, Kopplung, Architektur sowie kontextabhängige Performance- und Designhinweise sind keine Build-Fehler. Solche Fragen gehören später als prüfbare Review-Hinweise mit menschlichem Urteil in AiNetReview. Die aktuelle Auswahl technischer Diagnosen steht in `.editorconfig`.

## DI und Regel-Erweiterung

Der Host verwendet `Microsoft.Extensions.DependencyInjection` für Solution-Lader, Runner, Registry und Berichtsgenerator. Er konfiguriert Serilog vor dem Aufbau des Providers, bindet es über `Microsoft.Extensions.Logging` ein und leert den Logger beim regulären Prozessende. Core-Dienste erhalten nur `ILogger<T>` aus `Microsoft.Extensions.Logging.Abstractions`; Core referenziert Serilog nicht. Die validierte Konfiguration wird pro Aufruf aus der JSON erzeugt und dem Runner als unveränderlicher Wert übergeben.

Die Composition Root registriert genau eine produktive Regel explizit als `IReviewRule`: `TemplateNoOpRule`. `RuleRegistry` erhält `IEnumerable<IReviewRule>` und weist doppelte IDs sowie ungültige Deskriptoren beim Start zurück. Aktivierte Regeln werden aus dieser Registry anhand der JSON gewählt und nach ID sortiert. Es gibt weder Assembly-Scanning noch dynamisches Nachladen.

`ServiceRegistration` trennt gemeinsame Dienste von der einzelnen produktiven Regelregistrierung. `Program` kombiniert beides und startet die CLI. Der CLI-Adapter akzeptiert einen bereits gebauten Provider und seine Ein-/Ausgabeströme; IntegrationTests bauen damit denselben Host-Kern mit `FixtureFindingRule`. Es gibt keine Testabzweigung in `Program` und keinen produktiven Schalter zum Laden von Testregeln.

Konkrete Regeln und Dienste sind standardmäßig `sealed`. `IReviewRule` und kleine Dienst-Interfaces bilden Austauschpunkte; keine abstrakte Regel-Basisklasse und keine Vererbungshierarchie. Wiederverwendete Roslyn-Logik wird komponiert. `ReviewContext` wird pro Lauf vom Runner erzeugt und ist kein globaler veränderlicher DI-Dienst. Eine neue fachliche Regel benötigt einen Ordner unter `src/AiNetReview.Core/Rules/<Regelname>/`, gezielte Tests und genau eine zusätzliche Registrierungszeile. Ihr Deskriptor, ihre Optionen, ihre Beschreibung und ihre Findings stehen bei der Regel; Runner, CLI und Markdownwriter enthalten keine Regel-Sonderfälle.

## AiNetLinter als Referenz

Der getestete AiNetLinter-Code liegt ausschließlich im separaten Repository `C:/Daten/Entwicklung/Ralf/AiNetLinter/`. Die Testinfrastruktur mit isolierten Temp-Verzeichnissen ist bereits in AiNetReview übernommen und angepasst. Vor der Umsetzung eines vergleichbaren Bausteins sucht der Agent dort Implementierung **und Tests**, benennt die übertragbare Invariante und setzt sie nach dem zuständigen AiNetReview-Vertrag um. Er übernimmt weder Architektur noch Produktsemantik pauschal und ergänzt gezielte AiNetReview-Tests. Wenn kein passender Baustein existiert, ist eine Eigenimplementierung nach Vertrag zulässig.

Selektiv zu prüfen:

- `src/AiNetLinter/Baseline/SourceFileCatalogLoader.cs`, `src/AiNetLinter.FastTests/Baseline/SourceFileCatalogRegistrationPolicyTests.cs` und `src/AiNetLinter.IntegrationTests/Baseline/SourceFileCatalogRegistrationStressTests.cs`: einmalige MSBuild-Registrierung, paralleles Laden und Workspace-Ownership. Konsolenausgaben und alte Laderannahmen nicht übernehmen.
- `src/AiNetLinter/Core/RuleRegistry.cs` und `src/AiNetLinter.FastTests/Core/RuleRegistryTests.cs`: Ideen für ID-Validierung und Registry-Tests. Die dortige statische Regelmetadatenliste passt nicht zur DI-Registry und dem `IReviewRule`-Vertrag dieses Projekts.

AiNetLinter bleibt unverändert. Seine fachlichen Regeln, Metrikgrenzen, Build-Gates, eigener Logger, CLI-Protokollcode und Daemon-Architektur werden nicht übertragen.

## Bereinigung des begonnenen Arbeitsstands

Der **erste Umsetzungsschritt** ist eine eigenständig verifizierte Bereinigung des tatsächlich vorhandenen Codes. Er entfernt vollständig:

- `src/AiNetReview.Core/Storage/`, `src/AiNetReview/Mcp/`, `src/AiNetReview.Core/Catalog/` und `src/AiNetReview/Cli/CatalogCommand.cs`;
- `FingerprintService`, `FindingObservationFactory`, `FindingIdentity` und `StateMachine` einschließlich ihrer ausschließlich darauf bezogenen Tests;
- Storage-/MCP-/Katalog-Registrierungen, Zustandsschnittstellen und -felder aus Runner, Konfiguration, DI und Host sowie die darauf bezogenen Testpfade;
- ausschließlich hierfür benötigte NuGet-Pakete, insbesondere `ModelContextProtocol`, und überholte Aussagen in `docs/`, Repository-Regeln, Beispielen und `.gitignore`.

Angefangene, auch uncommittete Storage-Arbeit wird nicht fertiggestellt. `FindingDraft`, Solution-Lader, Regelregistry, Runner und Logging werden nur insoweit behalten und angepasst, wie sie die neuen Verträge erfüllen. Vor einer neuen Produktfunktion sind die Bereinigung, der Build und die betroffenen Fast- und IntegrationTests abgeschlossen. Kein Produktpfad referenziert danach die entfernten Komponenten oder ein Storage-Verzeichnis. Diese Bereinigung erfolgt durch neue überprüfbare Änderungen; ein pauschales Zurücksetzen gemischter Git-Commits ist nicht Teil des Vertrags.

## Testebenen

`AiNetReview.FastTests` prüft strenge Konfigurationsvalidierung, Registry-Duplikate, sichere Regel-IDs, Validierung und Sortierung aktueller Findings, Options-Defaults und deterministische Markdown-Struktur samt Escaping und Links. Zwei Findings derselben künstlichen Regel bleiben unabhängig. Leere vollständige Regelresultate und ungültige oder doppelte Entwürfe sind abgedeckt. Tests zu Fingerprints, Storage-Schemata, Verdicts und Zustandsübergängen entfallen.

`AiNetReview.IntegrationTests` ruft den CLI-Adapter mit injizierter `FixtureFindingRule` auf und prüft aktuelle Findings, leere Läufe, Berichte, Fehlerpfade, wiederholte Aufrufe ohne Vorzustand und das unveränderte Bestehen älterer Zeitstempel-Berichte. Ein kontrollierter Test ändert eine Quelldatei nach dem Laden, aber vor der Ausführung der Fixture-Regel, und belegt, dass Regel und Bericht weiterhin nur den geladenen Roslyn-Stand verwenden. Zusätzlich startet es die unveränderte produktive EXE als Prozess: `review` mit `template-noop` muss einen vollständigen Null-Finding-Bericht erzeugen. Tests prüfen das EXE-relative Logverzeichnis bei abweichendem Arbeitsverzeichnis, parallele Logschreiber, fehlende Schreibrechte, Rotation, stdout/stderr und Exit-Codes. Weitere Fälle sind ungültige Solution, fehlende Referenzen, Pfadflucht und Git-loses Ziel. Der Testadapter erlaubt eine Fixture-Registry nur im Testprozess; die produktive CLI besitzt keinen Fixture-Schalter.

Ein deterministischer Generator in `AiNetReview.IntegrationTests/Performance` erzeugt zur Testlaufzeit eine temporäre C#-Solution mit mindestens 180.000 belegten Codezeilen in mehreren Projekten. Belegt ist eine physische Zeile mit mindestens einem C#-Token außerhalb von Kommentartrivia. Der Test läuft als separate Kategorie `Performance` vor dem ersten Release auf Windows mit mindestens 4 vCPU und 16 GiB RAM. Die Infrastruktur muss mit `template-noop` in höchstens 10 Minuten und höchstens 6 GiB Peak-Private-Bytes vollständig laden, kompilieren und Markdown veröffentlichen. Gemessen werden Laufzeit, Peak-Speicher, Dokumente und analysierte Projekte. Dieser Test misst die Infrastruktur; spätere fachliche Regeln brauchen bei Bedarf eigene Lasttests. Reguläre CI führt die Performance-Kategorie nicht bei jedem Commit aus.

## Definition of Done

Der erste nutzbare Stand ist fertig, wenn:

- die produktive Registry genau `template-noop` enthält, eine gültige `ainetreview.json` einen vollständigen Null-Finding-Bericht erzeugt und fehlgeschlagene Analysen nie als „0 Findings“ gelten;
- eine neue Regel über `IReviewRule`, ihren Ordner und eine Registrierungszeile ergänzt werden kann, ohne Runner, CLI oder Markdownwriter zu ändern;
- die Fixture-Regel aktuelle Findings mit Begründung, Metriken, Quellort und Evidenz im Markdown nachweist und jedes Finding bei einem neuen Lauf erneut erscheint;
- jeder erfolgreiche Lauf einen eigenen vollständigen Zeitstempel-Berichtssatz veröffentlicht, ältere Berichte unverändert lässt und weder Review-Entscheidungen noch separate Quellcode-Snapshots persistiert; Evidenz-Ausschnitte innerhalb der erhaltenen Markdown-Berichte sind zulässig;
- nur die Eingabe-`ainetreview.json`, Markdown-Berichte und interne EXE-relative Logs zum Produktvertrag gehören; es gibt weder MCP noch Store noch einen generierten JSON-Katalog;
- FastTests, IntegrationTests und der separate Infrastrukturlasttest grün sind und das Tool seine eigene Solution erfolgreich analysiert;
- Serilog vom Host-Start an in das EXE-relative `logs/` schreibt und ein Logging-Fehler keinen erfolgreichen CLI-Start vortäuscht;
- Ist-Dokumentation, Repository-Regeln und Abhängigkeiten dem überprüften Produktstand entsprechen.

Die Dokumentation gilt als implementierbar, wenn jedes Feld, jeder Fehlerfall und jedes Berichtselement der drei Vertrags-Epics durch einen Test oder eine eindeutige Validierungsregel abgedeckt ist. Fachliche Regeln und ihre Grenzwerte gehören ausdrücklich nicht zu diesem DoD.
