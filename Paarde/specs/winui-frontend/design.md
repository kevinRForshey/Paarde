# Design — WinUI 3 front end (`Paarde.WinUI`)

See `requirements.md` for the inventory and requirements.

## Target layering

```mermaid
flowchart LR
    W["Paarde.WinUI<br/>(WinUI 3, Windows only)<br/>App, Views, Controls,<br/>ShellViewModel, WinUI service impls"]
    A["Paarde.Avalonia<br/>(cross-platform)"]
    P["Paarde.Presentation<br/>ViewModels, Contracts,<br/>AddPaardePresentation()"]
    C["Paarde.Core"]
    W --> P
    A --> P
    P --> C
```

Solutions:

| File | Contains | Builds on |
| --- | --- | --- |
| `Paarde.slnx` (unchanged) | Core, Presentation, Avalonia + their tests | Windows, Linux, macOS |
| `Paarde.Windows.slnx` (new) | everything in `Paarde.slnx` + `Paarde.WinUI` | Windows only |

## Shared composition (R1)

```csharp
// Paarde.Presentation/ServiceCollectionExtensions.cs
public static IServiceCollection AddPaardePresentation(this IServiceCollection services);
```

- Moves from `App.axaml.cs`: every Core service, every Presentation service/options provider, the
  `LlmRunnerSelector`/`LanguageServerSessionManager`/`IRunSessionStore` factories, the keyed
  `IPtyProcessManager`, and all Presentation view models — same lifetimes.
- Stays in each host: `IUiDispatcher`, `INavigationService` (+ `IFrameNavigationService` in
  Avalonia), `IThemeSelectorService`, `IWindowAppearanceService`, `IDialogService`, the three
  pickers, `IPageService`, `INavigationViewService`, `ShellViewModel`, views, Serilog, and
  `services.Configure<LocalSettingsOptions>(configuration.GetSection(...))` (needs
  `Microsoft.Extensions.Options.ConfigurationExtensions`; keeping it in the host avoids adding that
  package to Presentation).
- `ResolveApplicationDataFolder(IConfiguration)` moves next to `LocalSettingsOptions` as a static
  helper (`ApplicationDataPaths.Resolve(string? configuredFolder)`), taking the configured string
  rather than `IConfiguration` so Presentation needs no configuration package.
- Verification: diff the registration list before/after (service type, implementation, lifetime,
  key) with a throwaway script; build, tests, launch.

## Project setup (R2)

- `Paarde.WinUI.csproj`: `OutputType=WinExe`, `UseWinUI=true`,
  `TargetFramework=net10.0-windows10.0.26100.0` (matches the current `dotnet new winui` template),
  `TargetPlatformMinVersion=10.0.19041.0`, `Platforms=x64;ARM64`, `RuntimeIdentifier` defaulting to
  the build machine's architecture (template pattern, so `dotnet build` of the solution works),
  `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true` (no separate runtime install for an
  unpackaged app), `EnableMsixTooling=true` (still needed unpackaged: it generates `resources.pri`).
  Package versions pinned to the latest stable at T2 time.
- Pages navigated by `Frame` must be XAML pages: `Frame.Navigate` to a code-only `Page` subclass
  crashes natively (0xC0000005) because the XAML compiler emits no type metadata for it. Not-yet-
  ported pages are therefore one-line XAML placeholders, not C# stubs.
- Host: `App` constructor builds the same Generic Host as Avalonia (`UseSerilog` →
  `ConfigureServices` → `AddPaardePresentation()` + WinUI registrations). `OnLaunched` creates
  `MainWindow`, resolves `ShellView`, applies saved theme and transparency, then activates.
- Logs: same `<ApplicationDataFolder>/Logs` folder, file prefix `winui-log-.txt` so both apps can
  run at once without contending for one rolling file.
- Bindings: `x:Bind` with a typed `ViewModel` property on each page (compile-checked, like
  Avalonia's compiled bindings, and no `[Bindable]`/reflection needed). `{Binding}` only where a
  template can't use `x:DataType`.

### Packages

| Package | Version | Why | License |
| --- | --- | --- | --- |
| `Microsoft.WindowsAppSDK` | 2.6.5 | WinUI 3 | **Microsoft Software License Terms – Windows App SDK** (free, redistributable, "solely for use on Windows"; checked in the package's `license.txt` at T2). Not MIT: only the WinUI *source* repo is MIT. Accepted as the platform itself - see R2.4. |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.28000.2957 | build tooling WinAppSDK needs | Microsoft license (build-time only, not redistributed) |
| `Microsoft.Extensions.Hosting`, `Serilog.*` | same as Avalonia | host + logging | MIT / Apache-2.0 |
| ~~`Microsoft.Graphics.Win2D`~~ | not used | was planned for `TerminalControl` drawing | Checked at F31.4: the NuGet package is under Microsoft's Win2D EULA, not MIT (only the source repo is MIT). Dropped: `TerminalControl` draws with plain XAML elements instead (below). |
| `CommunityToolkit.WinUI.Controls.Sizers` | 8.2.251219 (added F31.2) | `GridSplitter`; `ResizablePaneHelper.AttachSplitter` persists the width on `ManipulationCompleted` | MIT |
| `TextMateSharp.Grammars` (in `Paarde.Presentation`) | 2.0.5 (F31.2) | `SyntaxTokenizer`, moved out of Avalonia so both front ends share it | MIT |
| editor (O1) | — | see below | must be permissive |

Licenses are checked against each package's own license file when it's added, not assumed from
the source repo.

## UI-specific services (R3)

| Contract | WinUI implementation | Notes |
| --- | --- | --- |
| `IUiDispatcher` | `WinUiDispatcher(DispatcherQueue)` → `TryEnqueue`; timer → `DispatcherQueue.CreateTimer()` | Constructed in `App`'s constructor (UI thread) with `DispatcherQueue.GetForCurrentThread()` and registered as an instance, because DI may first resolve it off the UI thread. |
| `INavigationService` | `NavigationService` over WinUI `Frame` | Port of the Avalonia one: `PageService.GetPageType(key)` → `Frame.Navigate(type, parameter)`; `INavigationAware.OnNavigatedFrom/To`, `IConfirmNavigationAway` before leaving, `clearNavigation` → `BackStack.Clear()`. Exposes `Frame` + `Navigated` only on a WinUI-side `IFrameNavigationService`, mirroring Avalonia. |
| `IThemeSelectorService` | `ElementTheme` on the window's root `FrameworkElement` | `AppTheme.Default/Light/Dark` ↔ `ElementTheme.Default/Light/Dark`; persists `theme.ToString()` under the existing key. Also updates title-bar button colors. |
| `IWindowAppearanceService` | `MainWindow.SystemBackdrop` = `MicaBackdrop` if `MicaController.IsSupported()`, else `DesktopAcrylicBackdrop`, else `null` | Same fallback order as Avalonia. |
| `IDialogService` | `ContentDialog` with `XamlRoot = MainWindow.Content.XamlRoot`, `DefaultButton = Close` for confirms | |
| Pickers | Windows App SDK's `Microsoft.Windows.Storage.Pickers` (`FileOpenPicker` / `FileSavePicker` / `FolderPicker`) constructed with `AppWindow.Id` | Not the UWP-era `Windows.Storage.Pickers`: no `InitializeWithWindow` interop, plain paths back, and they work elevated. No settable title, so `IFileSavePickerService`'s `title` is unused. |
| `IPageService` | same 7 `Configure<VM, View>()` registrations | |
| `INavigationViewService` + `ShellViewModel` | WinUI `NavigationView` / `NavigationViewItem` (`NavigationHelper.NavigateTo` attached property ported) | |

Converters: replaced by `x:Bind` function bindings in `Helpers/BindingFunctions.cs`
(`ToInfoBarSeverity`, `IsEnumValue`, `IsNotEmpty`, `VisibleIf`/`CollapsedIf`/`VisibleIfNotNull`) -
compile-checked and no resources to declare. Enum radio groups bind `IsChecked` one-way and set
the view model from `Checked` handlers (Tag = member name), because `x:Bind`'s generated two-way
code casts whatever `ConvertBack` returns and so can't ignore an unchecked button.
F31.2 added `DiffLineBackground`/`DiffCellBackground` for diff row tints.

Diff and step lists (F31.2) are `ListView`s with `SelectionMode="None"` (virtualized, and
`ScrollIntoView` for change/step navigation). The diff lists sit in a horizontal-only outer
`ScrollViewer` so long lines scroll instead of clipping. `DataTemplate`s can't reach the parent's
DataContext, so rows are wrapped in records that carry the file path (`DiffRowItem`,
`SideBySideRowItem`).

Other WinUI-specific notes from F31.1: `x:Bind` defaults to `OneTime`, so every live binding
states `Mode=OneWay`/`TwoWay` (text boxes add `UpdateSourceTrigger=PropertyChanged` to match
Avalonia's commit-per-keystroke); `[ObservableProperty]` in `Paarde.WinUI` uses partial properties
(MVVMTK0045), while Presentation's field-based ones are left alone; the theme setting key moved to
`AppearanceSettingsKeys.RequestedTheme` so both `ThemeSelectorService`s share it.

## Controls (R4)

### `TerminalControl`

Plain XAML on a `Canvas`, not Win2D (see Packages). Per row: one `Rectangle` per run of the same
background colour, and one `TextBlock` per run of same-style text, placed at its exact column, so
a monospace font can't drift out of the grid by more than one run. Wide characters always get a
run of their own. Rows whose cells are unchanged since the last refresh are left alone, and
`TerminalBuffer.Changed` (raised on the PTY thread) is coalesced to one refresh per UI-thread turn.
The cell is measured from a run of "M"s in `MonospaceFontFamily`, with row height snapped to whole
device pixels so row backgrounds don't anti-alias into seams.
Input: `PreviewKeyDown` (so Tab and the arrow keys reach the PTY rather than moving focus) uses
Avalonia's key-translation table, ported as-is, and `CharacterReceived` sends printable
characters, including surrogate pairs. AltGr (Ctrl+Alt) is left to `CharacterReceived` so it
still types characters. Size changes → `Resized(cols, rows)`.
The `Changed` subscription only lives while the control is loaded, because the buffers are
singletons that outlive the pages hosting them.

The workbench dock: WinUI has no `TabControl`, so a `SelectorBar` (Build/Run/Problems/Terminal,
`Tag` = `ActiveTabKey`) toggles which of four panes is visible inside the `Expander`.

### `MarkdownTextBlock`, `DiffWordSpanTextBlock`

Straight ports onto `RichTextBlock`/`TextBlock.Inlines` (`Run`, `Bold`, `Italic`, `Span`), keeping
the same dependency properties so view XAML ports 1:1.

### Code editor — open question O1

`CodeEditor` is confined to `ExplorerFileTabControl`, so the decision is deferred to a timeboxed
spike (T-F6) just before the Explorer port. Candidates, scored against R4.4's checklist (highlight,
two-way text, brackets, diagnostics, diff backgrounds, hunk gutter accept/reject, ghost text,
completion list, F12):

| Option | Fit | Cost / risk |
| --- | --- | --- |
| **Monaco in `WebView2`** (MIT) | Every R4.4 item is a documented Monaco API: markers, `deltaDecorations` (line + glyph-margin), inline-completions provider, completion provider, definition provider, bracket matching, TextMate-grade highlighting. | JS ↔ C# bridge for text sync and LSP callbacks; WebView2 memory per open tab (mitigate with one WebView2 + Monaco models per tab); offline bundling of Monaco assets. |
| **WinUIEdit** (Scintilla for WinUI; verify license) | Native; Scintilla indicators, markers, margins, autocomplete list cover most items. | Ghost text only as end-of-line annotations; syntax highlighting depends on which Lexilla lexers it exposes; smaller community. |
| Custom on Win2D | Full control | Re-implementing an editor; rejected. |

**Recommendation:** spike Monaco first (it's the only option that covers ghost text and
highlighting without compromise); fall back to WinUIEdit if WebView2 startup/memory is
unacceptable. The spike's output is a filled-in checklist + decision recorded here.

## Sequencing

Each feature leaves both apps building and the Avalonia app unchanged in behavior.

```mermaid
flowchart TD
    F0["F31.0 Shared composition<br/>(Avalonia refactor)"] --> F1["F31.1 Scaffold, services,<br/>shell, Settings"]
    F1 --> F2["F31.2 Main, History,<br/>Review, Diffs"]
    F1 --> F3["F31.3 Prompt + Compose"]
    F1 --> F4["F31.4 Terminal, dock,<br/>Run panel, status bar"]
    F2 & F3 & F4 --> F5["F31.5 Palette, Quick Open,<br/>shortcuts"]
    F1 --> F6["F31.6 Editor spike (O1)"]
    F6 & F5 --> F7["F31.7 Explorer + editor"]
    F7 --> F8["F31.8 Parity check, docs"]
```

Settings first because it exercises every R3 service (theme, transparency, dialogs, pickers,
navigation) on the simplest page.

## Testing

- Build both solutions; run the three existing test projects (counts unchanged).
- F31.0: registration-list diff + Avalonia launch.
- Each feature: side-by-side manual check against the Avalonia app on the ported pages.
- No WinUI test project unless a feature produces non-XAML logic not already covered by
  `Paarde.Presentation.Tests` (WinUI unit tests need a packaged test app, which is heavy).

## Risks

- **Editor (O1)** is the largest unknown; isolated to one view and deferred until the rest works.
- **`IConfirmNavigationAway` timing**: WinUI `Frame` navigation is synchronous; the async confirm
  has to run *before* calling `Frame.Navigate` (as Avalonia's `NavigationService` already does),
  not in `OnNavigatingFrom`.
- **Keyboard shortcuts**: Avalonia `KeyBinding`s in `ShellView` become `KeyboardAccelerator`s;
  some (F12 in the editor, Ctrl+Tab) may be swallowed by focused controls and need
  `ProcessKeyboardAccelerators` handling. _(F31.5: the shell's three accelerators work from
  every page and from inside the popups. `TerminalControl` handles keys in `PreviewKeyDown`, so a
  focused terminal keeps Ctrl+P, as in Avalonia. The editor's F12 and Ctrl+Tab are still open
  for F31.7.)_
- **Shared settings**: a WinUI-only setting added later must not break the Avalonia app — keep all
  keys in Presentation's `*SettingsKeys`, read with defaults.
- **Unpackaged APIs**: some Windows APIs (notifications, `ApplicationData`) need package identity;
  avoid them, keep using `%AppData%` paths.

## Open questions

- O1 — Code editor control (spike in F31.6).
- O2 — Should the settings folder eventually be renamed from `Paarde.Avalonia/ApplicationData` to
  something host-neutral? Not in this epic (would need a migration); sharing the current one is fine.
