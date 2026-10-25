# Requirements — Editor autocomplete (LSP symbol/member completion)

## Status
Draft. Not started.

## Context and current state

There are two unrelated things in this codebase that could be called "autocomplete." This spec
is explicitly about the one that doesn't exist yet:

| | Epic 15 (shipped) | This spec (new) |
|---|---|---|
| Mechanism | LLM prompt (`ILlmRunner`) predicts the next few tokens | LSP `textDocument/completion` request to a real language server |
| UI | Dimmed inline "ghost text" at the caret, accepted with **Tab** | A dropdown list of candidate symbols/members, navigated with ↑/↓, accepted with **Tab/Enter** or a click |
| Trigger | Debounced (400ms) after any typing pauses, in insert mode | A trigger character (e.g. `.`) or a manual shortcut (Ctrl+Space) |
| Source of truth | Whatever the model guesses from surrounding text | The language server's actual symbol table/type information |
| Known-wrong example | Can suggest a method that doesn't exist | Only ever lists symbols the server actually knows about |

Confirmed by inspecting `CodeEditor.cs` directly: Epic 15's ghost text is the *only* completion
behavior that exists anywhere in `CodeEditor` today. There is no `textDocument/completion` call
anywhere in `StdioLanguageServerClient.cs`, and `initialize`'s `capabilities` object is empty, so
no server even knows the client could support completion. This spec closes that gap.

Confirmed by inspecting the installed `Avalonia.AvaloniaEdit` 11.4.0 package directly (string
search inside `AvaloniaEdit.dll`): AvaloniaEdit already ships a **complete, reusable** completion
UI toolkit — `CodeCompletion.CompletionWindow`, `CompletionWindowBase`, `CompletionList`,
`ICompletionData`, plus `OverloadInsightWindow`/`InsightWindow` for signature help. This is a
materially simpler starting point than Epic 15's ghost text (which had no AvaloniaEdit primitive
to build on and needed a bespoke `VisualLineElementGenerator`) or Epic 13's diff rendering (bespoke
`IBackgroundRenderer`s) — **this spec should use `CompletionWindow` directly, not build a new
control.**

## Goals

1. Real symbol/member completion (method names, properties, variables, imports/types) backed by
   whichever language server is already running for the open file, for every language that has
   one — initially C# (existing) and, once [[multi-language-support]] ships, Python and
   TypeScript/JavaScript/JSX too. This spec does not re-decide which languages have servers; it
   consumes whatever `ILanguageServerClient`/`ILanguageServerSessionManager` already has running.
2. Standard completion UX: trigger-character-based popup, manual invoke shortcut, keyboard
   navigation, Tab/Enter/click to accept, Escape or continued typing past all matches to dismiss.
3. Coexist cleanly with Epic 15's ghost text — the two must never visually overlap or fight over
   the same keystroke.

## Non-goals (explicitly out of scope for v1)

- Snippet expansion (LSP `insertTextFormat: Snippet` placeholders like `$0`/`$1`). Items that
  come back as snippets are inserted using their plain `label`/`insertText` instead, same as if
  they were a simple-text item — reduced fidelity for parameterized call snippets, acceptable for
  v1.
- Lazy item resolution (`completionItem/resolve` for additional detail/docs on highlight). Use
  only what the initial `textDocument/completion` response already contains.
- Completion-item-kind icons (method vs. field vs. class glyphs). A short text tag (e.g. "method",
  "field") in the item's description is sufficient for v1.
- Signature help (`textDocument/signatureHelp`, parameter-hint popups while typing inside a call's
  parens) — `OverloadInsightWindow`/`InsightWindow` exist in AvaloniaEdit for this and are a
  natural follow-up, but are not part of this spec.
- Tutorial's proposal editor — same scope boundary Epic 17 drew for LSP features generally (no
  stable on-disk file identity to key a server session on there). Explorer's `CodeEditor` only.

## Requirements

### R1 — Trigger

**User story:** As a user typing code, I want a completion list to appear automatically after
typing a character like `.`, or on demand via a shortcut, so that I can discover and insert
symbols without typing them in full.

Acceptance criteria:
1. WHEN the user types a trigger character for the current file's language (`.` for C#, Python,
   and TypeScript/JavaScript/JSX — the one trigger character all four share) THE SYSTEM SHALL
   request completions at the caret position and, if any are returned, show them in a dropdown.
2. WHEN the user presses the manual-invoke shortcut (Ctrl+Space) at any caret position in an
   LSP-backed file THE SYSTEM SHALL request completions at that position regardless of the
   preceding character.
3. IF the file's language has no running/available language server THEN THE SYSTEM SHALL NOT
   attempt a completion request and SHALL NOT show any dropdown — pressing Ctrl+Space in an
   unsupported file is a silent no-op, not an error.
4. THE SYSTEM SHALL request completions exactly once per trigger (R1.1 or R1.2) rather than
   re-querying the server on every subsequent keystroke; narrowing the already-returned list as
   the user keeps typing is handled client-side (R3.3), not by re-querying.

### R2 — Server capability advertisement (shared with [[multi-language-support]] R5)

**User story:** As a developer, I need the LSP client to tell servers it supports completion, so
that servers actually return results instead of silently ignoring the request.

Acceptance criteria:
1. WHEN `StdioLanguageServerClient` sends `initialize` THE SYSTEM SHALL include a
   `capabilities.textDocument.completion` entry (minimally
   `{ "completionItem": { "snippetSupport": false } }`, reflecting the snippet non-goal above).
2. THE SYSTEM SHALL add a `GetCompletionAsync(filePath, position)` method to
   `ILanguageServerClient`, issuing a `textDocument/completion` request and parsing either an
   LSP `CompletionItem[]` or a `CompletionList { items: CompletionItem[], isIncomplete: bool }`
   response shape (the LSP spec allows a server to return either) into a single uniform
   `IReadOnlyList<LspCompletionItem>` (new small DTO: `Label`, `Detail`, `InsertText`, `Kind`).

### R3 — Completion list UI

**User story:** As a user, I want the completion list to look and behave like completion lists in
other editors, so that it's immediately familiar.

Acceptance criteria:
1. THE SYSTEM SHALL render the returned items using AvaloniaEdit's existing
   `CodeCompletion.CompletionWindow`/`CompletionList`/`ICompletionData`, anchored at the trigger
   caret position — not a new bespoke popup control.
2. THE SYSTEM SHALL support ↑/↓ to move the highlighted item and mouse click to select one,
   relying on `CompletionWindow`'s built-in list/selection behavior.
3. WHEN the user continues typing after the list is shown THE SYSTEM SHALL narrow the visible
   items to those matching what's been typed since the trigger, using `CompletionList`'s built-in
   filtering, and SHALL close the window automatically if no items match — no new filtering logic
   needs to be written if `CompletionWindow`'s default behavior already does this (confirm in
   design/implementation; write a small adapter only if it doesn't).
4. THE SYSTEM SHALL show each item's `Detail` (e.g. a method signature or type) alongside its
   `Label`, using whatever secondary-text slot `ICompletionData`/`CompletionList`'s default item
   template exposes.

### R4 — Acceptance

**User story:** As a user, I want to accept a highlighted completion with a single keystroke, so
that completing a symbol is as fast as typing it would be slow.

Acceptance criteria:
1. WHEN the completion window is open and the user presses Tab or Enter THE SYSTEM SHALL insert
   the highlighted item's `InsertText` (falling back to `Label` if no distinct `InsertText` was
   returned) at the trigger position, replacing any partially-typed filter text (R3.3), and close
   the window.
2. WHEN the user double-clicks or single-clicks (per `CompletionWindow`'s default interaction
   model — confirm exact click behavior during implementation) an item THE SYSTEM SHALL accept it
   identically to R4.1.
3. AFTER accepting an item THE SYSTEM SHALL leave the caret positioned immediately after the
   inserted text, ready for continued typing (e.g. to type `(` after accepting a method name).

### R5 — Dismissal

**User story:** As a user, I want to dismiss a completion list I don't want without it getting in
my way, so that it never feels sticky or forces a choice.

Acceptance criteria:
1. WHEN the completion window is open and the user presses Escape THE SYSTEM SHALL close it
   without inserting anything, leaving the document exactly as it was.
2. WHEN the completion window is open and the user moves the caret by a means other than typing
   matching characters (e.g. clicking elsewhere in the document, pressing an arrow key that
   AvaloniaEdit treats as caret movement rather than list navigation, or switching files/tabs)
   THE SYSTEM SHALL close the window without inserting anything.

### R6 — Coexistence with ghost text (Epic 15)

**User story:** As a user, I don't want the ghost-text suggestion and the completion dropdown
fighting for the same screen space or the same Tab keystroke, so that the editor behaves
predictably.

Acceptance criteria:
1. WHEN a completion dropdown (this spec) is open THE SYSTEM SHALL suspend Epic 15's ghost-text
   debounce/trigger entirely — no ghost-text suggestion shall be requested or rendered while the
   dropdown is open.
2. WHEN both a ghost-text suggestion and a trigger for this spec's dropdown would otherwise fire
   from the same keystroke THE SYSTEM SHALL give the completion dropdown precedence (it is
   server-verified; ghost text is a guess) — the ghost-text suggestion, if any was already
   showing, SHALL be dismissed the moment the completion dropdown opens.
3. WHEN the completion dropdown closes (acceptance or dismissal, R4/R5) THE SYSTEM SHALL resume
   normal ghost-text behavior on subsequent keystrokes exactly as if the dropdown had never
   opened — no lingering suppression.

### R7 — Scope boundary

**User story:** As a developer, I need this feature scoped the same way Epic 17 was, so I don't
accidentally take on Tutorial's proposal-editor complexity.

Acceptance criteria:
1. THE SYSTEM SHALL implement this feature in Explorer's `CodeEditor` usage only.
2. THE SYSTEM SHALL NOT implement LSP-backed completion in Tutorial's proposal editor
   (`TutorialDetailControl`'s `Editor`) in this pass — same rationale Epic 17 already documented
   (no stable on-disk file identity for a language server to track there).

## Open questions for design phase

- Whether `CompletionList`'s default substring filter is "good enough" or whether it needs a
  thin wrapper for fuzzy/prefix matching closer to what users expect from VS Code-style
  completion — confirm by exercising the real control during implementation (R3.3's
  "confirm in design/implementation" note).
- Exact trigger-character set per language beyond the shared `.` — e.g. whether TypeScript's
  `typescript-language-server` advertises additional server-defined trigger characters via
  `initialize`'s response (`completionProvider.triggerCharacters`) that should be honored
  per-language rather than hardcoding just `.` — see design.md.
