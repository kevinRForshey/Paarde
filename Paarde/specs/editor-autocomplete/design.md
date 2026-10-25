# Design — Editor autocomplete (LSP symbol/member completion)

Companion to `requirements.md` in this directory. Implements R1–R7.

## Summary

Add one new `IBackgroundRenderer`-sibling-free feature to `CodeEditor`: wiring AvaloniaEdit's own
`AvaloniaEdit.CodeCompletion.CompletionWindow` to `textDocument/completion`. Unlike every other
`CodeEditor` extension so far (ghost text, diagnostics, bracket highlight, diff rendering — all
bespoke `IBackgroundRenderer`/`VisualLineElementGenerator` types), this one has a first-party
AvaloniaEdit control to anchor on, so the amount of new rendering code is small; most of the work
is request plumbing and the ghost-text coexistence rule (R6).

## New/changed types

### `Paarde.Core/Contracts/Services/ILanguageServerClient.cs` (changed)

Add:
```csharp
Task<IReadOnlyList<LspCompletionItem>> GetCompletionAsync(string filePath, LspPosition position, CancellationToken ct = default);
```
alongside the existing `GetDefinitionAsync`/`FindReferencesAsync` shape — same parameter and
cancellation convention, no new pattern introduced.

### `Paarde.Core/Models/LspCompletionItem.cs` (new)

```csharp
public record LspCompletionItem(string Label, string? Detail, string? InsertText, int? Kind);
```
`InsertText` falls back to `Label` at the call site (R4.1) rather than being pre-resolved here,
so the DTO stays a faithful, un-opinionated mirror of the wire response. `Kind` is the raw LSP
`CompletionItemKind` integer, carried through for a future icon pass (explicitly out of scope now
— R3.4 only needs `Detail` as a text string); not rendered as anything beyond plain text in v1.

### `StdioLanguageServerClient.cs` (changed)

- `initialize`'s `capabilities.textDocument` object (already being touched by
  [[multi-language-support]] R5) gains `completion: { completionItem: { snippetSupport: false } }`
  (R2.1).
- New `GetCompletionAsync`: sends `textDocument/completion` with `{ textDocument: { uri }, position }`,
  same URI-construction helper the existing `GetDefinitionAsync` already uses. Parses the response
  by checking whether the JSON root is an array (`CompletionItem[]`) or an object with an `items`
  property (`CompletionList`) — both are valid per the LSP spec — and maps each item's `label`/
  `detail`/`insertText`/`kind` into `LspCompletionItem`. If `insertText` is absent, leave
  `InsertText` null (the fallback-to-`Label` logic belongs at the UI call site per R4.1, not here).

### `Paarde.Avalonia/Controls/CodeEditor.cs` (changed)

New private field: `CompletionWindow? _completionWindow`.

- **Trigger wiring (R1):** `TextArea.TextEntered` (AvaloniaEdit's existing event for "a character
  was just inserted," already analogous to how ghost text's `TextChanged` hook works) checks
  whether the just-typed character is `.` (R1's shared trigger character) and, if so, calls a new
  `TriggerCompletionAsync(caretOffset)`. A new keybind (Ctrl+Space) on `TextArea`'s `KeyDown`
  (same tunnel-routing approach already used for ghost text's Tab/Escape interception, per Epic
  15's T15.1.1 note that `TextArea.OnKeyDown` otherwise consumes some keys first) calls the same
  method unconditionally (R1.2).
- **Request (R1.4, R2.2):** `TriggerCompletionAsync` calls
  `ILanguageServerSessionManager.GetCompletionAsync` (once [[multi-language-support]] ships; until
  then, directly against the single `ILanguageServerClient`, since this spec's trigger plumbing
  doesn't care which abstraction is behind it — it depends only on the shared `GetCompletionAsync`
  method shape either exposes). If the result is empty, no window is shown (R1.1 "if any are
  returned"). If the language has no server at all, the call site already no-ops per the session
  manager's documented behavior (multi-language-support design.md) — `CodeEditor` doesn't need its
  own "is this file LSP-backed" check (R1.3 is satisfied one layer down).
- **Rendering (R3):** wraps each `LspCompletionItem` in a small `CodeEditorCompletionData :
  ICompletionData` adapter (`Text`/`Content`/`Description` from `Label`/`Detail`; `Complete(...)`
  calls `TextArea.Document.Replace` with `InsertText ?? Label` over the completion segment
  `CompletionWindow` already tracks for you — this is exactly the pattern AvaloniaEdit's own
  samples use for `ICompletionData.Complete`, no novel logic). `_completionWindow =
  new CompletionWindow(TextArea); _completionWindow.CompletionList.CompletionData.AddRange(...);
  _completionWindow.Show();` — `CompletionWindow` handles anchoring, the dropdown chrome, ↑/↓
  navigation, and (per its own built-in behavior, to be confirmed during implementation per
  requirements.md's open question) substring filtering/auto-close-on-no-match itself (R3.2, R3.3).
  `_completionWindow.Closed += (_, _) => _completionWindow = null;` clears the field so later
  triggers know no window is active.
- **Acceptance/dismissal (R4, R5):** handled by `CompletionWindow` itself once shown — Tab/Enter/
  click calling `ICompletionData.Complete` (R4), Escape/caret-move-elsewhere closing it (R5) —
  this is exactly what the control is for; no new keydown interception needed beyond what's
  already described for opening it, confirmed against AvaloniaEdit's documented `CompletionWindow`
  behavior (same source as the ghost-text epic's own decompile-to-confirm approach where
  documentation was insufficient).
- **Ghost-text coexistence (R6):** `GhostTextDebouncer`'s existing `NotifyInput`/`ShouldFire`
  calls (Epic 15) are gated behind `_completionWindow is null` — one extra condition at each of
  the two or three call sites already checking debounce state, not a rework of the debouncer
  itself. The moment `TriggerCompletionAsync` is about to show a window, any currently-visible
  ghost-text suggestion is dismissed via the existing `GhostTextSuggestionState.Dismiss()` call
  already used elsewhere (R6.2). No new state machine — composing two already-existing pieces of
  state (`_completionWindow`'s null-ness, `GhostTextSuggestionState`) is sufficient.

## Trigger-character scope decision (open question from requirements.md)

v1 hardcodes `.` as the only trigger character across all languages (R1.1), rather than reading
each server's `initialize` response `completionProvider.triggerCharacters` (e.g.
`typescript-language-server` also advertises `"` and `'` for import-path completion, and C#
servers sometimes advertise ` ` or `(`). Rationale: a single shared trigger keeps `CodeEditor`
language-agnostic (it doesn't need to know which server is behind a file to decide whether to
fire), and `.` covers the dominant real-world case (member access) for all four target languages.
Revisit per-server trigger characters only if `.`-only proves too narrow in practice — flagged
here rather than silently decided, per this spec's own non-goals framing.

## Testing approach

- `StdioLanguageServerClientTests` (extended): `GetCompletionAsync` against the existing
  Python-script fake-server fixture, covering both the bare-array and `{ items: [...] }` response
  shapes (R2.2), and asserting `initialize`'s outgoing request now includes
  `capabilities.textDocument.completion` (R2.1).
- `CodeEditorCompletionDataTests` (new, pure unit test, no Avalonia rendering needed): given an
  `LspCompletionItem`, `Complete(...)` produces the expected document replacement, including the
  `InsertText ?? Label` fallback (R4.1).
- Ghost-text/completion coexistence (R6) is logic-testable at the `GhostTextDebouncer`
  call-site-gating level without a real window: a test asserting "when a sentinel 'completion
  active' flag is true, `ShouldFire` is never consulted" is sufficient; a true rendered-window
  interaction test is not feasible in this sandbox (no X server), same caveat every prior
  Avalonia-touching epic in `ROADMAP.md` already notes.
- No test stands up a real `pyright`/`typescript-language-server`/`csharp-ls` process for
  completion specifically — same fake-server rationale used throughout Epic 17 and
  [[multi-language-support]].
