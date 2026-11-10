namespace Paarde.Core.Models;

// One result from ICodebaseIndexService.FindRelevantFilesAsync - RelativePath matches
// the same working-directory-relative shape FileMentionMatcher already returns for the @mention
// popup, so both flows can feed the same PromptViewModel.AttachMentionedFile-style attach path.
public sealed record RelevantFileMatch(string RelativePath, double Score);
