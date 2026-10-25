# Design — Multi-language code intelligence

Companion to `requirements.md` in this directory. Implements R1–R6.

## Summary of the architectural change

Today: one `ILanguageServerClient` singleton, one `LspOptions`, one hardcoded `languageId`.

Target: one **session manager** that owns zero-or-more `ILanguageServerClient` instances, keyed by
`languageId`, each independently configured, started, and torn down. `ExplorerViewModel` talks to
the manager, not to individual clients — this keeps its code almost unchanged from Epic 17's
shape (still one `DiagnosticsPublished`-shaped event to subscribe to, still one place to call
`NotifyDocumentOpenedAsync`/`GetDefinitionAsync`/etc.), just routed by the manager underneath.

## New/changed types

### `Paarde.Core/Models/LanguageServerOptions.cs` (new, replaces the shape of `LspOptions.cs`)

```csharp
public record LanguageServerOptions(string LanguageId, string ExecutablePath, string Arguments, bool ForceEnabled = false);
```

`LspOptions.cs` itself is deleted; its single-instance settings key is migrated (see "Settings
migration" below) rather than kept alongside the new type, to avoid two parallel configuration
shapes existing at once — Epic 17's `LspOptions`/`LspSettingsKeys`/`LspOptionsProvider` seam is
fully superseded, not wrapped.

Built-in defaults (R1.2), seeded when no settings exist yet:
```csharp
new LanguageServerOptions("csharp",     "csharp-ls", ""),
new LanguageServerOptions("python",     "pyright-langserver", "--stdio"),
new LanguageServerOptions("typescript", "typescript-language-server", "--stdio"),
```

Rationale for `pyright` over `python-lsp-server` (open question from requirements.md): pyright is
what VS Code itself ships for Python and has the best out-of-box type-checking signal with zero
plugin configuration; `python-lsp-server` needs separate plugin installs (e.g. `pylsp-mypy`) to
reach comparable diagnostics quality. Both are swappable via Settings regardless (R1.4), so this
default is a quality-of-first-run decision, not a lock-in.

The `typescript` entry's single server handles `.ts`, `.tsx`, `.js`, `.jsx` — see R2.1's mapping;
there is no separate `typescriptreact`/`javascript` row in settings, only in the internal
extension→languageId table, because `typescript-language-server` is one process regardless of
which of those four extensions triggered it.

### `Paarde.Core/Models/LanguageServerSettingsKeys.cs` / `LanguageServerOptionsProvider.cs` (new)

Same settings-provider seam Epic 17 established for `LspOptionsProvider`, but storing a
`List<LanguageServerOptions>` (serialized the same way `RecentWorkingDirectories` or other
list-shaped settings already are in this codebase) instead of one flat record.

**Settings migration:** on first load after this change ships, if the old `LspOptions` settings
key is present and the new per-language key is absent, synthesize a single
`LanguageServerOptions("csharp", <old ExecutablePath>, <old Arguments>)` entry, seeded alongside
the Python/TypeScript defaults, then stop reading the old key. This satisfies R1.3 without a
separate "migration service" — it's a one-time fallback read in `LanguageServerOptionsProvider`'s
load path.

### `Paarde.Core/Contracts/Services/ILanguageServerSessionManager.cs` (new)

```csharp
public interface ILanguageServerSessionManager
{
    event Action<string filePath, IReadOnlyList<LspDiagnostic> diagnostics>? DiagnosticsPublished;

    Task StartAllAsync(string workingDirectoryPath, CancellationToken ct = default);
    Task StopAllAsync(CancellationToken ct = default);

    Task NotifyDocumentOpenedAsync(string filePath, string content, CancellationToken ct = default);
    Task NotifyDocumentChangedAsync(string filePath, string content, CancellationToken ct = default);
    Task NotifyDocumentClosedAsync(string filePath, CancellationToken ct = default);

    Task<IReadOnlyList<LspLocation>> GetDefinitionAsync(string filePath, LspPosition position, CancellationToken ct = default);
    Task<IReadOnlyList<LspLocation>> FindReferencesAsync(string filePath, LspPosition position, CancellationToken ct = default);
}
```

This is almost exactly `ILanguageServerClient`'s existing surface (reusing `LspDiagnostic`/
`LspLocation`/`LspPosition` as-is — no change to those small DTOs) with `StartAsync`/`StopAsync`'s
single working-directory parameter kept, since the manager, not each client, now owns the
"per working directory" concept. `ILanguageServerClient` itself is **unchanged** — it keeps
representing exactly one running server process for exactly one language, which is still the
right unit for `StdioLanguageServerClient` to implement. The manager is the new layer, not a
replacement for the client abstraction.

### `Paarde.Core/Services/LanguageServerSessionManager.cs` (new)

Holds `Dictionary<string languageId, ILanguageServerClient>` for whichever languages R4's
detection (+ R1.5's availability check) decided to start. Routing logic:

- `NotifyDocumentOpenedAsync`/`Changed`/`Closed`/`GetDefinitionAsync`/`FindReferencesAsync` all
  take a `filePath` first; the manager derives `languageId` from its extension (R2.1's fixed
  mapping, implemented as a small internal `LanguageIdResolver.Resolve(string filePath) : string?`
  static helper — shared by both this manager and, later, by [[editor-autocomplete]]'s completion
  routing, so it's worth it being its own small class rather than inlined). If no client is
  registered for that `languageId` (R2.3 — unmapped extension, or mapped but that language's
  server never started per R4), every method becomes a documented no-op (notifications) or returns
  an empty result (requests) — mirrors `StdioLanguageServerClient`'s existing "no-op safely when
  the server isn't running" convention (Epic 17, T17.2.1's fire-and-forget rationale), now applied
  per-language instead of globally.
- `DiagnosticsPublished` is re-raised (not transformed) from whichever underlying client raised it
  — `ExplorerViewModel`'s existing file-path-based filtering (R3.5) needs no changes.
- `StartAllAsync` performs R4's project-marker detection, then for each language whose marker was
  found (or whose `ForceEnabled` flag is set) and whose executable passes `IsAvailableAsync`,
  constructs a `StdioLanguageServerClient` from that language's `LanguageServerOptions` and calls
  its `StartAsync`. Failures are caught **per language** (R3.4) — one `try`/`catch` per language
  in the loop, logged/surfaced as today's existing non-blocking status-message pattern, never
  aborting the loop for the remaining languages.
- `StopAllAsync` iterates every entry in the dictionary and calls `StopAsync` on each, replacing
  Epic 17's single `StopAsync()` call in `App.axaml.cs`'s exit handler (R3.3).

Registered as a DI **singleton**, same lifetime rationale Epic 17 gave for the single client
(T17.1.1: "Explorer only ever has one active working directory at a time, so one long-lived
session is simpler than a keyed multi-session manager" — that reasoning now lives one layer up,
inside this manager, rather than at the DI-registration level).

### `Paarde.Core/Services/ProjectTypeDetector.cs` (new)

```csharp
public interface IProjectTypeDetector
{
    IReadOnlySet<string> DetectLanguageIds(string workingDirectoryPath);
}
```

Implements R4.1: `Directory.EnumerateFiles`/`EnumerateDirectories` at depth 0 and depth 1 (the
working directory itself, plus its immediate subdirectories — covers the common `backend/` +
`frontend/` polyglot-repo split named in requirements.md's R3 user story, without a full recursive
walk that could be slow on a large monorepo and isn't needed for the marker files in question,
which conventionally live at a project's own root). Returns the set of `languageId`s whose marker
was found; `*.csproj`/`*.sln` → `"csharp"`; `pyproject.toml`/`setup.py`/`requirements.txt` →
`"python"`; `package.json` → `"typescript"`. Pure and synchronous (no I/O beyond local directory
enumeration) — trivially unit-testable against a temp-directory fixture, same style
`ExplorerViewModelTests` already uses for file-tree fixtures.

### `Paarde.Core/Services/StdioLanguageServerClient.cs` (changed, not replaced)

- **Deviation from the plan above:** `languageId` is a per-call parameter on
  `NotifyDocumentOpenedAsync`, not a constructor parameter. A single configured
  `typescript-language-server` process (keyed `"typescript"`, the settings/process group) must
  open files as four distinct LSP `languageId`s — `typescript`/`typescriptreact`/`javascript`/
  `javascriptreact` — depending on the specific file's extension, so a fixed per-instance
  `languageId` would have been wrong for that process. `LanguageIdResolver.Resolve(filePath)`
  computes the per-file value; `LanguageServerSessionManager` passes it through on each call.
  `LanguageIdResolver.ResolveServerGroup(filePath)` is the separate, coarser mapping used only to
  pick *which* running client instance handles a file.
- `NotifyDocumentOpenedAsync` sends that per-call `languageId` instead of the literal `"csharp"`
  (R2.4).
- `initialize`'s request body gains a populated `capabilities.textDocument` object (R5.1):
  `publishDiagnostics: {}`, `definition: {}`, `references: {}`. Adding `completion: {...}` is
  explicitly left to whichever spec lands first per requirements.md's "Cross-spec dependency" —
  the object shape here should make that a one-property addition, not a restructure.

### `ExplorerViewModel` (changed)

- Constructor takes `ILanguageServerSessionManager` instead of `ILanguageServerClient`.
- `OnNavigatedTo` calls `StartAllAsync(WorkingDirectoryPath)` instead of `StartAsync`.
- Every other call site (`LoadNode`'s `didOpen`/`didClose`, `OnSelectedFileContentChanged`'s
  `didChange`, F12/Shift+F12 handlers) changes only its receiver type, not its logic — the manager
  presents the same method shapes.

### `Paarde.Avalonia.Tests/Fakes/FakeLanguageServerClient.cs` (changed)

Gains a `FakeLanguageServerSessionManager` sibling (or the existing fake is extended to also
implement the new manager interface trivially) so `ExplorerViewModelTests`' constructor injection
keeps working without standing up real per-language fakes for every test.

## Settings UI (`SettingsView.axaml`, R6)

Replace the single `LspExecutablePath`/`LspArguments` `TextBox` pair with an `ItemsControl`/
`ItemsRepeater` bound to the per-language collection, one row per language with: language name
label, executable-path `TextBox`, arguments `TextBox`, and an availability indicator (reusing
whatever visual convention existing preflight checks in this app already use — e.g. Claude CLI's
availability check in Settings, if one exists, otherwise a simple `TextBlock` bound to a
per-row `IsAvailable` property refreshed on page load). Exact row count is driven by
`LanguageServerOptionsProvider`'s current collection, not hardcoded to three, so a sixth language
added later (per requirements.md's explicit "for now" framing) needs no XAML change — only a new
default entry in the provider.

## Testing approach

- `ProjectTypeDetectorTests`: temp-directory fixtures per marker file, asserting the right
  `languageId` set, including a polyglot fixture with both `package.json` and `requirements.txt`
  present (R3's motivating scenario) and an empty-folder fixture (R4.3).
- `LanguageServerSessionManagerTests`: against 2+ `FakeLanguageServerClient`-style stubs keyed by
  language, asserting routing by extension (R2.2), no-op on unmapped extension (R2.3), one
  client's simulated start failure not affecting another's successful start (R3.4), and
  `StopAllAsync` calling `StopAsync` on every started client (R3.3).
- `StdioLanguageServerClientTests` (existing, extended): assert the `didOpen` notification's
  `languageId` field reflects the caller-supplied per-call value, not a hardcoded `"csharp"` (R2.4),
  and that `initialize`'s outgoing request body now contains the R5.1 capability object — both
  checkable against the existing Python-script fake-server fixture's recorded request log.
- No new test attempts a real `pyright`/`typescript-language-server` process — same "fake external
  process" rationale Epic 17 already documented (environment-fragile, slow, not what's under test).
