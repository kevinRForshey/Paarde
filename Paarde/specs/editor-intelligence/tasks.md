# Implementation tasks — Editor intelligence depth (outline, breadcrumb, hover)

Each task references the requirement(s) it satisfies. T1-T12 done; T13 (manual verification) left
unchecked - no X server or real language servers available in this sandbox.

## F27.1 — LSP client additions

- [x] T1 Add `LspDocumentSymbol`/`LspSymbolKind` DTOs; add `GetDocumentSymbolsAsync` to
  `ILanguageServerClient`/`ILanguageServerSessionManager`; implement in
  `StdioLanguageServerClient` (both flat and hierarchical response shapes, normalized to the
  single-root shape per design.md #3); add `textDocument.documentSymbol` capability advertisement.
  Confirm empirically which response shape each of `csharp-ls`/`pyright-langserver`/
  `typescript-language-server` actually returns. _(R1)_
  Done: `Paarde.Core/Models/LspRange.cs`, `LspSymbolKind.cs`, `LspDocumentSymbol.cs` added;
  `GetDocumentSymbolsAsync` added to both interfaces and `LanguageServerSessionManager`/
  `StdioLanguageServerClient`. Shape detection (`ParseDocumentSymbols`) checks whether the first
  response entry has its own `"range"` (hierarchical `DocumentSymbol[]`) vs. only a nested
  `"location"."range"` (flat `SymbolInformation[]`), per design.md #3. Deviation: did not
  independently verify each real language server's actual response shape against a live server in
  this sandbox (no network/installed language servers available) — relying on design.md's own
  documented assumption that no currently-configured server returns the flat shape instead of
  hierarchical; `ParseFlatSymbols`'s best-effort containerName-based nesting exists and is covered
  by T10's tests regardless, so the flat path isn't unexercised, just unconfirmed against a real
  server.
- [x] T2 Add `LspHover` DTO; add `GetHoverAsync` to `ILanguageServerClient`; implement in
  `StdioLanguageServerClient` (both `MarkupContent`/`MarkedString[]` response shapes); add
  `textDocument.hover` capability advertisement. _(R2)_
  Done: `Paarde.Core/Models/LspHover.cs` added; `GetHoverAsync` added to both interfaces and
  `LanguageServerSessionManager`/`StdioLanguageServerClient`. `ParseHover`/`FlattenHoverContents`/
  `FlattenMarkedString` normalize every LSP `contents` union variant (bare `MarkedString`,
  `{language, value}`, `MarkupContent`, or an array of any of those) to a single string, joining
  array entries with a blank line. All four test fakes
  (`Paarde.Core.Tests`/`Paarde.Avalonia.Tests` `FakeLanguageServerClient`/
  `FakeLanguageServerSessionManager`) updated with `DocumentSymbolsToReturn`/`HoverToReturn` stubs.
  Full solution builds with 0 warnings/0 errors; existing 537 Core + 399 Avalonia tests pass
  unchanged.

## F27.2 — Outline panel + breadcrumb bar

- [x] T3 `CodeEditor`: add `DocumentSymbolsProperty`; wire a host-ViewModel refresh on file
  open/settled-edit, reusing the existing diagnostics/ghost-text debounce. _(R3)_
  Done: `DocumentSymbolsProperty` + a `DocumentSymbolsRequested` event added to `CodeEditor` -
  raised once immediately when `FilePath` is first set, then again after every settled edit via a
  second `GhostTextDebouncer` instance driven by the same existing `_ghostTextTimer` tick (no new
  timer). `OpenFileTabViewModel.DocumentSymbols` + `RequestDocumentSymbolsAsync()` added (same
  round-trip shape as `RequestCompletionAsync`); wired in `ExplorerFileTabControl.axaml.cs`'s
  constructor and bound in `ExplorerFileTabControl.axaml`.
- [x] T4 Outline panel: collapsible `TreeView` pane in `ExplorerFileTabControl.axaml` bound to
  `DocumentSymbols`, with its own `ResizablePaneHelper`-backed persisted width
  (`WorkspaceLayoutSettingsKeys.OutlinePanelWidth`); clicking a node raises
  `SymbolNavigationRequested`. _(R4)_
  Done: `ExplorerFileTabControl.axaml`'s row 4 is now a `Grid` (`OutlinePanelGrid`) with the outline
  `TreeView` (bound to `DocumentSymbols.Children`, `TreeDataTemplate` recursing on `Children`), a
  `GridSplitter`, then `Editor`; width persisted via a new `_outlinePaneHelper`
  (`WorkspaceLayoutSettingsKeys.OutlinePanelWidth`), same construction pattern as
  `ExplorerView.axaml.cs`'s `_paneHelper`. Deviation: no `CodeEditor`-owned
  `SymbolNavigationRequested` event - the outline panel isn't a child of `CodeEditor` and can't
  raise an event `CodeEditor` itself declares, so `OnOutlineSymbolSelected` in
  `ExplorerFileTabControl.axaml.cs`'s code-behind calls `ExplorerViewModel.NavigateToLocation` +
  `ApplyPendingCaretTarget()` directly instead - the exact same "one caret-jump implementation"
  path `OnReferenceSelected` already uses, just reached via a direct code-behind call rather than
  a relayed `CodeEditor` event. T5's breadcrumb bar will use the same direct-call shape.
- [x] T5 `BreadcrumbPathBuilder` (pure function, `Paarde.Avalonia/Helpers/`) computing the
  enclosing-symbol chain from `DocumentSymbols` + `CaretPosition`; `BreadcrumbBar.axaml` control
  rendering it above `CodeEditor`, each segment clickable (raises `SymbolNavigationRequested`).
  _(R5)_
  Done: `BreadcrumbPathBuilder.Build(root, caretPosition)` added
  (`Paarde.Avalonia/Helpers/BreadcrumbPathBuilder.cs`) - walks `root.Children` picking whichever
  sibling's `Range` contains the caret (inclusive of both endpoints), descending until none
  matches. `BreadcrumbBar.axaml`/`.axaml.cs` added (`Paarde.Avalonia/Views/`) - a self-contained
  `UserControl` (`DataContext = this`) with `DocumentSymbolsProperty`/`CaretPositionProperty`
  inputs and a computed `SegmentsProperty`, rendered as clickable `Button`s via an `ItemsControl`;
  wired above `Editor` in `ExplorerFileTabControl.axaml`, bound to the tab's `DocumentSymbols` and
  `Editor.CaretPosition` directly (`{Binding #Editor.CaretPosition}`). Deviation: raises its own
  `SegmentClicked` event rather than a `CodeEditor`-owned `SymbolNavigationRequested` - same
  reason/same resolution as T4's deviation note.
- [x] T6 Wire `SymbolNavigationRequested` (both T4 and T5's sources) through
  `ExplorerViewModel`/`OpenFileTabViewModel` to the existing caret-move/scroll-into-view path
  `CodeNavigationRequestedEventArgs`'s handling already uses. _(R4, R5)_
  Done: accomplished as part of T4/T5 themselves - `OnOutlineSymbolSelected` and
  `OnBreadcrumbSegmentClicked` (both in `ExplorerFileTabControl.axaml.cs`) each call
  `ExplorerViewModel.NavigateToLocation` + `ApplyPendingCaretTarget()` directly, the identical path
  `OnReferenceSelected`'s go-to-definition handling already uses - exactly one "move caret to an
  LSP position and scroll it into view" implementation, per design.md's requirement, just reached
  by a direct code-behind call in both new cases rather than a relayed `CodeEditor` event.

## F27.3 — Hover tooltips

- [x] T7 `CodeEditor`: pointer-hover debounce raising `HoverRequestedEventArgs`;
  `HoverSuggestionProperty`; extend `DiagnosticsRenderer`'s tooltip lifecycle so a diagnostic at
  the hovered position takes priority over hover text, falling back to hover text when none
  applies. _(R6)_
  Done: `HoverRequestedEventArgs`/`HoverSuggestionProperty`/`HoverRequested` event added;
  `OnPointerMovedForDiagnostics` replaced by `OnPointerMovedForHover`, which resolves the
  diagnostic-vs-hover priority at the hovered offset (R6) - a diagnostic at that offset always
  hides/cancels any hover tooltip/debounce and wins (`ShowDiagnosticTooltip`, extracted from the
  old handler); otherwise a new `_hoverDebouncer` (`GhostTextDebouncer`, 400ms, same existing
  `_ghostTextTimer` tick via `OnHoverTimerTick`, no new timer) fires `HoverRequested` once the
  pointer settles on a new offset. `OnHoverSuggestionChanged`/`ShowHoverTooltip`/
  `HideHoverTooltip` mirror the existing diagnostic-tooltip lifecycle and
  `OnGhostTextSuggestionChanged`'s staleness check (`_pendingHoverRequestOffset` vs.
  `_currentHoverOffset`). Deviation: design.md describes extending `DiagnosticsRenderer`'s own
  tooltip lifecycle (implying it already hooks `TextView.PointerHovered`/`PointerHoverStopped`
  internally) - confirmed by reading `DiagnosticsRenderer.cs` that it does not; the actual
  diagnostic tooltip has always lived in `CodeEditor`'s own `PointerMoved`/`PointerExited`
  subscriptions, so T7 extends that existing mechanism directly rather than introducing new
  `TextView` hover hooks. Scope decision: the hover tooltip isn't invalidated on text changes
  (typing) - hover is inherently pointer-driven/transient and the next pointer move already
  recomputes it, so adding change-driven invalidation would be unused complexity (YAGNI).
- [x] T8 `ExplorerViewModel`/`OpenFileTabViewModel`: request/store `GetHoverAsync` results the
  same way `Diagnostics`/`CompletionItems` already round-trip. _(R6)_
  Done: `OpenFileTabViewModel.HoverSuggestion` + `RequestHoverAsync(LspPosition)` added (same
  null-then-set round-trip shape `RequestCompletionAsync` already uses, so a bound
  `HoverSuggestionProperty.Changed` handler always re-fires even if the new hover text happens to
  equal the previous one); wired via `Editor.HoverRequested` in `ExplorerFileTabControl.axaml.cs`'s
  constructor and bound (`HoverSuggestion="{Binding HoverSuggestion}"`) on `Editor` in
  `ExplorerFileTabControl.axaml`. No `ExplorerViewModel` changes needed - hover, like completion,
  is entirely within one tab's own editor and doesn't need to reach across tabs.

## F27.4 — Go to Symbol in File

- [x] T9 `SymbolQuickPickViewModel` (new, parallel to `QuickOpenViewModel`): substring-filtered
  flattened symbol list, `Ctrl+Shift+O` keybind, second `Popup` on `ShellView.axaml` anchored the
  same way Quick Open/Command Palette already are. Confirming a result raises
  `SymbolNavigationRequested` (T6's path). _(R7)_
  Done: `SymbolQuickPickViewModel` added (`Paarde.Avalonia/ViewModels/`) - a DI singleton (same
  shape as `QuickOpenViewModel`/`CommandPaletteViewModel`), filtering a flattened symbol list by
  substring match on `Name`. `ShellViewModel`/`ShellView.axaml`/`ShellView.axaml.cs` extended with
  a third popup + `Ctrl+Shift+O` `KeyBinding`, identical shape to the other two popups. Deviation:
  `IActiveEditorContext` extended with `DocumentChanged`/`SetActiveDocument` and
  `NavigationRequested`/`RequestNavigation` - `SymbolQuickPickViewModel` is a DI singleton that
  outlives any single navigation, so (same reasoning `StatusBarViewModel` already documents for
  its own caret readout) it can't hold a live `ExplorerViewModel` reference (transient, one per
  page navigation) to read `DocumentSymbols` from or to reuse T6's `NavigateToLocation`/
  `TakePendingCaretTarget` path. `ExplorerFileTabControl.axaml.cs` reports its own
  `FilePath`/`DocumentSymbols` to `IActiveEditorContext` whenever its `Editor` is focused (mirrors
  the existing `SetCaretPosition` reporting exactly) and subscribes to `NavigationRequested` only
  while focused; since Go to Symbol only ever targets the already-focused file, the handler
  (`JumpCaretTo`) moves the caret directly rather than going through `NavigateToLocation`'s
  cross-file open-or-activate path, which only matters when a jump might target a different,
  not-yet-open tab.

## F27.5 — Tests

- [x] T10 `StdioLanguageServerClientTests` extensions for both new DTOs/response shapes and
  capability advertisement. _(validates R1, R2)_
  Done: the fake LSP server script extended with `textDocument/documentSymbol` (discriminated by
  a `flat-symbols.cs`/otherwise filename suffix, since the request carries no other test-chosen
  marker) and `textDocument/hover` (discriminated by `position.character`, the same trick the
  existing completion handler already uses) handlers, each returning both response-shape variants.
  Six new tests added: hierarchical-shape single-root wrapping, flat-shape containerName-based
  nesting reconstruction, `MarkupContent` hover, `MarkedString[]`-with-a-bare-string-entry hover
  flattening, and one capability-advertisement test asserting
  `hierarchicalDocumentSymbolSupport`/`hover.contentFormat`. 17/17 passing in this test class, 542
  total in `Paarde.Core.Tests` with no regressions.
- [x] T11 `BreadcrumbPathBuilderTests`: enclosing-chain computation including boundary cases.
  _(validates R5)_
  Done: 8 tests covering null root, null caret position, caret outside every symbol's range, a
  full nested enclosing chain, caret exactly on a symbol's opening/closing position (both inside,
  per `Contains`'s inclusive-both-endpoints check), one position past a symbol's closing position
  (outside), and picking the correct sibling when only one of several contains the caret. All
  passing, no Avalonia dependency (pure function test).
- [x] T12 `SymbolQuickPickViewModelTests`: substring filtering, selection-to-navigation dispatch.
  _(validates R7)_
  Done: 8 tests via `FakeActiveEditorContext`, covering no-active-document (`EmptyStateMessage`,
  no `ExplorerViewModel` dependency needed), full flattened population on open, substring
  filtering (case-insensitive), no-matches empty state, `ActivateSelected` dispatching
  `RequestNavigation` with the selected symbol's `SelectionRange.Start` and closing the popup,
  nothing-selected no-op, `Close`, and an active-document change received while closed still
  reflecting on the next open. 415 total in `Paarde.Avalonia.Tests` with no regressions.
- [ ] T13 Manual verification in a real C# project (then repeat for Python/TypeScript per
  `[[multi-language-support]]`): outline panel populates and jumps correctly; breadcrumb updates
  live as the caret moves between methods/classes without a visible LSP round-trip delay; hovering
  a symbol shows a tooltip, and a diagnostic at the same position still wins; `Ctrl+Shift+O` opens,
  filters, and jumps correctly. _(validates AC-1 through AC-5; same sandboxed-environment
  limitation as every other Avalonia-rendering manual-verification task in this roadmap)_
  Not done: no X server or real language servers (csharp-ls/pyright-langserver/
  typescript-language-server) available in this sandbox to render Avalonia UI or exercise a real
  LSP round-trip - same limitation every other manual-verification task in this roadmap already
  notes. Left unchecked for a human to verify in a real desktop session.
