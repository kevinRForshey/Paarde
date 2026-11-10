namespace Paarde.Presentation.Models;

// Navigation parameter from the History page to the Prompt page, to re-run or fork a past
// session. Carries the user's raw prompt text and working directory - not the fully composed
// prompt actually sent to the CLI (RunSession.Prompt) - so PromptViewModel's normal pipeline
// (instruction template, preset/depth, context files) applies fresh rather than being doubled up.
// AutoRun distinguishes "Re-run" (true: load and immediately send) from "Fork" (false: load into the
// text box for the user to edit before sending).
public record PromptLaunchRequest(string PromptText, string WorkingDirectoryPath, bool AutoRun);
