# Paarde

**P**rompt **A**ssist **A**nd **R**eview **D**evelopment **E**nvironment

Paarde is a desktop app that sits between you and the [Claude Code CLI](https://github.com/anthropics/claude-code).
You describe a feature, fix, or new project in plain English; Claude Code does the work; Paarde turns
its response into a readable, step-by-step tutorial and lets you review, edit, and selectively apply
the proposed changes to your project before anything touches disk.

It's built for learning *with* an LLM, not just accepting its output blindly — every change comes
with an explanation, and nothing is written to your files until you choose to apply it.

## What it does

1. **Prompt** — point Paarde at a project folder, describe what you want, optionally attach context
   files, and send it to Claude Code.
2. **Tutorial** — Claude's response comes back as a plain-language explanation of what changed and
   why, broken down per file (and, where possible, per logical step within a file) rather than a
   wall of text.
3. **Diffs** — see exactly what changed, line by line, against what's currently on disk.
4. **Review and edit** — select or deselect individual files or hunks, edit the proposed code directly
   in-app before it's written, and apply only what you want.
5. **Apply, undo, revert** — apply the selected changes to disk, undo the last apply, or revert
   everything the current session has applied — all without leaving the app.
6. **Explore** — browse your whole project tree, gitignore-aware, with files touched by the current
   proposal marked; open any file, proposed or not, in a read-only preview. For C#/.NET projects, an
   optional background language server adds live diagnostics, go-to-definition, and find-references
   to that preview.
7. **History** — every run is saved. Browse past runs, re-run one as-is, or fork it (reload its
   prompt and working directory for editing) to continue down a different path.

## Why Avalonia (cross-platform by design)

Paarde is built on [Avalonia UI](https://avaloniaui.net/) rather than a Windows-only framework like
WinUI/WPF. Avalonia renders its own UI instead of wrapping each OS's native widget set, so the same
XAML views and C# view models run unmodified on **Windows, Linux, and macOS** — there's one codebase,
not a native app per platform. [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) layers a
Fluent/WinUI-style look (navigation shell, icons, dialogs) on top of that, so it looks and feels like
a modern desktop app on any of the three.

## Technology stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| UI framework | [Avalonia](https://avaloniaui.net/) 11.3.x (`Paarde.Avalonia`) |
| UI styling/controls | [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) (NavigationView shell, Fluent controls) |
| Code editor | [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) + [AvaloniaEdit.TextMate](https://github.com/AvaloniaUI/AvaloniaEdit) + [TextMateSharp.Grammars](https://github.com/danipen/TextMateSharp) for syntax-highlighted, editable code boxes |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) |
| DI / hosting | `Microsoft.Extensions.Hosting` generic host, wired up in `Paarde.Avalonia/App.axaml.cs` |
| Domain/shared logic | `Paarde.Core` — parsing, diffing, apply orchestration, session persistence; UI-framework-agnostic so it's unit-testable without Avalonia |
| LLM integration | Shells out to the `claude` CLI (`Paarde.Core/Services/ClaudeCliRunner.cs`), streaming NDJSON (`stream-json`) for live progress |
| Code intelligence | Shells out to a Language Server Protocol server over stdio (`Paarde.Core/Services/StdioLanguageServerClient.cs`, `csharp-ls` by default) for Explorer's diagnostics/go-to-definition/find-references |
| Tests | xUnit (`Paarde.Core.Tests`, `Paarde.Avalonia.Tests`) |

### Project layout

```
Paarde.slnx
├─ Paarde.Core/            Domain models, services, view models shared by the UI (no Avalonia dependency)
├─ Paarde.Core.Tests/      Unit tests for Paarde.Core
├─ Paarde.Avalonia/        The desktop app: views, view models, DI setup, Program.cs entry point
└─ Paarde.Avalonia.Tests/  Unit tests for Paarde.Avalonia view models
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The [Claude Code CLI](https://github.com/anthropics/claude-code) installed and authenticated:
  ```
  npm install -g @anthropic-ai/claude-code
  claude login
  ```
  Paarde shells out to this CLI; it needs to be on your `PATH` (or pointed to explicitly — see
  Settings below) and already logged in.
- *(Optional)* [`csharp-ls`](https://github.com/razzmatazz/csharp-language-server) for Explorer's
  diagnostics/go-to-definition/find-references on C#/.NET projects:
  ```
  dotnet tool install --global csharp-ls
  ```
  Not required to run Paarde — if it's missing, those three features are simply inactive and
  everything else works as normal.

## Building and running

```bash
# From the repository root
dotnet build Paarde.slnx

# Run the desktop app
dotnet run --project Paarde.Avalonia

# Run the test suites
dotnet test Paarde.Core.Tests
dotnet test Paarde.Avalonia.Tests
```

Paarde runs the same way on Windows, Linux, and macOS — there's no platform-specific build step.

## Usage

On the Explorer, Diffs, Tutorial, and History pages, drag the divider between a browse-list pane
(file tree, file list, or run list) and the pane next to it to resize it; dragging it all the way
to one side collapses that pane. Each page remembers its own pane widths between visits.

### 1. Main page

The landing page when Paarde opens: a quick "Getting started" walkthrough of the Prompt → Tutorial/
Diffs → Apply → Settings flow for first-time users, a **quick prompt** box that hands straight off
to the Prompt page, and - once you've run at least one prompt - **Recent runs** (reopen a past
run's prompt and working directory to edit before sending) and **Recent projects** (switch the
Prompt page's working directory to one you've used before) lists.

### 2. Prompt page

- **Project location** — click **Browse...** to choose the folder Claude Code should treat as its
  working directory. This is the folder it reads from and writes to. The **Recent...** dropdown
  next to it switches straight to a previously-used working directory without leaving this page.
- **Context files** — optionally attach specific files or a whole folder as extra context for the
  model, beyond what's in the working directory. You can also attach a file by typing `@` followed
  by part of its name directly in the prompt text box — a popup lists matching files (gitignore-aware)
  for you to pick from. **Suggest relevant files** ranks every file in the working directory against
  your prompt text (keyword matching, not a cloud embeddings call — no API key or extra setup needed)
  and lists candidates below; double-click one to attach it, or **Dismiss** to ignore the suggestions.
- **Prompt text** — describe what you want built, fixed, or changed, in plain English. A live
  estimated token count is shown above the box.
- **Preset** — a short instruction nudge appended ahead of your prompt: `General`, `Feature`,
  `Bugfix`, `Refactor`, or `Explain`.
- **Tutorial detail** — controls how deep the generated explanation goes: `Standard`, `Brief`, or
  `Detailed`.
- **Timeout** — how long to wait for the CLI before giving up.
- **Continue previous conversation** — resume the last run's Claude Code session instead of starting
  a new one, so a follow-up prompt has the prior turn's context.
- Click **Send to Claude Code** to run it. A progress ring, elapsed time, and a running character
  count show that a response is streaming in; once it completes you're told how many files are ready
  to review. **Cancel** stops an in-flight run.
- **Tool activity** — if **Enable read-only agent tools** is on (Settings page), a small collapsible
  list appears under the progress indicator as Claude reads, greps, or globs the project mid-run
  (e.g. "Reading `Foo.cs`"). Hidden when the setting is off or no tool has been used yet.

### 3. Explorer page

Browse the full file tree of your current working directory — not just the files touched by a
proposal. The tree is gitignore-aware (it matches your project's root `.gitignore` the same way
apply-time safety checks do) and always excludes `.git`. Files touched by the current proposal are
marked with a small accent dot next to their name. Select any file, proposed or not, to see its
content in a read-only, syntax-highlighted preview on the right.

If `csharp-ls` is installed (see Prerequisites), the preview also gets live code intelligence for
C#/.NET projects:

- **Diagnostics** — compiler errors and warnings are underlined as you browse; hover one to see the
  message.
- **Go to Definition** — press **F12** with the caret on a symbol to jump to where it's defined,
  even in another file.
- **Find References** — press **Shift+F12** to list every place the symbol under the caret is used,
  in a small popup; click one to jump there.

### 4. Diffs page

Pick a file from the list on the left to see a color-coded line-by-line diff against what's
currently on disk (or, for a brand-new file, the whole thing shown as additions). **Side-by-side**
toggles between that inline view and an old/new two-column layout. **Copy file contents** copies
the proposed content to your clipboard. If you arrived here by jumping from a tutorial step (see
below), a **Back to Tutorial** button returns you to that exact step.

### 5. Tutorial page

This is where you actually decide what to keep:

- An **Overview** section at the top shows Claude's plain-language summary of the whole response,
  when one was given.
- Explanations and overview text render basic Markdown — bold/italic emphasis, `code` spans, and
  bullet/numbered lists — instead of flat text.
- Pick a file from the list to open it as a **tab** with its explanation and an **editable** code
  box — you can tweak Claude's proposed content before anything is applied. Picking more files
  opens more tabs, so you can switch between several without losing your place; a `*` on a tab
  marks unsaved edits, and **Close Tab** closes the active one.
- Where the response breaks a file's change into discrete steps, each shows its own numbered title
  ("Step N:"), explanation, a checkbox to include/exclude it, and **Apply this change** / **Explain
  further** / **View in Diff** buttons. A progress bar plus Previous/Next buttons above the steps
  list let you step through them in order; **View in Diff** jumps straight to that step's hunk on
  the Diffs page.
- Use **Select all / Select none / Invert selection** per file, or check/uncheck a file in the list
  to include or exclude it entirely.
- **Undo** / **Redo** revert your in-editor edits; **Revert to proposed** discards your edits back to
  Claude's output; **Revert to original** discards both, back to what's on disk right now.
- **Apply Selected** writes every selected change, across every selected file, to disk (with a
  confirmation prompt, and a warning if it would touch files with uncommitted git changes).
- **Undo Last Apply** restores files to their content from right before the most recent apply.
- **Revert All Applied** restores every file this session has touched back to its content from
  before the session started.
- **Refine selected file** sends a scoped follow-up instruction that only asks Claude to change the
  currently selected file, continuing the same conversation.
- **Export as Markdown** / **Export as HTML** save the overview and every file's steps as a
  self-contained document you can share or archive outside the app.

### 6. Terminal page

Run an arbitrary shell command against your project without leaving the app — the same
shell-out-and-capture mechanism Epic 12's build/test feedback loop uses, generalized to whatever
command you type.

- **What it is** — a scoped-down "run a command, stream its output" panel, not a full interactive
  terminal. It routes your command through a real shell (`/bin/sh -c` on Linux/macOS, `cmd.exe /c`
  on Windows) in the working directory last picked on the Prompt page, so `&&`-chains, pipes, and
  PATH resolution all work the way they would in an actual shell.
- **How to use it** — type a command (e.g. `dotnet test`, `git log`, `npm install`) and click
  **Run**. Output streams into the pane below as the command produces it, not just once it exits.
  **Cancel** stops an in-flight command (and kills the underlying process, not just the UI's wait
  for it). **Clear** empties the output pane without affecting anything running.
- **Limits** — this is deliberately not a real terminal/PTY:
  - **No input.** There's no way to type back to the running process, so anything that needs
    interactive input will hang or fail — `vim`, `htop`, pagers like `less`/`more`, `git commit`
    without `-m` (which tries to open an editor), a password prompt, a Python REPL, an `npm init`
    wizard, and so on.
  - **No ANSI/escape-code rendering.** Output is shown as plain text, so color codes, progress
    bars, or cursor-movement sequences will show as raw escape characters rather than rendering.
  - **No state carries over between runs.** Each **Run** starts a brand-new shell process, so a
    `cd` or an exported environment variable from one command is gone by the next — there's no
    persistent session the way a real terminal window has.

  Real, interactive terminal use still belongs in an external terminal window for now; a true PTY
  (wrapping `Pty.Net`/ConPTY) is a larger, separate piece of work than this scoped-down version.

### 7. History page

Every run is persisted automatically. Select a past session from the list to see its files; use
**Fork** to reload that run's prompt and working directory onto the Prompt page for editing, or
**Re-run** to send it again immediately.

### 8. Settings page

- **Theme** — Light, Dark, or Default (follow the OS).
- **Claude Code CLI** — override the executable path and arguments if `claude` isn't on your `PATH`
  or you need custom flags.
- **Enable read-only agent tools** — lets Claude read files, search (grep), and list files (glob) in
  your project itself while it's generating a response, instead of only seeing whatever context you
  attached or Paarde inlined. It still can't write files or run commands — those stay off. Off by
  default, since this changes what the model can *do* mid-run, not just how it's invoked; check this
  box on the Settings page to turn it on. It applies to whichever LLM runner (Claude Code CLI or
  Anthropic API) is currently selected above it. Using it with the Claude Code CLI runner requires a
  CLI build that supports `--allowedTools`/`--permission-mode` — run `claude --version`, and if
  turning this on causes an error, upgrade with `npm install -g @anthropic-ai/claude-code`.
- **Prompt template** — view or override the instruction preamble sent ahead of every prompt;
  **Reset to default** restores the built-in template.
- **Code intelligence** — override the language server's executable path and arguments if
  `csharp-ls` isn't on your `PATH`, or swap in a different LSP-speaking server entirely.

## Project status

Paarde is under active development. See [ROADMAP.md](ROADMAP.md) for a detailed, honest breakdown
of what's implemented, what's partial, and what's planned next.
