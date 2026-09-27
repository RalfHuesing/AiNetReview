# Findings and reconciliation

Rules return immutable `FindingDraft` values. The runner validates their identity fields, paths, line numbers, rationale, finite numeric metrics, evidence, snapshot and relevant source file list before reconciling any state. `projectPath` must identify a `.csproj` in the loaded solution, `sourcePath` must be one of its source documents, and all paths must remain inside the configured project root. `sourceFiles` contains unique, normalized repo-relative `.cs` paths, including the primary source. Their SHA-256 hashes cover the file bytes and use the lowercase `sha256:<hex>` form.

The identity key is `(ruleId, projectPath, sourcePath, subjectId, discriminator)`. A new key receives a cryptographically random `F-` ID followed by 32 lowercase hexadecimal characters. The key, rather than the comparison text, preserves the ID across updates.

`FingerprintService` hashes exactly: a 32-bit big-endian fingerprint version, a 32-bit big-endian UTF-8 byte length, and the UTF-8 comparison text. It does not normalize the text. The result is `sha256:` plus 64 lowercase hexadecimal characters. The comparison text is not retained in observations. Snapshots are retained for new, updated and reopened transitions; line endings are normalized to LF.

The runner processes configured rules sequentially in ordinal rule-ID order. It applies these transitions only after every active rule has completed:

| Previous state | Current scan | Event | New state |
| --- | --- | --- | --- |
| No identity | Finding present | `new` | `open` |
| `open` | Same comparison version | None | `open` |
| `open` | Changed fingerprint, fingerprint version, behavior version or options | `updated` | `open` |
| `accepted` or `false-positive` | Same comparison version | None | Unchanged |
| `accepted` or `false-positive` | Changed comparison version | `reopened` | `open` |
| `resolved` | Finding present | `reopened` | `open` |
| Any active state | Finding absent from an active rule | `resolved` | `resolved` |
| `resolved` | Finding still absent | None | `resolved` |

An inactive rule's findings are left unchanged. A rule exception, invalid draft, or cancellation prevents the completed-run store call, so an empty result is returned only after a complete run.

The current `InMemoryFindingStore` keeps transitions only for the lifetime of the process and has no verdict-writing operation. `FixtureFindingRule` exists only in IntegrationTests and emits two controlled method findings whose scenario, comparison text and behavior version can be varied. The production registry remains limited to `template-noop`. Durable storage and review decisions are not implemented yet.
