# Paarde
Pronounced like Party, but with a soft t like pardy.


**P**rompt **A**ssist **A**nd **R**eview **D**evelopment **E**nvironment

Paarde is a desktop app that sits between you and the [Claude Code CLI](https://github.com/anthropics/claude-code).
You describe a feature, fix, or new project in plain English; Claude Code does the work; Paarde turns
its response into a readable, step-by-step tutorial and lets you review, edit, and selectively apply
the proposed changes to your project before anything touches disk.

It's built for learning *with* an LLM, not just accepting its output blindly — every change comes
with an explanation, and nothing is written to your files until you choose to apply it.

It has two front ends over the same view models and services:

- **Paarde.Avalonia** — runs on Windows, Linux, and macOS.
- **Paarde.WinUI** — a native Windows (WinUI 3) app with a Monaco code editor.

## Quick start

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Claude Code CLI, installed and
logged in. The code lives in the [`Paarde/`](Paarde) folder:

```bash
cd Paarde

# Any platform
dotnet build Paarde.slnx
dotnet run --project Paarde.Avalonia

# Windows only: the solution that also builds the WinUI app
dotnet build Paarde.Windows.slnx
dotnet run --project Paarde.WinUI
```

## More

- [`Paarde/README.md`](Paarde/README.md) — the full guide: prerequisites, building, testing, and
  how to use each page.
- [`Paarde/ROADMAP.md`](Paarde/ROADMAP.md) — what's implemented, what's partial, and what's planned.

## License

Licensed under the [Apache License, Version 2.0](LICENSE).
