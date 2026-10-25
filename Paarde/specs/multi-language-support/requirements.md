# Requirements — Multi-language code intelligence

## Status
Draft. Not started. Supersedes/extends Epic 17 (`ROADMAP.md`), which shipped C#-only by
deliberate scope decision ("no speculative multi-language support built ahead of a second real
use case" — T17.1.1). This spec is that second use case.

## Context and current state

Confirmed by direct inspection of the installed packages and source, 2026-10-23:

- **Syntax highlighting is already language-agnostic and already works for every target
  language.** `TextMateSharp.Grammars`' `RegistryOptions.GetLanguageByExtension` was probed
  directly and resolves `.py` → `python`, `.ts` → `typescript`, `.tsx` → `typescriptreact`,
  `.js` → `javascript`, `.jsx` → `javascriptreact` (alongside the existing `.cs` → `csharp`).
  `CodeEditor.ApplyGrammarForFilePath()` already calls this generically. **Syntax highlighting is
  out of scope for this spec — there is nothing to build.**
- **Semantic intelligence is entirely C#-shaped and single-language by construction.** This is
  the actual gap:
  - `LspOptions` (`Paarde.Core/Models/LspOptions.cs`) is one global
    `record LspOptions(string ExecutablePath, string Arguments)` defaulting to `("csharp-ls", "")`.
    There is no per-language concept at all.
  - `StdioLanguageServerClient` (`Paarde.Core/Services/StdioLanguageServerClient.cs`) owns exactly
    one `_process` and one `_workingDirectoryPath` field. Only one LSP server can run at a time.
  - `NotifyDocumentOpenedAsync` hardcodes `["languageId"] = "csharp"` in the `didOpen`
    notification, regardless of the file actually opened.
  - `ExplorerViewModel`'s LSP-start logic has zero project-type gating: it unconditionally calls
    `_languageServerClient.StartAsync(WorkingDirectoryPath)` whenever a working directory is set
    and whatever executable `LspOptions` names (default `csharp-ls`) is found on PATH — it never
    checks whether the opened project is actually a C#/.NET project.
  - `SettingsView.axaml` exposes exactly one `LspExecutablePath`/`LspArguments` field pair.
  - Repo-wide grep confirms **zero** project-type/language detection logic exists anywhere in
    `Paarde.Core` or `Paarde.Avalonia` outside test fixtures.
- **"React" is not a distinct language server target.** React components are TypeScript/JavaScript
  files with JSX syntax (`.tsx`/`.jsx`); there is no separate "React language server." Microsoft's
  `typescript-language-server` (wrapping `tsserver`) already handles `.ts`/`.tsx`/`.js`/`.jsx`
  through one process. This spec treats "Python, TypeScript, React, and JavaScript" as **three**
  LSP-relevant language groups: Python, and TypeScript+JavaScript+JSX/TSX (one server).

## Goals

1. Add live diagnostics, go-to-definition, and find-references (the same three capabilities Epic
   17 shipped for C#) for Python and for TypeScript/JavaScript/JSX/TSX files, in Explorer's
   `CodeEditor` only — same scope boundary Epic 17 already established (Tutorial's proposal editor
   is explicitly out of scope; see Epic 17's "Why").
2. Support a project containing more than one of these languages at once (e.g. a repo with a
   `backend/` Python service and a `frontend/` TypeScript app) without the user manually switching
   configuration.
3. Make adding a fifth language later (explicitly anticipated by "for now" in the request that
   produced this spec) a configuration change, not a code change, wherever reasonably possible.
4. Preserve every existing C#/`csharp-ls` behavior exactly — this is additive, not a rewrite of
   Epic 17.

## Non-goals

- Syntax highlighting (already works — see Context).
- Any language beyond Python/TypeScript/JavaScript/JSX-TSX/C# in this pass.
- Semantic intelligence in Tutorial's proposal editor (same boundary Epic 17 drew).
- Symbol/member completion dropdown UI — that is [[editor-autocomplete]]'s concern. This spec's
  `initialize` capability advertisement (R5) is a shared dependency between the two specs; see
  "Cross-spec dependency" below.
- Auto-installing language servers for the user (npm/pip install flows). This spec assumes the
  user has the relevant server executable on PATH, exactly as Epic 17 already assumes for
  `csharp-ls`.

## Cross-spec dependency

[[editor-autocomplete]] requires `textDocument/completion` support, which requires the
`initialize` handshake to advertise a non-empty `textDocument.completion` client capability (today
`StdioLanguageServerClient` sends an empty `capabilities` object — confirmed by reading
`StdioLanguageServerClient.cs`). Whichever spec is implemented first should land that capability
change, since both need it; it is listed under this spec's R5 but is not exclusive to it.

## Requirements

### R1 — Per-language server configuration

**User story:** As a user working in a Python or TypeScript/JavaScript project, I want to
configure which language server runs for that language, so that code intelligence works the same
way it already does for C#.

Acceptance criteria (EARS):
1. WHEN the application loads settings THE SYSTEM SHALL read a per-language collection of
   `(languageId, executablePath, arguments)` entries instead of the single global `LspOptions`.
2. THE SYSTEM SHALL ship with built-in defaults for three entries: `csharp` → `csharp-ls`
   (unchanged from today), `python` → `pyright-langserver --stdio`, and `typescript` →
   `typescript-language-server --stdio`.
3. IF a user has not changed Settings since upgrading THEN THE SYSTEM SHALL behave identically to
   pre-upgrade behavior for C# projects (i.e. the migration from the old single `LspOptions` to
   the new per-language collection SHALL preserve any customized C# executable path/arguments the
   user already configured).
4. WHEN a user edits a language's executable path or arguments in Settings THE SYSTEM SHALL
   apply the change on the next server start for that language (restart required, not hot-swapped
   into a running server — consistent with how changing `LspExecutablePath` behaves today).
5. IF a configured executable for a language is not found on PATH THEN THE SYSTEM SHALL disable
   that language's code intelligence silently degrading to syntax-highlighting-only for files of
   that language, with a non-blocking status message — mirroring today's `IsAvailableAsync` false
   path for C#, not a hard error.

### R2 — Language detection from file identity

**User story:** As a user opening a file, I want the system to know what language it is without
me telling it, so that the right server handles it automatically.

Acceptance criteria:
1. WHEN a file is opened in Explorer's `CodeEditor` THE SYSTEM SHALL derive an LSP `languageId`
   from the file's extension using a fixed mapping: `.cs` → `csharp`; `.py` → `python`; `.ts` →
   `typescript`; `.tsx` → `typescriptreact`; `.js`/`.mjs`/`.cjs` → `javascript`; `.jsx` →
   `javascriptreact`.
2. THE SYSTEM SHALL route every `textDocument/*` notification and request for that file (`didOpen`,
   `didChange`, `didClose`, `definition`, `references`) to the server instance responsible for its
   derived `languageId`, not to a single global server instance.
3. IF a file's extension has no entry in the mapping THEN THE SYSTEM SHALL NOT start or address any
   language server for that file — it behaves exactly as today's non-C# files already do (syntax
   highlighting only, no LSP calls attempted).
4. THE SYSTEM SHALL stop sending `"languageId": "csharp"` unconditionally in `didOpen` (today's bug
   — `StdioLanguageServerClient.NotifyDocumentOpenedAsync` hardcodes this) and instead send the
   `languageId` derived per R2.1 for whichever server is handling the notification.

### R3 — Concurrent multi-server lifecycle

**User story:** As a user with a polyglot repository (e.g. Python backend + TypeScript frontend
in one working directory), I want diagnostics/definition/references to work in both languages at
once, so that I don't have to pick one language to get intelligence for.

Acceptance criteria:
1. WHEN a working directory is opened THE SYSTEM SHALL independently evaluate, for each configured
   language, whether that language's project markers are present in the working directory (see R4),
   and start that language's server if so and if its executable is available (R1.5).
2. THE SYSTEM SHALL support zero, one, or multiple language server processes running concurrently
   against the same working directory, each independently started/stopped/torn down.
3. WHEN the application exits or the working directory changes THE SYSTEM SHALL stop every running
   language server process, not just one — extending Epic 17's existing single-process teardown
   (`App.axaml.cs`'s `desktop.Exit` handler) to iterate every active session.
4. IF one language server process crashes or fails to start THEN THE SYSTEM SHALL continue
   operating normally for every other language's server and for syntax-highlighting-only files —
   one server's failure SHALL NOT block or crash any other.
5. THE SYSTEM SHALL publish diagnostics (`DiagnosticsPublished`) tagged with enough information
   (file path is sufficient, since paths are already language-unambiguous) that `ExplorerViewModel`
   continues to filter correctly to "whichever file is currently open" exactly as it does today,
   regardless of which underlying server instance raised the event.

### R4 — Project-type detection to decide which servers to attempt

**User story:** As a user opening a project, I don't want the system to attempt to launch a
Python language server against a pure C# repo (or vice versa), so that I don't see irrelevant
startup attempts, log noise, or failed-executable status messages for languages my project doesn't
use.

Acceptance criteria:
1. WHEN a working directory is opened THE SYSTEM SHALL detect project-type markers at the working
   directory root and in its immediate subdirectories (non-recursive beyond one level is
   sufficient — see Design for the exact depth trade-off) using: presence of any `*.csproj`/`*.sln`
   file → candidate C# project; presence of `pyproject.toml`, `setup.py`, or `requirements.txt` →
   candidate Python project; presence of `package.json` → candidate TypeScript/JavaScript project.
2. THE SYSTEM SHALL attempt to start a language's server only if that language's marker was
   detected per R4.1 **or** the user has explicitly force-enabled that language in Settings
   (escape hatch for unusual layouts the marker heuristic misses).
3. IF no markers are detected at all (e.g. a folder of loose files, or a language with no marker
   convention) THEN THE SYSTEM SHALL start no language servers and behave exactly as the
   pre-multi-language app does for an unrecognized project — syntax highlighting only, no error.
4. THE SYSTEM SHALL re-run detection each time a different working directory is opened — detection
   results are not cached across working directories.

### R5 — Capability handshake update (shared with [[editor-autocomplete]])

**User story:** As a developer implementing either this spec or the autocomplete spec, I need the
`initialize` handshake to actually advertise client capabilities, so that servers know what the
client supports.

Acceptance criteria:
1. WHEN `StdioLanguageServerClient` sends `initialize` THE SYSTEM SHALL include a non-empty
   `capabilities.textDocument` object advertising at minimum `publishDiagnostics`, `definition`,
   `references` (already relied upon today, just not formally advertised) and SHALL be structured
   so `completion` support (per [[editor-autocomplete]] R2) can be added without a second breaking
   change to this handshake.
2. THE SYSTEM SHALL NOT regress any existing diagnostics/definition/references behavior for C#
   projects as a result of this change — `csharp-ls` must continue to respond identically to an
   `initialize` request whose `capabilities` object is now populated instead of empty.

### R6 — Settings UI for per-language configuration

**User story:** As a user, I want to see and edit each language's server configuration in one
place, so that configuring Python/TypeScript intelligence feels like the existing C# experience,
not a new concept.

Acceptance criteria:
1. WHEN the user opens Settings' "Code intelligence" section THE SYSTEM SHALL show one row per
   configured language (initially: C#, Python, TypeScript/JavaScript), each with its own
   executable-path and arguments fields, replacing today's single flat pair.
2. THE SYSTEM SHALL show, per language row, whether that language's server is currently detected as
   available (executable found on PATH) — reusing the existing `IsAvailableAsync` preflight concept
   per language instance.
3. IF the user clears a language's executable path THEN THE SYSTEM SHALL treat that language as
   disabled (same as R1.5) rather than falling back to any default.

## Open questions for design phase

- Exact recommended default server per language (R1.2) — `pyright-langserver` vs. `python-lsp-server`
  for Python is a real trade-off (pyright: faster, type-checking-focused, Node-based, no separate
  pip install of a checker; pylsp: plugin-based, pure Python, weaker type inference out of the box).
  Default to **pyright** unless the user has a reason to prefer pylsp — flag this choice in design.md
  rather than deciding silently.
- Whether `StdioLanguageServerClient` should be refactored into a single class parameterized by
  `languageId`, with N instances held by a new session-manager type, vs. introducing a new
  wrapper type that holds N `StdioLanguageServerClient`s. Design phase decision — see design.md.
