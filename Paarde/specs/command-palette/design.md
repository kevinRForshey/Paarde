# Design — Command palette & Quick Open

Companion to `requirements.md` in this directory. Implements R1–R3.

## Summary of the architectural change

Two new DI-singleton view models (`QuickOpenViewModel`, `CommandPaletteViewModel`), owned by
`ShellViewModel` the same "singleton handed in, not created here" way `RunPanelViewModel`/
`StatusBarViewModel`/`WorkbenchDockViewModel` already are. Two new `Popup`s in `ShellView.axaml`,
anchored to the `NavigationFrame` so they overlay whichever page is currently showing. Two new
global `KeyBinding`s, attached in `ShellView.axaml.cs`'s constructor to the containing `Window`
(there being no other place in the app that currently owns a `Window`-scoped gesture besides the
one-off in `App.axaml.cs`). One new `if (parameter is string filePath)` branch in the already
general-purpose `ExplorerViewModel.OnNavigatedTo`. No existing page's own content changes.

## New/changed types

### `Paarde.Avalonia/ViewModels/QuickOpenViewModel.cs` (new)

```csharp
public partial class QuickOpenViewModel : ObservableObject
{
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private string? selectedResult;

    public ObservableCollection<string> Results { get; } = new(); // relative paths, for display

    [RelayCommand]
    private void Open() { ... } // IsOpen = true; CommandPaletteViewModel.IsOpen = false (R3.2); re-filter with the current (likely empty) Query

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Refilter(value);

    [RelayCommand]
    private void ActivateSelected() { ... } // see "Activating a result" below
}
```

Constructor dependencies: `INavigationService`, `ILocalSettingsService`, `IWorkingDirectoryContext`.
Working-directory tracking copies the exact pattern `StatusBarViewModel`/`WorkbenchDockViewModel`
already use for the same `IWorkingDirectoryContext` (which has no readable "current" property, only
`Changed`): read `WorkingDirectorySettingsKeys.WorkingDirectory` once via `ILocalSettingsService` in
the constructor, keep it in a private field, and update that field (not re-reading settings) on
`IWorkingDirectoryContext.Changed`.

**File-tree caching (resolves requirements.md's first open question):** `QuickOpenViewModel` keeps
its own `private FileTreeEntry? _cachedTree` / `_cachedTreeWorkingDirectory`, invalidated the same
way `PromptViewModel._cachedMentionFileTree` already is (rebuild when the working directory
differs from what's cached). This is the same small duplication Epic 14 already tolerates between
its own two call sites rather than a new `IFileTreeCache` abstraction — two consumers doesn't
justify extracting a shared service, and the two caches can legitimately go stale at different
times (Quick Open only needs to be fresh when opened; the mention popup needs to be fresh on every
keystroke while typing a prompt).

**Refilter:**
```csharp
private void Refilter(string query)
{
    if (_workingDirectoryPath is null || !Directory.Exists(_workingDirectoryPath))
    {
        Results.Clear(); // R1.5: explicit empty state, not a throw
        return;
    }

    if (_cachedTree is null || _cachedTreeWorkingDirectory != _workingDirectoryPath)
    {
        _cachedTree = FileTreeBuilder.Build(_workingDirectoryPath);
        _cachedTreeWorkingDirectory = _workingDirectoryPath;
    }

    Results.ReplaceWith(FileMentionMatcher.FindMatches(_cachedTree, query)); // R1.2
    SelectedResult = Results.FirstOrDefault();
}
```
(`ReplaceWith` here is shorthand for the existing `Clear()`-then-`Add()`-each loop
`PromptViewModel.UpdateMentionSuggestions` already uses — no new collection helper, same inline
pattern.)

**Activating a result (resolves requirements.md's third open question):** `FileMentionMatcher`
returns *relative* paths (for display, matching Epic 14's existing convention); activating one
resolves it back to an absolute path with `Path.Combine(_workingDirectoryPath, selectedResult)`
and calls:

```csharp
_navigationService.NavigateTo(typeof(ExplorerViewModel).FullName!, absolutePath);
IsOpen = false;
```

This is the entire integration point with Explorer — no new coupling, no `ExplorerViewModel`
singleton promotion. `INavigationService.NavigateTo(pageKey, parameter)` already exists and already
threads `parameter` through to `OnNavigatedTo`; `ExplorerViewModel.OnNavigatedTo` already runs this
exact instance (per requirements.md's context note on why the "transient" registration already
behaves like a singleton in practice) regardless of whether `Navigate` actually changes page or the
Frame determines Explorer is already the current page — `NavigationService.NavigateTo`'s existing
`_lastParameterUsed` dedupe only skips re-navigating when `pageKey` *and* `parameter` are both
unchanged, so selecting a second, different file while Explorer is already showing still delivers
the new path.

`ExplorerViewModel.OnNavigatedTo` (changed):
```csharp
public async void OnNavigatedTo(object parameter)
{
    try
    {
        WorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
        RefreshTree();

        if (!string.IsNullOrWhiteSpace(WorkingDirectoryPath) && Directory.Exists(WorkingDirectoryPath))
        {
            await _languageServerSessionManager.StartAllAsync(WorkingDirectoryPath);
        }

        // Epic 24, R1.4: Quick Open navigates here with the absolute path of the file to open.
        if (parameter is string filePath && File.Exists(filePath))
        {
            OpenOrActivateTab(filePath);
        }
    }
    catch (...) { ... }
}
```
`OpenOrActivateTab` is already `private` on this same class, so no visibility change is needed —
this is purely a new call site inside the method that already owns the type's only other call
site (`OnSelectedNodeChanged`). The `File.Exists` guard means a stale/deleted path from an earlier
session's tree cache fails silently (no tab opened) rather than `OpenFileTabViewModel` throwing on
construction.

### `Paarde.Avalonia/ViewModels/CommandPaletteViewModel.cs` (new)

```csharp
public sealed record PaletteCommand(string Name, Func<bool> CanExecute, Action Execute);

public partial class CommandPaletteViewModel : ObservableObject
{
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private PaletteCommand? selectedResult;

    public ObservableCollection<PaletteCommand> Results { get; } = new();

    private readonly IReadOnlyList<PaletteCommand> _registry; // built once in the constructor

    [RelayCommand]
    private void Open() { ... } // IsOpen = true; QuickOpenViewModel.IsOpen = false (R3.2); Refilter(string.Empty)

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Refilter(value);

    [RelayCommand]
    private void ExecuteSelected()
    {
        if (SelectedResult is { } command && command.CanExecute())
        {
            command.Execute();
        }

        IsOpen = false; // R2.5: closes either way - a disabled command can't be the SelectedResult in practice (see "disabled, not hidden" below)
    }
}
```

**Registry construction (R2.2) — resolves the "explicit, hardcoded" requirement literally:**
`CommandPaletteViewModel`'s constructor takes `RunPanelViewModel` and `INavigationService` and
builds `_registry` as a plain list, not by reflecting over `NavigationView.MenuItems` or any
`[RelayCommand]`-bearing type:

```csharp
_registry =
[
    new("Build", () => runPanelViewModel.BuildCommand.CanExecute(null), () => runPanelViewModel.BuildCommand.Execute(null)),
    new("Run", () => runPanelViewModel.RunCommand.CanExecute(null), () => runPanelViewModel.RunCommand.Execute(null)),
    new("Stop", () => runPanelViewModel.StopCommand.CanExecute(null), () => runPanelViewModel.StopCommand.Execute(null)),
    GoTo("Main", typeof(MainViewModel)),
    GoTo("Prompt", typeof(PromptViewModel)),
    GoTo("Explorer", typeof(ExplorerViewModel)),
    GoTo("Terminal", typeof(TerminalViewModel)),
    GoTo("Diffs", typeof(DiffsViewModel)),
    GoTo("Tutorial", typeof(TutorialViewModel)),
    GoTo("History", typeof(HistoryViewModel)),
    GoTo("Settings", typeof(SettingsViewModel)),
];

PaletteCommand GoTo(string name, Type viewModelType) =>
    new($"Go to {name}", () => true, () => navigationService.NavigateTo(viewModelType.FullName!));
```
This list is hand-maintained and must be kept in sync with `ShellView.axaml`'s `NavigationView`
entries by whoever adds a page in the future — the same manual-sync cost
`NavigationHelper.NavigateTo`'s string page keys already carry everywhere else in this codebase
(there is no single source of truth for "pages" today; this doesn't introduce a new one, it just
adds a second hand-written list alongside the XAML that already exists).

**"Disabled, not hidden" (R2.4):** `Refilter` still includes commands whose `CanExecute()` is
currently `false` in `Results` (substring-filtered the same as everything else); the
`ListBox`'s `ItemTemplate` binds a disabled-looking style to `CanExecute()` evaluated at bind time
(an `IValueConverter` or a `Func<PaletteCommand, bool>`-backed binding — implementation detail at
build time, not a new type). `ExecuteSelected`'s own `CanExecute()` re-check means even a
stale/disabled highlighted command can't actually fire if its state changed between render and
`Enter`.

### `Paarde.Avalonia/ViewModels/ShellViewModel.cs` (changed)

Two more constructor-injected singleton properties, same shape as the existing three:
```csharp
public QuickOpenViewModel QuickOpenViewModel { get; }
public CommandPaletteViewModel CommandPaletteViewModel { get; }
```

### `Paarde.Avalonia/App.axaml.cs` (changed)

```csharp
services.AddSingleton<QuickOpenViewModel>();
services.AddSingleton<CommandPaletteViewModel>();
```

### `ShellView.axaml` (changed)

Two new `Popup`s added alongside the existing dock/status-bar rows, anchored to the
`NavigationFrame` (R1.1/R2.1's "independent of the currently navigated page" — placing the popup's
`PlacementTarget` on the `Frame` rather than any one page's control is what makes it appear
regardless of which page is underneath):

```xml
<Popup Name="QuickOpenPopup"
       PlacementTarget="NavigationFrame"
       Placement="Center"
       IsLightDismissEnabled="True"
       IsOpen="{Binding QuickOpenViewModel.IsOpen}">
    <Border Classes="PalettePopupChrome">
        <StackPanel>
            <TextBox Name="QuickOpenQueryBox" Text="{Binding QuickOpenViewModel.Query}" Watermark="Go to file..." />
            <ListBox Name="QuickOpenResultsList"
                      ItemsSource="{Binding QuickOpenViewModel.Results}"
                      SelectedItem="{Binding QuickOpenViewModel.SelectedResult}" />
        </StackPanel>
    </Border>
</Popup>
<Popup Name="CommandPalettePopup"
       PlacementTarget="NavigationFrame"
       Placement="Center"
       IsLightDismissEnabled="True"
       IsOpen="{Binding CommandPaletteViewModel.IsOpen}">
    <Border Classes="PalettePopupChrome">
        <StackPanel>
            <TextBox Name="CommandPaletteQueryBox" Text="{Binding CommandPaletteViewModel.Query}" Watermark="Type a command..." />
            <ListBox Name="CommandPaletteResultsList"
                     ItemsSource="{Binding CommandPaletteViewModel.Results}"
                     SelectedItem="{Binding CommandPaletteViewModel.SelectedResult}" />
        </StackPanel>
    </Border>
</Popup>
```

**Difference from Epic 14's popup shape, noted explicitly (R3.3's "reuse" is about the mechanism,
not a byte-identical copy):** the mention popup has no `TextBox` of its own — it reads from
whichever `TextBox` triggered it (`PromptTextBox`). Quick Open/the command palette have no
"host" control to read from (there is nothing on screen already being typed into when `Ctrl+P`
fires), so each popup's content includes its own query `TextBox`, focused programmatically when
the popup opens (`QuickOpenQueryBox.Focus()` in the `Open` command's code-behind-adjacent
handling, or an `IsOpen`-changed handler in `ShellView.axaml.cs` — implementation detail, not a new
pattern). Everything else (`Popup`/`IsLightDismissEnabled`/`ListBox`) is identical to
`PromptView.axaml`'s existing precedent.

`Up`/`Down`/`Enter`/`Escape` handling on each query `TextBox`'s `KeyDown` mirrors
`PromptView.axaml.cs`'s `OnPromptTextBoxKeyDown` exactly (move `SelectedResult`'s index,
`Enter`→`ActivateSelected`/`ExecuteSelected`, `Escape`→`Close`), added as two new handlers in
`ShellView.axaml.cs` rather than reusing `PromptView`'s (different `UserControl`, no shared base
worth introducing for two call sites — same YAGNI call Epic 14 itself already made by not
factoring out a reusable "filterable popup" control for its own single use).

### `ShellView.axaml.cs` (changed) — global key bindings (resolves requirements.md's second open
question)

```csharp
public ShellView()
{
    ViewModel = App.GetService<ShellViewModel>();
    DataContext = ViewModel;
    InitializeComponent();
    // ... existing wiring ...

    AttachedToVisualTree += OnAttachedToVisualTree;
}

private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
{
    if (TopLevel.GetTopLevel(this) is not Window window)
    {
        return;
    }

    window.KeyBindings.Add(new KeyBinding
    {
        Gesture = new KeyGesture(Key.P, KeyModifiers.Control),
        Command = ViewModel.QuickOpenViewModel.OpenCommand,
    });
    window.KeyBindings.Add(new KeyBinding
    {
        Gesture = new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift),
        Command = ViewModel.CommandPaletteViewModel.OpenCommand,
    });
}
```

**Decision — attached to the `Window`, not `ShellView` itself:** Avalonia's `KeyBindings`
collection lives on `TopLevel` (`Window` is one; a plain `UserControl` like `ShellView` is not), so
there is no choice here beyond *which* `Window` — and since `App.axaml.cs` constructs exactly one
`MainWindow` with `ShellView` as its entire `Content` (desktop single-window app, confirmed by
inspection), reaching it via `TopLevel.GetTopLevel(this)` from inside `ShellView` is simpler than
threading a `Window` reference through `App.axaml.cs`'s construction order, and keeps the
`KeyBinding` registration next to the view models it targets instead of in `App.axaml.cs`'s
unrelated service-registration code. `AttachedToVisualTree` (not the constructor) is used because
`TopLevel.GetTopLevel(this)` returns `null` until `ShellView` is actually in the visual tree —
confirmed necessary the same way `ResizablePaneHelper`'s existing code already needs its own
`AttachedToVisualTree`-timed hookups elsewhere in this codebase.

**R3.2 (only one popup open at a time):** each `OpenCommand` sets the *other* view model's
`IsOpen = false` before setting its own `true` — `QuickOpenViewModel.Open()` needs a reference to
`CommandPaletteViewModel` (and vice versa) purely for this one line, passed in as a constructor
parameter. This is a direct dependency between two small, already-paired singletons (not a new
pub/sub context type) — proportionate for "these two mutually exclude each other," unlike
`IWorkingDirectoryContext`/`IActiveEditorContext`'s pub/sub shape, which exists specifically to
avoid direct references between types that don't otherwise know about each other; here the two
view models already need to know about each other by definition of the requirement.

## Testing approach

- `QuickOpenViewModelTests` (`Paarde.Avalonia.Tests`): `Refilter` with no working directory set
  leaves `Results` empty (R1.5); with a fake working directory, typing narrows `Results` to
  matching relative paths (reusing a fake/temp directory tree, same style
  `FileMentionMatcherTests`/`FileTreeBuilderTests` already use); `ActivateSelected` calls
  `INavigationService.NavigateTo` with `typeof(ExplorerViewModel).FullName` and the resolved
  absolute path, then sets `IsOpen = false`.
- `CommandPaletteViewModelTests`: the registry contains exactly the expected Build/Run/Stop/Go-to-*
  entries; filtering by substring narrows `Results`; `ExecuteSelected` calls through to the
  underlying `RunPanelViewModel` command only when `CanExecute()` is true, and is a no-op (closes
  the popup, executes nothing) when false; selecting a "Go to X" entry calls
  `INavigationService.NavigateTo` with the right page key.
- `ExplorerViewModelTests` extended: `OnNavigatedTo` with a `string` file-path parameter opens or
  activates a tab for that file in addition to its existing working-directory-reload behavior; a
  non-existent path is a silent no-op (R1.4's guard); a non-`string` (or `null`) parameter preserves
  today's exact existing behavior (regression guard — this must not change what happens when
  Explorer is reached by its ordinary `NavigationViewItem`).
- Popup `KeyBinding`/focus/`KeyDown` wiring in `ShellView.axaml.cs`, like every other
  Avalonia-rendering concern in this codebase, needs a real display and is manual/deferred.

## Open question

- Whether the command-palette registry (R2.2) should grow a "New Prompt"/"Clear context" or other
  page-specific action beyond pure navigation, as `ROADMAP.md`'s original Epic 24 text speculated.
  **Deferred**: none of those commands exist as a reusable, page-independent `IRelayCommand` today
  (they're private to `PromptViewModel`/etc., scoped to whichever page owns them), so adding them
  would mean either promoting more view models to singletons or reaching into a not-currently-active
  page's state — a bigger design decision than this epic's first pass, and not required by
  `ROADMAP.md`'s R2.2 text either. Fast-follow once there's a concrete need.
