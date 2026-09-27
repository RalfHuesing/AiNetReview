# Roadmap: Initial infrastructure

Die Epics definieren die Verträge; diese Liste zeigt Reihenfolge und Fortschritt. Ein Haken bestätigt nur den benannten Punkt. Produktabnahme erfolgt nach [Epic 4](epics/04-Umsetzung-und-Abnahme.md#definition-of-done).

Für jeden vergleichbaren Infrastrukturbaustein vor der Umsetzung die [getesteten AiNetLinter-Referenzen](epics/04-Umsetzung-und-Abnahme.md#ainetlinter-als-referenz) prüfen; die Abnahme richtet sich ausschließlich nach AiNetReview-Verträgen und -Tests.

## M0 — Grundlage

- [x] Fünf-Projekt-Skelett mit .NET-10-Buildkonfiguration ist vorhanden.
- [x] TestKit mit isoliertem `temp/` sowie Build- und Testskripte sind vorhanden.
- [x] Laufende Infrastrukturänderungen abschließen; Build und FastTests über die Skripte prüfen.

## M1 — Eingaben und Regeln

- [x] Serilog-Dateilogging beim Host-Start gemäß [Epic 1](epics/01-Eingaben-und-Host.md#logging) einrichten und durch Prozesstests prüfen.
- [ ] Konfiguration, Pfadvalidierung und Solution-Lader gemäß [Epic 1](epics/01-Eingaben-und-Host.md) umsetzen.
- [ ] Composition Root, Regelvertrag, Registry, `template-noop` und Katalog gemäß [Epic 2](epics/02-Regel-und-Findings.md) anschließen.

## M2 — Findings und Speicherung

- [ ] Identität, Fingerprint, Zustandsautomat und Fixture-Regel gemäß [Epic 2](epics/02-Regel-und-Findings.md) umsetzen und prüfen.
- [ ] JSON-Store, Snapshots und Markdown-Berichte gemäß [Epic 3](epics/03-Storage-und-Berichte.md) umsetzen.

## M3 — Schnittstellen und Abnahme

- [ ] CLI und MCP an denselben Runner anschließen; Locking, Polling und Stale-Prüfung gemäß [Epic 1](epics/01-Eingaben-und-Host.md) abschließen.
- [ ] FastTests, IntegrationTests, Dogfooding und separaten Lasttest gemäß [Epic 4](epics/04-Umsetzung-und-Abnahme.md) bestehen.
- [ ] Produkt-DoD aus [Epic 4](epics/04-Umsetzung-und-Abnahme.md#definition-of-done) vollständig nachweisen.
