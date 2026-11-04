# Implementation tasks — Editor intelligence depth (outline, breadcrumb, hover)

Each task references the requirement(s) it satisfies. Not started — no code written yet.

## F27.1 — LSP client additions

- [ ] T1 Add `LspDocumentSymbol`/`LspSymbolKind` DTOs; add `GetDocumentSymbolsAsync` to
  `ILanguageServerClient`/`ILanguageServerSessionManager`; implement in
  `StdioLanguageServerClient` (both flat and hierarchical response shapes, normalized to the
  single-root shape per design.md #3); add `textDocument.documentSymbol` capability advertisement.
  Confirm empirically which response shape each of `csharp-ls`/`pyright-langserver`/
  `typescript-language-server` actually returns. _(R1)_
- [ ] T2 Add `LspHover` DTO; add `GetHoverAsync` to `ILanguageServerClient`; implement in
  `StdioLanguageServerClient` (both `MarkupContent`/`MarkedString[]` response shapes); add
  `textDocument.hover` capability advertisement. _(R2)_

## F27.2 — Outline panel + breadcrumb bar

- [ ] T3 `CodeEditor`: add `DocumentSymbolsProperty`; wire a host-ViewModel refresh on file
  open/settled-edit, reusing the existing diagnostics/ghost-text debounce. _(R3)_
- [ ] T4 Outline panel: collapsible `TreeView` pane in `ExplorerFileTabControl.axaml` bound to
  `DocumentSymbols`, with its own `ResizablePaneHelper`-backed persisted width
  (`WorkspaceLayoutSettingsKeys.OutlinePanelWidth`); clicking a node raises
  `SymbolNavigationRequested`. _(R4)_
- [ ] T5 `BreadcrumbPathBuilder` (pure function, `Paarde.Avalonia/Helpers/`) computing the
  enclosing-symbol chain from `DocumentSymbols` + `CaretPosition`; `BreadcrumbBar.axaml` control
  rendering it above `CodeEditor`, each segment clickable (raises `SymbolNavigationRequested`).
  _(R5)_
- [ ] T6 Wire `SymbolNavigationRequested` (both T4 and T5's sources) through
  `ExplorerViewModel`/`OpenFileTabViewModel` to the existing caret-move/scroll-into-view path
  `CodeNavigationRequestedEventArgs`'s handling already uses. _(R4, R5)_

## F27.3 — Hover tooltips

- [ ] T7 `CodeEditor`: pointer-hover debounce raising `HoverRequestedEventArgs`;
  `HoverSuggestionProperty`; extend `DiagnosticsRenderer`'s tooltip lifecycle so a diagnostic at
  the hovered position takes priority over hover text, falling back to hover text when none
  applies. _(R6)_
- [ ] T8 `ExplorerViewModel`/`OpenFileTabViewModel`: request/store `GetHoverAsync` results the
  same way `Diagnostics`/`CompletionItems` already round-trip. _(R6)_

## F27.4 — Go to Symbol in File

- [ ] T9 `SymbolQuickPickViewModel` (new, parallel to `QuickOpenViewModel`): substring-filtered
  flattened symbol list, `Ctrl+Shift+O` keybind, second `Popup` on `ShellView.axaml` anchored the
  same way Quick Open/Command Palette already are. Confirming a result raises
  `SymbolNavigationRequested` (T6's path). _(R7)_

## F27.5 — Tests

- [ ] T10 `StdioLanguageServerClientTests` extensions for both new DTOs/response shapes and
  capability advertisement. _(validates R1, R2)_
- [ ] T11 `BreadcrumbPathBuilderTests`: enclosing-chain computation including boundary cases.
  _(validates R5)_
- [ ] T12 `SymbolQuickPickViewModelTests`: substring filtering, selection-to-navigation dispatch.
  _(validates R7)_
- [ ] T13 Manual verification in a real C# project (then repeat for Python/TypeScript per
  `[[multi-language-support]]`): outline panel populates and jumps correctly; breadcrumb updates
  live as the caret moves between methods/classes without a visible LSP round-trip delay; hovering
  a symbol shows a tooltip, and a diagnostic at the same position still wins; `Ctrl+Shift+O` opens,
  filters, and jumps correctly. _(validates AC-1 through AC-5; same sandboxed-environment
  limitation as every other Avalonia-rendering manual-verification task in this roadmap)_
