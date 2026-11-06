# Design — Editor intelligence depth (outline, breadcrumb, hover)

## Summary of architectural change

Two new LSP request types (`documentSymbol`, `hover`) are added at the same layer
`GetDefinitionAsync`/`GetCompletionAsync` already live at - no new transport, process-management,
or per-language routing concerns, since `ILanguageServerSessionManager` already handles "one
process per language, routed by file extension" for every existing request type. Everything above
that layer (outline panel, breadcrumb, hover tooltip, symbol quick-pick) is new Avalonia UI
following patterns Epics 17/19/24 already established.

## New/changed types

### 1. `LspDocumentSymbol` (new, `Paarde.Core/Models/`)

```csharp
public sealed record LspDocumentSymbol(
    string Name,
    LspSymbolKind Kind,
    LspRange Range,
    LspRange SelectionRange,
    IReadOnlyList<LspDocumentSymbol> Children);
```

`LspSymbolKind` mirrors LSP's numeric `SymbolKind` enum (class, method, property, etc.) - a thin
enum, not reused from any UI icon type, so `Paarde.Core` stays UI-framework-agnostic (same
separation `LspDiagnostic`/`LspCompletionItem` already keep).

### 2. `LspHover` (new, `Paarde.Core/Models/`)

```csharp
public sealed record LspHover(string Contents, LspRange? Range);
```

`Contents` is pre-flattened to a single string at the DTO layer (joining LSP's `MarkupContent`/
`MarkedString[]` variants) - `CodeEditor` renders it as plain/wrapped text in a tooltip, not
rendered Markdown, so there is no reason to carry the raw union type past the client boundary.

### 3. `ILanguageServerClient`/`StdioLanguageServerClient`/`ILanguageServerSessionManager` additions

```csharp
Task<LspDocumentSymbol?> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default);
Task<LspHover?> GetHoverAsync(string filePath, LspPosition position, CancellationToken cancellationToken = default);
```

`GetDocumentSymbolsAsync` returns a single synthetic root `LspDocumentSymbol` (name = file name,
`Children` = the server's actual top-level symbols) rather than `IReadOnlyList<LspDocumentSymbol>`,
so the outline `TreeView` and the breadcrumb computation (design #6 below) both walk one shape
instead of special-casing "no parent at the top level." `StdioLanguageServerClient`'s parsing
normalizes a flat `SymbolInformation[]` response into this nested shape by reconstructing
containment from each entry's `ContainerName` string match against prior entries' `Name` - a
best-effort reconstruction, not guaranteed as precise as a server that returns hierarchical
`DocumentSymbol[]` natively, but acceptable since R8's tests cover both shapes and no UI
requirement depends on perfect containment for the flat-response case.

`initialize`'s capabilities gain `textDocument.documentSymbol.hierarchicalDocumentSymbolSupport =
true` and `textDocument.hover.contentFormat = ["markdown", "plaintext"]`, alongside the existing
per-method capability block Epic 19 established.

### 4. `CodeEditor` changes

- `DocumentSymbolsProperty` (`StyledProperty<LspDocumentSymbol?>` - singular root, not a list, per
  #3 above).
- Pointer-hover handling: subscribes to `TextView.PointerHovered`/`PointerHoverStopped` (the
  AvaloniaEdit hooks `DiagnosticsRenderer` already uses internally for its own tooltip), raising a
  new `HoverRequestedEventArgs(LspPosition)` event on a short debounce (same debounce constant
  `GhostTextDebouncer` already uses, reused rather than a third timer).
- `HoverSuggestionProperty` (`StyledProperty<LspHover?>`) - host ViewModel fills this in after
  `GetHoverAsync` returns; `DiagnosticsRenderer` is extended to check "is there a diagnostic at
  this position" first and only fall back to rendering `HoverSuggestion`'s tooltip if not -
  preserves R6's "diagnostics win" priority without a second tooltip widget competing for the same
  screen position.
- A new `SymbolNavigationRequested` event (`LspPosition`) - raised by both the outline panel (R4)
  and the breadcrumb bar (R5) when a segment/node is clicked, consumed by
  `ExplorerViewModel`/`OpenFileTabViewModel` through the exact same caret-move/scroll path
  `CodeNavigationRequestedEventArgs`'s go-to-definition handling already uses, so there is exactly
  one "move caret to an LSP position and scroll it into view" implementation, not three.

### 5. Outline panel + breadcrumb bar UI

- `ExplorerFileTabControl.axaml` gains a collapsible left-edge pane (own
  `WorkspaceLayoutSettingsKeys.OutlinePanelWidth`, same `ResizablePaneHelper` convention as every
  other resizable pane in this app) bound to the tab's `DocumentSymbols`, rendered via `TreeView`
  with a `TreeDataTemplate` recursing on `Children`.
- A new thin `BreadcrumbBar.axaml` control sits above `CodeEditor` inside
  `ExplorerFileTabControl.axaml`, bound to the same `DocumentSymbols` plus `CodeEditor.
  CaretPosition`. Its symbol-chain computation (`DocumentSymbols` + caret line/column → ordered
  list of enclosing symbols) is a pure function (`BreadcrumbPathBuilder.Build(root, caretPosition)`
  in `Paarde.Avalonia/Helpers/`) kept separate from the control itself specifically so R8's test
  can exercise it without a live `TextArea`.

### 6. `SymbolQuickPickViewModel` (new, parallel to `QuickOpenViewModel`)

Same shape as `QuickOpenViewModel`: a `Query` string, substring-filtered `Results` over the active
tab's flattened `DocumentSymbols` (flattening the nested tree into a single list with each
display label prefixed by its parent chain, e.g. `"MyClass.MyMethod"`), `SelectedResult`, and
`IsOpen`. `ShellView.axaml` gains a second `Popup` for it (same `PlacementTarget="NavigationFrame"`
anchor Quick Open/Command Palette both already use), opened by a new `Ctrl+Shift+O` `KeyBinding`.
Confirming a result raises the same `SymbolNavigationRequested` path as the outline panel.

## Testing approach

- `StdioLanguageServerClientTests` extensions: `documentSymbol` response parsing for both
  flat/hierarchical shapes; `hover` response parsing for both `MarkupContent`/`MarkedString[]`
  shapes; capability advertisement assertions for both new methods - same test style as the
  existing `GetCompletionAsync` tests.
- `BreadcrumbPathBuilderTests` (new, pure function, no Avalonia dependency): given a synthetic
  `LspDocumentSymbol` tree and a caret position, asserts the correct enclosing-symbol chain,
  including boundary cases (caret exactly on a symbol's opening/closing line, caret outside every
  symbol's range).
- `SymbolQuickPickViewModelTests` (new): substring filtering and selection-to-navigation dispatch,
  same shape as the existing `QuickOpenViewModelTests`/`CommandPaletteViewModelTests`.
- No automated test can exercise the live outline `TreeView`, breadcrumb bar rendering, or hover
  tooltip's actual on-screen appearance/timing - same sandboxed-environment limitation this repo
  has documented for every other Avalonia-rendering feature. Covered by code inspection plus a
  manual-verification task in `tasks.md`.

## Open questions

- **Flat-response containment reconstruction (design #3) is best-effort, not exact.** Accepted as
  a known limitation rather than solved precisely - no currently-configured language server in
  this app (`csharp-ls`, `pyright-langserver`, `typescript-language-server`) is known to return the
  flat `SymbolInformation[]` shape instead of hierarchical `DocumentSymbol[]`; confirm this
  empirically during T1 and only invest further in the flat-shape reconstruction if one of them
  actually does.
- **Should the outline panel auto-expand to the node containing the caret?** Left as an
  implementation-time call, not specified here - a reasonable default (VS Code does this) but not
  load-bearing for any acceptance criterion above.
