# Rule Catalog

`CatalogWriter` generates `docs/ainetreview-rules.md` and `ainetreview.example.json` from the registered rules' `RuleDescriptor` values. It creates the `docs/` directory when needed and does not modify `ainetreview.json`.

The Markdown catalog lists rules by ordinal rule ID and includes each rule's behavior version, purpose, measurement, review questions, and option defaults. A descriptor marked as a template is labeled as a technical template. The example configuration contains every registered rule and its defaults, with `schemaVersion: 1`, an empty `solution`, `outputDirectory: "audit-reporting"`, and `storageDirectory: ".ainetreview"`.

Both generated files use UTF-8 without a byte-order mark and deterministic ordering. The current host registration contains only `template-noop`, which has no options.
