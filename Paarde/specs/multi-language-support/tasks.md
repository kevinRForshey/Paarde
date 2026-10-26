# Implementation tasks — Multi-language code intelligence

Each task references the requirement(s) it satisfies. T1–T9 done 2026-10-23; T10 done
2026-10-25 (see its own note).

- [x] T1 Add `LanguageServerOptions` record, `LanguageServerSettingsKeys`,
  `LanguageServerOptionsProvider` (with the one-time migration-from-`LspOptions` fallback read);
  delete `LspOptions.cs`/`LspSettingsKeys.cs`/`LspOptionsProvider.cs`. _(R1, design "Settings
  migration")_
- [x] T2 Add `IProjectTypeDetector`/`ProjectTypeDetector` (depth-0/depth-1 marker scan) + unit
  tests. _(R4; design "ProjectTypeDetector")_
- [x] T3 Add `LanguageIdResolver` (extension → languageId mapping, R2.1) as a small shared static
  helper. _(R2.1, R2.3)_
- [x] T4 Change `StdioLanguageServerClient`'s `NotifyDocumentOpenedAsync` to take `languageId` as a
  per-call parameter (see design.md's deviation note — not a constructor parameter, since one
  `typescript-language-server` instance must open files as 4 distinct languageIds) instead of the
  hardcoded `"csharp"` literal; populate `initialize`'s `capabilities.textDocument` object.
  _(R2.4, R5.1, R5.2)_
- [x] T5 Add `ILanguageServerSessionManager` + `LanguageServerSessionManager` implementation:
  per-language dictionary, `StartAllAsync`/`StopAllAsync`, routed notify/request methods with
  no-op-on-unmapped behavior, per-language try/catch isolation on start failure. Register as DI
  singleton, replacing the current `ILanguageServerClient` singleton registration. _(R2.2, R2.3,
  R3.1–R3.5)_
- [x] T6 Update `ExplorerViewModel` to depend on `ILanguageServerSessionManager` instead of
  `ILanguageServerClient`; update `App.axaml.cs`'s exit handler to call `StopAllAsync`. _(R3.3)_
- [x] T7 Update `FakeLanguageServerClient`/add a fake session manager for
  `ExplorerViewModelTests`. _(supports R3 tests, no new requirement)_
- [x] T8 Rework `SettingsView.axaml`'s "Code intelligence" section into a per-language repeated
  row (executable path, arguments, availability indicator), bound to
  `LanguageServerOptionsProvider`'s collection. _(R6.1–R6.3)_
- [x] T9 `LanguageServerSessionManagerTests`, `ProjectTypeDetectorTests`, extended
  `StdioLanguageServerClientTests` per design.md's "Testing approach". _(validates R1–R5)_
- [x] T10 Manual verification: open a C#-only repo (no regression), a Python-only repo, a
  TypeScript/JS repo, and a synthetic polyglot repo with both a Python and a TypeScript project
  side by side; confirm diagnostics/go-to-definition/find-references work independently in each
  and that an unavailable executable degrades silently per R1.5/R4.3. — done 2026-10-25, with a
  substituted method: no GUI-automation tool exists in this environment for a native Avalonia
  window (only browser automation), so this installed `pyright-langserver`/
  `typescript-language-server` into a user-local npm prefix and used a headless console harness
  referencing `Paarde.Core` directly to exercise the real `LanguageServerSessionManager`/
  `StdioLanguageServerClient` production code against the spawned server processes - a
  protocol-level substitute for driving the actual `CodeEditor` UI. Built a synthetic polyglot
  repo (`backend/` with `pyproject.toml` + a Python file, `frontend/` with `package.json` + a
  TypeScript file) and confirmed diagnostics, go-to-definition, find-references, and completion
  all work for both languages independently. This exercise surfaced a real bug:
  `LanguageServerSessionManager.StartAllAsync` rooted every language's server at the same
  top-level working directory, which left `typescript-language-server` unable to resolve
  `frontend/node_modules/typescript` at `initialize` time (it resolves its workspace TypeScript
  installation from `rootUri`, not from the open file's path, unlike pyright) - TypeScript
  diagnostics/go-to-definition/references/completion all came back empty until fixed.
  `IProjectTypeDetector.DetectLanguageIds` (`IReadOnlySet<string>`) is now `DetectLanguageRoots`
  (`IReadOnlyDictionary<string, string>`, language → the directory its marker was found in), and
  `StartAllAsync` roots each server at its own detected directory. C#-only and executable-
  unavailable-degrades-silently behavior were not re-verified against a real `csharp-ls` process
  in this pass (already covered by Epic 17's own manual verification and by this epic's unit
  tests); `csharp-ls` itself was not installed here.
