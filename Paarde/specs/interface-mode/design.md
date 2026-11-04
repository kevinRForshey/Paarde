# Design — Interface mode (Editor-focused vs. Flow/Wizard)

## Summary of architectural change

Epic 25 collapsed Main/Prompt/Diffs/Tutorial into one docked Assistant panel beside Explorer's
editor. This epic does **not** reverse that - it adds a second *presentation* of the same
underlying singletons (`PromptViewModel`, `TutorialViewModel`), reachable as separate
`NavigationView` pages, selected by a persisted setting read once at startup. The business logic,
`IPromptResultStore`, and `AssistantPanelViewModel` itself are untouched; only three thin page
wrappers and some startup/menu wiring are new.

This works cleanly because of a design choice Epic 25 already made for independent reasons:
`ComposePanelControl`/`ReviewPanelControl` take their `DataContext` from whoever hosts them rather
than resolving their own view model from DI. That means a "page" host and a "docked panel" host
are both just *different DataContext sources* for the identical control tree - no markup fork.

## New/changed types

### 1. `InterfaceMode` (new enum, `Paarde.Avalonia/Models/InterfaceMode.cs`)

```csharp
public enum InterfaceMode
{
    EditorFocused = 0,
    FlowWizard = 1,
}
```

`EditorFocused = 0` so that any pre-existing settings store (which has never written this key)
reads back the default via the same "missing key ⇒ default(T)" convention
`ILocalSettingsService.ReadSettingAsync<T>` already uses elsewhere.

### 2. `InterfaceModeSettingsKeys` (new, `Paarde.Avalonia/Services/InterfaceModeSettingsKeys.cs`)

```csharp
public static class InterfaceModeSettingsKeys
{
    public const string Mode = "Interface.Mode";
}
```

### 3. `SettingsViewModel` changes

- New `[ObservableProperty] private InterfaceMode interfaceMode;` loaded in the constructor from
  `ILocalSettingsService.ReadSettingAsync<InterfaceMode>(InterfaceModeSettingsKeys.Mode)`.
- New `[ObservableProperty] private bool interfaceModeChangeRequiresRestart;` - a plain computed
  flag, recomputed in `OnInterfaceModeChanged(InterfaceMode value)` (the `[ObservableProperty]`
  partial generated hook) by comparing against the value loaded at construction time. Drives the
  "Restart to apply" caption's `IsVisible` binding.
- `OnInterfaceModeChanged` also fires `await _localSettingsService.SaveSettingAsync(...)` -
  persisted immediately on selection (same "save as soon as changed" pattern `ThemeVariant` already
  follows via `SwitchThemeCommand`), not deferred to a page-wide "Save" button. Unlike
  `SwitchThemeCommand`, there is no live-apply call (`_themeSelectorService.SetThemeAsync`
  equivalent) - R2/R3/R7/R8 are all startup-time decisions.

### 4. `MainViewModel`/`MainView.axaml` (new, revived)

Rebuilt against the pre-Epic-25 shape (recovered via `git show`), with one behavioral change:
`OpenRecentRun`/`OpenRecentProject`/`SendQuickPrompt` navigate to `typeof(PromptViewModel
).FullName!` exactly as before, but since `PromptViewModel` is now `AddSingleton` (not
`AddTransient`), `OnNavigatedTo`'s existing `PromptLaunchRequest` handling (already written to
support repeated navigation into the same long-lived instance, per
`specs/unified-workbench/requirements.md`'s own notes on this) is what makes a second/third visit
to Home → Compose behave correctly - no new code needed in `PromptViewModel` itself.
`AddTransient` for `MainViewModel` itself (unchanged from the original), since re-reading
`RecentRuns`/`RecentProjects` fresh on every navigation is desired, same as before.

### 5. `ReviewView.axaml`/`.axaml.cs` (new)

```csharp
public partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
        DataContext = App.GetService<TutorialViewModel>();
    }
}
```

```xml
<UserControl ... x:DataType="vm:TutorialViewModel">
    <views:ReviewPanelControl DataContext="{Binding}" />
</UserControl>
```

This is the entire file - `ReviewPanelControl` already does all the work. `Configure<
TutorialViewModel, ReviewView>()` added to `PageService`'s constructor, alongside the four
existing `Configure` calls.

### 6. `App.axaml.cs` changes

- `services.AddSingleton<MainViewModel>()` → actually `AddTransient<MainViewModel>()` (see #4) and
  `services.AddTransient<MainView>()`/`AddTransient<ReviewView>()` registered the same way every
  other page/view pair already is.
- Startup navigation (wherever `InitializeThemeAndNavigate` or its equivalent currently calls
  `GetService<INavigationService>().NavigateTo(typeof(ExplorerViewModel).FullName!)`) becomes:

```csharp
var mode = await GetService<ILocalSettingsService>()
    .ReadSettingAsync<InterfaceMode>(InterfaceModeSettingsKeys.Mode);
var startupPage = mode == InterfaceMode.FlowWizard
    ? typeof(MainViewModel).FullName!
    : typeof(ExplorerViewModel).FullName!;
GetService<INavigationService>().NavigateTo(startupPage);
```

### 7. `ShellView.axaml.cs` changes

Constructor reads the same persisted `InterfaceMode` once (synchronously blocking on the read is
acceptable here - it already happens before the window is shown, same timing `App.axaml.cs`'s own
startup-navigation read uses) and, only for `FlowWizard`, inserts three `NavigationViewItem`s
ahead of the three existing static ones:

```csharp
if (mode == InterfaceMode.FlowWizard)
{
    NavigationViewControl.MenuItems.Insert(0, BuildItem("Home", Symbol.Home, typeof(MainViewModel)));
    NavigationViewControl.MenuItems.Insert(1, BuildItem("Compose", Symbol.Send, typeof(PromptViewModel)));
    NavigationViewControl.MenuItems.Insert(2, BuildItem("Review", Symbol.Document, typeof(TutorialViewModel)));
}
```

`BuildItem` is a small private helper constructing a `NavigationViewItem` + `IconSource` +
`NavigationHelper.NavigateTo` attached-property assignment in code, mirroring exactly what the
three static XAML items already declare declaratively - there is no existing precedent in this
codebase for conditionally-included static XAML items, and three imperative inserts are simpler
than maintaining two parallel `NavigationView.MenuItems` XAML blocks gated by a converter.

### 8. `ExplorerView.axaml.cs` change (R7)

`ResizablePaneHelper`'s construction for `AssistantPanelSplitter` gains a mode-conditional
`defaultWidth` argument:

```csharp
var assistantDefaultWidth = _interfaceMode == InterfaceMode.FlowWizard ? 0 : 360;
```

read once from `ILocalSettingsService` alongside the existing `ExplorerAssistantPanelWidth` read in
the constructor - no change to `ResizablePaneHelper` itself.

### 9. `AssistantPanelViewModel` change (R8)

Its existing `IPromptResultStore.PropertyChanged` subscription (today: `ShowReview()`) gains a
mode check:

```csharp
promptResultStore.PropertyChanged += (_, e) =>
{
    if (e.PropertyName != nameof(IPromptResultStore.Tutorial))
    {
        return;
    }

    ShowReview();

    if (_interfaceMode == InterfaceMode.FlowWizard)
    {
        _navigationService.NavigateTo(typeof(TutorialViewModel).FullName!);
    }
};
```

`AssistantPanelViewModel` already takes `IPromptResultStore` as a constructor dependency; it gains
`InterfaceMode` (read once at construction, same as every other consumer in this design) and
`INavigationService` (already a widely-injected singleton service, e.g. `MainViewModel`/
`PromptViewModel` both already take it - no new DI surface area). `ShowReview()` still runs
unconditionally in both modes so the docked panel's own tab state stays correct if the user later
drags it open in `FlowWizard` mode (R4 of `[[interface-mode]]`'s acceptance criteria, AC-4).

## Testing approach

- Unit tests for `InterfaceModeSettingsKeys`/`InterfaceMode` round-tripping through
  `ILocalSettingsService` (mock), same shape as existing settings-key tests elsewhere in
  `Paarde.Avalonia.Tests`.
- `SettingsViewModelTests`: changing `InterfaceMode` persists immediately and flips
  `InterfaceModeChangeRequiresRestart` to `true`; loading a value equal to the persisted one keeps
  it `false`.
- `MainViewModelTests` (revived/rewritten, same shape as the pre-Epic-25 version would have had):
  `RecentRuns`/`RecentProjects` population from a mock `IRunSessionStore`;
  `OpenRecentRun`/`SendQuickPrompt` call `INavigationService.NavigateTo` with the right parameter
  type.
- `AssistantPanelViewModelTests` extension: a `Tutorial` property change triggers
  `INavigationService.NavigateTo(typeof(TutorialViewModel).FullName!)` when `InterfaceMode` is
  `FlowWizard`, and does not call `NavigateTo` at all when `EditorFocused` (only `ShowReview()`'s
  existing, already-tested effect).
- No automated test can exercise `ShellView.axaml.cs`'s `NavigationView.MenuItems` mutation or
  `ResizablePaneHelper`'s live-rendered default width directly - same sandboxed-environment
  limitation this repo has already documented for every other Avalonia-rendering task (e.g.
  `specs/command-palette/tasks.md` T9). Covered by code inspection plus a manual-verification task
  in `tasks.md`.

## Open questions

- **Does `MainViewModel` need `IConfirmNavigationAway`?** The pre-Epic-25 version didn't have it.
  Resolved: no - it has no unsaved-edit state of its own (unlike `TutorialViewModel`'s
  `HasUnsavedChanges`), same reasoning as the original.
- **Should `ReviewView`'s page instance tear down `ReviewPanelControl`'s pane-width
  `ResizablePaneHelper` state when navigated away from?** Resolved: no change needed -
  `ReviewPanelControl` already persists via `ILocalSettingsService` on `DragCompleted`, not on
  unload, so repeated navigation in/out behaves identically to the docked-panel case today.
