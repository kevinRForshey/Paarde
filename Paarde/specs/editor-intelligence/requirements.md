# Requirements — Editor intelligence depth (outline, breadcrumb, hover)

## Status

Draft. Not started. This is Epic 27 (`ROADMAP.md`) — referenced elsewhere in this repo as
`[[editor-intelligence]]`. Independent of `[[interface-mode]]` (Epic 26) - this epic only touches
`CodeEditor`/`ILanguageServerClient` and Explorer's per-tab UI, which exist identically in both
interface modes. Builds directly on the per-language LSP session infrastructure Epics 17-19
already shipped.

## Context and current state

Confirmed by direct inspection, 2026-11-03:

- `ILanguageServerClient` (`Paarde.Core/Contracts/Services/`) today exposes: diagnostics push
  (`DiagnosticsPublished`), `GetDefinitionAsync`, `GetReferencesAsync`, `GetCompletionAsync`. No
  `textDocument/documentSymbol` or `textDocument/hover` request exists anywhere in the codebase.
- `CodeEditor.cs` (`Paarde.Avalonia/Controls/`) already has the exact event+property round-trip
  shape this epic's new features need: `CodeNavigationRequestedEventArgs` (F12/Shift+F12 →
  go-to-definition/find-references), `CompletionRequestedEventArgs` (trigger char/Ctrl+Space),
  and null-tolerant opt-in `StyledProperty`s (`DiagnosticsProperty`, `CompletionItemsProperty`,
  `GhostTextSuggestionProperty`) that a host ViewModel fills in after an async round trip. New
  outline/hover data follows the identical pattern, not a new one.
- `DiagnosticsRenderer` already tracks `_shownDiagnosticTooltip` - a tooltip-show/hide lifecycle
  keyed to pointer position, specific to diagnostics today. This is the existing mechanism to
  extend for hover text, not a second independent tooltip system.
- `ExplorerViewModel`/`OpenFileTabViewModel` own the per-tab round trip for
  `Diagnostics`/`CompletionItems`/`GhostTextSuggestion` (request on a debounce, store the result,
  bind it back to the active tab's `CodeEditor`). Outline/hover follow the same per-tab-property
  convention.
- Epic 24's Quick Open (`QuickOpenViewModel`, a `Popup`/`ListBox` anchored to `NavigationFrame`,
  substring-filtered via `FileMentionMatcher`) is the existing "lightweight filterable popup"
  shape this epic's own symbol quick-pick reuses rather than building a second popup mechanism.
- `StatusBarViewModel` already surfaces `CaretPositionText` from `CodeEditor.CaretPosition` -
  precedent for a status/breadcrumb-style readout driven off the caret.

## Goals

- Give the editor awareness of what's in the current file and under the cursor, on top of the LSP
  session infrastructure already running per language (Epics 17-19).
- **Document outline**: a panel listing the active file's symbols (classes/methods/properties,
  nested), click-to-jump.
- **Breadcrumb bar**: file path segments plus the chain of symbols (e.g. `Namespace > Class >
  Method`) enclosing the caret, updating live as the caret moves, each segment clickable.
- **Hover tooltips**: hovering a symbol shows its type signature/doc comment from the language
  server, not only diagnostics.
- **Go to Symbol in File** (`Ctrl+Shift+O`): reuses Quick Open's popup shape over the same outline
  data for fast symbol jumping without the mouse.

## Non-goals

- No workspace-wide symbol search ("Go to Symbol in Workspace") - scoped to the active file's own
  outline only, same single-file scope `GetDefinitionAsync`/`GetReferencesAsync` already have
  (cross-file *results* from those are fine; a workspace-wide symbol *index* is not being built
  here).
- No semantic highlighting / semantic tokens.
- No change to ghost text or completion behavior - purely additive new LSP requests alongside them.
- No custom minimap. AvaloniaEdit has no built-in minimap support; a correct one (synchronized
  scroll, click-to-navigate, rendered-text thumbnailing) is a large bespoke rendering effort for
  comparatively low value against this epic's other goals, and is explicitly out of scope.

## Requirements

1. **R1 — `LspDocumentSymbol` DTO + `GetDocumentSymbolsAsync`.** New
   `LspDocumentSymbol` (`Paarde.Core/Models/`): `Name`, `Kind`, `Range`, `SelectionRange`, nested
   `Children` (mirrors LSP's hierarchical `DocumentSymbol` shape). New
   `GetDocumentSymbolsAsync(string filePath, CancellationToken)` on `ILanguageServerClient`/
   `ILanguageServerSessionManager`, implemented in `StdioLanguageServerClient` via
   `textDocument/documentSymbol`, advertised in the `initialize` request's capabilities (same
   per-method capability-advertisement convention Epic 19 used for completion). Must tolerate
   both response shapes a server may return - flat `SymbolInformation[]` and hierarchical
   `DocumentSymbol[]` - normalizing both into the same nested `LspDocumentSymbol` shape at the DTO
   layer (same "tolerate either shape" precedent `GetCompletionAsync`'s array-vs-`CompletionList`
   parsing already set).
2. **R2 — `LspHover` DTO + `GetHoverAsync`.** New `LspHover` (`Paarde.Core/Models/`): `Contents`
   (markdown or plaintext, whichever the server returned) and an optional `Range`. New
   `GetHoverAsync(string filePath, LspPosition position, CancellationToken)` on
   `ILanguageServerClient`, implemented via `textDocument/hover`, with its own capability
   advertisement.
3. **R3 — `CodeEditor` outline plumbing.** New `DocumentSymbolsProperty`
   (`StyledProperty<IReadOnlyList<LspDocumentSymbol>?>`, same null-tolerant opt-in convention as
   `DiagnosticsProperty`/`CompletionItemsProperty`) on `CodeEditor`. A host ViewModel requests a
   refresh whenever a file opens or its content settles after edits, reusing the existing
   diagnostics/ghost-text debounce rather than introducing a second one.
4. **R4 — Outline panel UI.** New collapsible pane (in `ExplorerFileTabControl.axaml`, alongside
   the existing editor) rendering the active tab's `DocumentSymbols` as a nested `TreeView`.
   Clicking a node moves the caret to that symbol's `SelectionRange` and scrolls it into view,
   reusing the same caret-move/scroll-into-view path `GoToDefinitionRequested`'s jump already uses.
5. **R5 — Breadcrumb bar.** New thin control above `CodeEditor` showing the open file's relative
   path, plus the chain of symbols whose range contains the caret - computed client-side from
   `DocumentSymbols` + `CaretPosition` (no extra LSP round trip per caret move). Each segment is
   clickable and jumps to that symbol's own start (same jump path as R4).
6. **R6 — Hover tooltips.** `CodeEditor`'s pointer-hover handling debounces a `GetHoverAsync` call
   at the hovered position and shows the result in a tooltip, extending `DiagnosticsRenderer`'s
   existing `_shownDiagnosticTooltip` show/hide lifecycle rather than adding a second,
   independent tooltip mechanism. If a diagnostic and hover text would both show at the same
   position, the diagnostic tooltip takes priority (matches the existing precedent that
   diagnostics are the higher-priority, already-shipped signal).
7. **R7 — Go to Symbol in File.** New `Ctrl+Shift+O` keybind opening a popup styled exactly like
   Quick Open's (reusing its `Popup`/`ListBox`/substring-filter shape via a new
   `SymbolQuickPickViewModel` parallel to `QuickOpenViewModel`) over the active tab's
   `DocumentSymbols`. Selecting an entry jumps the caret (R4's jump path).
8. **R8 — Tests.** `StdioLanguageServerClient` response-shape parsing tests for both new DTOs
   (including both hierarchical/flat symbol shapes); a pure-function test for the
   `DocumentSymbols` + `CaretPosition` → breadcrumb-chain computation (no live `TextArea` needed -
   same sandbox limitation precedent as other `CodeEditor` logic that can only be verified by
   direct computation rather than a live control); substring-filter tests for the new quick-pick
   (reusing the `FileMentionMatcher` pattern Quick Open itself reused).

## Acceptance criteria

- **AC-1**: Given a C# file open in Explorer with a running `csharp-ls` session, the outline panel
  lists its classes/methods nested correctly, and clicking a method moves the caret to its
  signature.
- **AC-2**: Given the caret is inside a method body, the breadcrumb bar shows
  `<file> > <namespace> > <class> > <method>`; moving the caret into a different method updates the
  last segment without an LSP round trip.
- **AC-3**: Given the caret rests over a symbol for the hover debounce window, a tooltip appears
  showing that symbol's hover text; if a diagnostic also applies at that position, the diagnostic
  tooltip is what's shown.
- **AC-4**: `Ctrl+Shift+O` opens a popup listing the active file's symbols, filterable by typing,
  and selecting one jumps the caret the same way clicking it in the outline panel would.
- **AC-5**: All of the above work (once implemented for the corresponding language server) for
  Python and TypeScript files too, per `[[multi-language-support]]`'s existing per-language
  session isolation - no change needed to that isolation to support this epic.
