# Requirements — Build & Run

## Status

Draft. Not started. This is Epic 20 (`ROADMAP.md`) — the first, concrete slice of the broader
"wizard → IDE" initiative also described there (Epics 20–24). Scope here is deliberately narrowed
to **Build and Run**, the feature explicitly requested: persistent Build/Run/Stop controls, working
for .NET today, structured so a second compiled (or interpreted) language is a configuration
change, not a rewrite.

## Context and current state

Confirmed by direct inspection, 2026-10-26:

- **Nothing in this app runs the user's actual program.** Epic 12 (`ROADMAP.md`, done) added
  `IBuildTestRunner`/`ProcessCommandRunner` and a global `BuildCommand`/`TestCommand` pair in
  Settings, but it only fires automatically *after* an LLM-proposed change is applied, as a
  pass/fail gate feeding a "send errors back to Claude" loop — there is no button anywhere that
  just builds or runs the project on demand.
- **Epic 16's Terminal page can run `dotnet run` manually**, but `ITerminalCommandRunner.RunAsync`
  (`Paarde.Core/Services/ProcessCommandRunner.cs`) is wait-for-exit: it returns a `CommandResult`
  only once the process exits. A `dotnet run`-started web server or any other long-lived process
  never exits on its own, so this call simply hangs — there is no "stop" button, no live Run
  session, and the Terminal page is a separate full navigation away from whatever file you were
  looking at.
- **No concept of a run target exists anywhere.** `IProjectTypeDetector`
  (`Paarde.Core/Contracts/Services/IProjectTypeDetector.cs`, Epic 18) answers "which languages does
  this working directory contain, and where," which is a different question from "which specific
  project/script in there is the thing you'd press Run on" — a repo can contain several `.csproj`
  files, only some of which are runnable (console/web apps vs. class libraries vs. test projects).
- **Every build/test/run-adjacent setting today is a single flat value**, not per-project — same
  shape problem Epic 18 already fixed once for `LspOptions` (one global LSP config) by replacing it
  with per-language `LanguageServerOptions`. Build & Run needs the analogous per-configuration shape
  from day one rather than repeating that migration later.
- There is no persistent UI surface at all today — every page (`Prompt`, `Explorer`, `Terminal`,
  `Tutorial`, `Diffs`, `History`, `Settings`) is a full `Frame` navigation target under
  `ShellView`'s `NavigationView` (confirmed in `ShellView.axaml`/`PageService.cs`). Nothing survives
  a page switch except what's explicitly persisted to settings. A Run button has nowhere to live
  that isn't itself a page you'd have to navigate to and away from, which is exactly the "wizard"
  pattern this epic (and [[ide-workbench-shell]]) is reacting against.

## Goals

1. A **Build** action and a **Run** action that work today for this repo's own stack (.NET /
   `dotnet build` / `dotnet run`), triggerable without leaving whatever page the user is on.
2. A **Run** action starts a real, long-lived process (e.g. `dotnet run` serving a web app) that
   keeps running and streaming output until the user explicitly stops it or it exits on its own —
   not a wait-for-exit call that never returns.
3. Support more than one runnable target in a single working directory (e.g. an API project and a
   console tool side by side), with a way to pick which one "Run" currently means.
4. Structure language-specific knowledge (how to detect a runnable target, what the default build
   command is, what the default run command is) as **data**, not branching code, so adding Python
   or Node/TypeScript support later is a settings/defaults change — explicitly anticipated by "for
   now and also for other compiled languages in the future" in the request that produced this spec.
5. Never leave an orphaned child process behind — a Run session must be torn down on app exit the
   same way Epic 17/18's language-server processes already are.
6. The controls themselves must be reachable from anywhere in the app, not buried on a page you
   have to navigate to — this is the one piece of the broader workbench-shell problem
   ([[ide-workbench-shell]]) this epic cannot defer, since "a Run button" is the explicit ask.

## Non-goals

- A real debugger (breakpoints, stepping, variable inspection). This epic runs a process and shows
  its output; attaching a debugger is a materially bigger, separate epic if ever pursued.
- Per-configuration environment variables, launch profiles, or multi-process "compound" run
  configurations (e.g. "run API + run frontend together"). A `RunConfiguration`'s command string is
  shelled out the same way Terminal's already is, so `FOO=bar dotnet run` typed directly into the
  command field works today without dedicated UI for it — a first-class environment-variable
  editor is left for later if it turns out to matter.
- Solving the general "this app feels like a wizard, not an IDE" problem — that's
  [[ide-workbench-shell]] (`ROADMAP.md` Epic 22) and its siblings (Epics 23–25). This spec covers
  only the Build/Run controls and the minimal persistent strip they need to live in; Epic 22
  generalizes that strip into the full output dock (merging in Terminal and Problems) plus a status
  bar.
- Rebuilding Epic 12's post-apply build/test gate. That stays exactly as it is — a separate,
  automatic, whole-repo check run right after an LLM apply. This epic's per-configuration
  Build/Run is a different, user-triggered action that may target a single project within the
  repo, not the whole thing. See design.md's "Relationship to Epic 12" for how the two settings
  are allowed to diverge after a one-time seed.
- Perfect run-target detection for every build system. C# detection (R2) is solid (`.csproj`
  inspection). Python/Node detection (R2.4/R2.5) is a first-pass heuristic, explicitly weaker, and
  always user-correctable by hand-editing or adding a configuration — see design.md's open
  questions.

## Requirements

**R1 — Run configuration model**
- R1.1 A `RunConfiguration` is a named, persisted record of: a language id (same vocabulary
  `LanguageServerOptions.LanguageId` already uses — `"csharp"`/`"python"`/`"typescript"`), the
  working-directory-relative project/script path it targets, a build command, a run command, and
  whether it was auto-detected or hand-authored/edited.
- R1.2 Multiple `RunConfiguration`s can coexist for one working directory (R1.1's "more than one
  runnable target" goal). Exactly one is "selected" at a time; the selection persists per working
  directory across app restarts.
- R1.3 Configurations persist via the same settings-provider seam `LanguageServerOptionsProvider`
  already established (read-fresh-every-time, no caching) — not a new persistence mechanism.

**R2 — Detecting runnable targets**
- R2.1 For C#: every `.csproj` under a detected `"csharp"` language root (Epic 18's
  `IProjectTypeDetector.DetectLanguageRoots`) is a candidate; a candidate is excluded if it looks
  like a test project (`<IsTestProject>true</IsTestProject>`, a `Microsoft.NET.Test.Sdk` package
  reference, or a filename matching `*.Tests.csproj`/`*.Test.csproj`) and otherwise included if it
  declares `<OutputType>Exe</OutputType>`, `<OutputType>WinExe</OutputType>`, or an SDK of
  `Microsoft.NET.Sdk.Web` (web projects are runnable even with no explicit `OutputType`).
- R2.2 Each included C# candidate gets a default build command (`dotnet build "<path>"`) and run
  command (`dotnet run --project "<path>"`).
- R2.3 Detection re-runs whenever the working directory changes (same trigger point Epic 17/18 use
  for starting language servers) and merges into whatever's already persisted: a configuration not
  seen before is appended; one the user has since edited (R1.1's "hand-authored" flag) is left
  alone rather than silently overwritten by a fresh detection pass.
- R2.4 For Python: a best-effort heuristic (common entry-point filenames — `main.py`, `app.py`,
  `manage.py` — inside a detected `"python"` root) produces zero or more candidates with a run
  command of `python <file>` and no build command. Explicitly weaker than R2.1/R2.2; producing zero
  candidates (requiring the user to add one by hand) is an acceptable outcome, not a bug.
- R2.5 For TypeScript/JavaScript: `package.json`'s `scripts` map is read; a `dev` or `start` entry
  (preferring `dev`) becomes a run configuration's run command (`npm run dev`/`npm start`), and a
  `build` entry becomes its build command, inside a detected `"typescript"` root.
- R2.6 The user can add, edit, or delete a `RunConfiguration` by hand (Settings UI), for any
  language, independent of whether detection found anything — this is the escape hatch for every
  case R2.1–R2.5 gets wrong or doesn't cover.

**R3 — Build**
- R3.1 A Build action runs the selected configuration's build command via the existing
  `IBuildTestRunner` contract (Epic 12) — a build is expected to terminate; no new "wait for exit"
  abstraction is introduced for it.
- R3.2 Build output (stdout/stderr) and pass/fail status are visible without navigating to a
  different page from wherever the user triggered it.
- R3.3 A blank build command (legitimate for an interpreted-language configuration, R2.4) skips the
  build step entirely rather than erroring.

**R4 — Run**
- R4.1 A Run action starts the selected configuration's run command as a real, long-lived child
  process and returns once it has *started*, not once it exits — this is the behavioral gap
  neither `IBuildTestRunner` nor `ITerminalCommandRunner` (both wait-for-exit) currently fill.
- R4.2 Run's output streams live, the same way Terminal's already does, visible without navigating
  away from the current page.
- R4.3 By default, Run performs Build first (if a build command is configured) and only starts the
  run command if the build succeeded; a build failure stops the sequence and surfaces the same way
  a standalone Build failure would (R3.2).
- R4.4 A Stop action terminates the running process (and its full process tree — a `dotnet run`
  process is itself a launcher for the actual app process on some platforms) on demand.
- R4.5 Starting Run while a previous Run from this app is still active stops the previous one
  first, rather than erroring or leaving two processes running unknowingly.
- R4.6 The running process is terminated on app exit, the same way Epic 17/18's language-server
  processes already are — no orphaned child process left behind when the app closes (R5 of
  "Goals").

**R5 — Reachability**
- R5.1 Build/Run/Stop controls and the configuration picker are visible regardless of which page
  (`Prompt`/`Explorer`/`Tutorial`/etc.) is currently active — they live in the persistent shell
  (`ShellView`), not on any one page.
- R5.2 Output from the most recent Build or Run is reachable from that same persistent location,
  not a separate page requiring navigation.

**R6 — Tests**
- R6.1 Automated coverage for: run-configuration detection (C# inclusion/exclusion rules, R2.1;
  merge-without-clobbering, R2.3), the long-lived process manager (start/stream/stop, double-start
  stops the prior run, process-tree kill), and the Build→Run sequencing/failure-short-circuit logic
  (R4.3) — at the ViewModel/service level, consistent with this codebase's existing "no X server in
  this sandbox" limitation for anything requiring a rendered `CodeEditor`/`Application`.
- R6.2 Manual verification (same caveat as every other UI-touching task in this codebase) that the
  toolbar/output panel actually render and behave correctly in a running `Application`.

## Open questions

- Should "Run" always rebuild (R4.3), or should there be a secondary "Run without building" action
  for a fast inner loop once a build is already known-current? **Leaning toward always-rebuild for
  v1** — simpler, matches most IDEs' default `F5` behavior, and avoids a second button for a
  marginal case; revisit if it proves annoying in practice.
- Python/Node detection (R2.4/R2.5) is acknowledged as weak. An alternative for Node would be
  always offering every `scripts` entry rather than guessing `dev`/`start`/`build` — deferred as a
  possible refinement once there's a real polyglot repo to test against, the same way Epic 18's
  TypeScript rooting bug only surfaced under real manual verification.
- Whether a `RunConfiguration`'s `WorkingDirectoryPath` should default to the detected language
  root (Epic 18's convention) or the project file's own directory when they differ (e.g. a
  `.csproj` nested several levels under the detected root) — **decided: the project file's own
  directory**, since `dotnet run --project <path>` resolves relative paths (e.g. referenced
  `appsettings.json`) against the project's directory, not an ancestor.
