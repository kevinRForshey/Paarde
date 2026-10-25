# Implementation tasks — Editor autocomplete

Each task references the requirement(s) it satisfies. Not started — no code written yet.
Depends on [[multi-language-support]] T1–T6 only if full multi-language completion is desired;
T1–T6 below can land against the existing single `ILanguageServerClient` (C# only) independently,
then pick up multi-language routing for free once that spec's session manager exists.

- [x] T1 Add `LspCompletionItem` DTO; add `GetCompletionAsync` to `ILanguageServerClient`. _(R2.2)_
  — done 2026-10-23, alongside T2 (see `ROADMAP.md` T19.1.1 for the full note). Also added to
  `ILanguageServerSessionManager`/`LanguageServerSessionManager` since that façade now exists.
- [x] T2 Implement `GetCompletionAsync` in `StdioLanguageServerClient`, including the
  array-vs-`CompletionList` response parsing; add `capabilities.textDocument.completion` to the
  `initialize` request. _(R2.1, R2.2)_ — done 2026-10-23, see `ROADMAP.md` T19.1.1.
- [x] T3 Add `CodeEditorCompletionData : ICompletionData` adapter + unit tests for its `Complete`
  behavior and `InsertText ?? Label` fallback. _(R4.1)_ — done 2026-10-23, see `ROADMAP.md`
  T19.2.1/T19.4.1. The fallback (exposed as `InsertionText`) is unit tested; `Complete` itself
  isn't - it needs a live `AvaloniaEdit.Editing.TextArea`, which throws when constructed outside a
  running Avalonia `Application` in this sandbox (confirmed directly, see `ROADMAP.md` T19.4.1).
- [x] T4 Wire `TextArea.TextEntered` (trigger character `.`) and a new Ctrl+Space keybind on
  `CodeEditor` to a `TriggerCompletionAsync` method that calls `GetCompletionAsync` and shows a
  `CompletionWindow` when results are non-empty. _(R1.1, R1.2, R1.4, R3.1)_ — done 2026-10-23, see
  `ROADMAP.md` T19.2.1. Deviated from this task's and design.md's framing of `CodeEditor` calling
  `GetCompletionAsync` directly - it goes through the same event+property round trip as Epic 15/17
  instead (`CompletionRequested`/`CompletionItems`), with `ExplorerViewModel.
  RequestCompletionAsync` making the actual `GetCompletionAsync` call. `TriggerCompletion()` (not
  `TriggerCompletionAsync` - the async half lives in the ViewModel, not `CodeEditor`) is the method
  that raises `CompletionRequested`.
- [x] T5 Confirm (by exercising the real control, not just reading docs) that
  `CompletionWindow`/`CompletionList`'s built-in filtering and auto-close-on-no-match satisfy
  R3.3 as-is; write a thin adapter only if they don't. _(R3.2, R3.3, R3.4)_ — done 2026-10-23 via
  `ilspycmd` decompile of the installed `AvaloniaEdit.dll` (no live control available to exercise
  directly in this sandbox - same substitute the rest of this codebase has used for other
  undocumented AvaloniaEdit behavior). Confirmed no adapter needed; see `ROADMAP.md` T19.2.2 for
  the exact mechanics, including the "`IsVisible = false`, not a literal close" nuance.
- [x] T6 Gate `GhostTextDebouncer`'s existing fire conditions behind "no completion window
  currently open," and dismiss any visible ghost-text suggestion the moment a completion window
  opens. _(R6.1, R6.2, R6.3)_ — done 2026-10-23, see `ROADMAP.md` T19.3.1.
- [x] T7 `StdioLanguageServerClientTests` extensions (both response shapes, capability
  advertisement) + ghost-text/completion coexistence gating test. _(validates R2, R6)_ — done
  2026-10-23, see `ROADMAP.md` T19.4.1. The `StdioLanguageServerClientTests` half landed under T1/T2;
  the coexistence gating has no automated test (same `CodeEditor`-level sandbox limitation as T3) -
  covered by code inspection plus T8's manual verification instead.
- [ ] T8 Manual verification in a real C# project: typing `.` after a known symbol shows a
  populated dropdown; Tab/Enter/click accept; Escape dismisses; a ghost-text suggestion visible at
  trigger time disappears when the dropdown opens; typing past all matches auto-closes the window.
  Repeat once [[multi-language-support]] ships for a Python and a TypeScript file. — not done: no
  display/running `Application` available in this sandbox.
