using Paarde.Core.Models;
using Paarde.Core.ViewModels;

using Newtonsoft.Json;

namespace Paarde.Core.Helpers;

// Parses the JSON response contract requested by PromptViewModel.TutorialInstructionPrompt (Epic 5 /
// F5.1) - a single JSON object, either wrapped in commentary/fences or raw, matching ClaudeJsonResponse.
// TryParse never throws: any extraction/deserialization/validation failure comes back as a false
// return plus a human-readable `error`, which PromptViewModel feeds back to the model on a one-shot
// retry before giving up and handing the raw text to the legacy markdown parser.
//
// A file's "edits" (search/replace blocks) are applied against the real file at
// workingDirectoryPath/path as part of validation, not mapping - an edit whose "search" text can't be
// matched is indistinguishable from a truncated/stale response, so it's treated as a schema failure
// and fed back on the same retry path as any other validation error, rather than silently
// producing a half-applied file.
public static class ClaudeJsonResponseParser
{
    public static bool TryParse(string cliOutput, out ClaudeParsedResponse? response, out string? error, string? workingDirectoryPath = null)
    {
        response = null;

        var jsonText = ExtractJsonObject(cliOutput);

        if (jsonText is null)
        {
            error = "No JSON object was found in the response.";
            return false;
        }

        ClaudeJsonResponse? parsed;

        try
        {
            parsed = JsonConvert.DeserializeObject<ClaudeJsonResponse>(jsonText);
        }
        catch (JsonException ex)
        {
            error = $"The response was not valid JSON: {ex.Message}";
            return false;
        }

        // Defensive: JsonConvert.DeserializeObject<T> only returns null for the literal "null", which
        // ExtractJsonObject can never produce (it always returns a balanced `{...}` substring) -
        // unreachable in practice, left untested rather than forcing a contrived repro.
        if (parsed is null)
        {
            error = "The response's JSON object could not be deserialized.";
            return false;
        }

        if (!TryValidate(parsed, workingDirectoryPath, out var resolvedEdits, out error))
        {
            return false;
        }

        response = Map(parsed, resolvedEdits);
        return true;
    }

    // Resolves and applies each file's "edits" (if any) as part of validation, since checking whether
    // an edit's "search" text can even be matched requires reading the real file - the result is
    // handed to Map so it isn't computed (and the file re-read) a second time.
    private static bool TryValidate(ClaudeJsonResponse response, string? workingDirectoryPath, out Dictionary<ClaudeJsonFile, ResolvedFileEdits> resolvedEdits, out string? error)
    {
        resolvedEdits = [];
        var responseType = response.ResponseType?.Trim().ToLowerInvariant();

        if (responseType != "answer" && responseType != "development")
        {
            error = "\"responseType\" must be either \"answer\" or \"development\".";
            return false;
        }

        if (responseType == "answer")
        {
            if (string.IsNullOrWhiteSpace(response.Answer))
            {
                error = "\"answer\" is required and must be non-empty when \"responseType\" is \"answer\".";
                return false;
            }

            error = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(response.Tutorial))
        {
            error = "\"tutorial\" is required and must be non-empty when \"responseType\" is \"development\".";
            return false;
        }

        foreach (var file in response.Files ?? [])
        {
            if (string.IsNullOrWhiteSpace(file.Path))
            {
                error = "Every entry in \"files\" must have a non-empty \"path\".";
                return false;
            }

            var edits = file.Edits ?? [];

            if (edits.Count > 0)
            {
                foreach (var edit in edits)
                {
                    if (string.IsNullOrWhiteSpace(edit.Title))
                    {
                        error = $"An edit in file \"{file.Path}\" is missing a non-empty \"title\".";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(edit.Search))
                    {
                        error = $"An edit in file \"{file.Path}\" is missing a non-empty \"search\".";
                        return false;
                    }
                }

                var originalContent = string.IsNullOrWhiteSpace(workingDirectoryPath)
                    ? string.Empty
                    : FileContentReader.ReadOrEmpty(Path.Combine(workingDirectoryPath, file.Path.Trim()));

                var applied = SearchReplaceEditApplier.Apply(originalContent, edits);
                var failedEdit = edits.Zip(applied.Outcomes).FirstOrDefault(pair => !pair.Second.Success).First;

                if (failedEdit is not null)
                {
                    error = $"The \"search\" text for edit \"{failedEdit.Title}\" in file \"{file.Path}\" could not be matched to that file's current content - it may be incomplete, or the file may have changed since the request was made.";
                    return false;
                }

                resolvedEdits[file] = new ResolvedFileEdits(originalContent, applied);
                continue;
            }

            if (file.Content is null)
            {
                error = $"File \"{file.Path}\" is missing \"content\".";
                return false;
            }

            foreach (var step in file.Steps ?? [])
            {
                if (string.IsNullOrWhiteSpace(step.Title))
                {
                    error = $"A step in file \"{file.Path}\" is missing a non-empty \"title\".";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    private static ClaudeParsedResponse Map(ClaudeJsonResponse response, Dictionary<ClaudeJsonFile, ResolvedFileEdits> resolvedEdits)
    {
        if (response.ResponseType!.Trim().Equals("answer", StringComparison.OrdinalIgnoreCase))
        {
            return new ClaudeParsedResponse(response.Answer!.Trim(), []);
        }

        var files = (response.Files ?? [])
            .Select(file => resolvedEdits.TryGetValue(file, out var resolved)
                ? MapEditModeFile(file, resolved)
                : MapContentModeFile(file))
            .ToList();

        return new ClaudeParsedResponse(response.Tutorial!.Trim(), files);
    }

    private static ProposedFileChangeViewModel MapContentModeFile(ClaudeJsonFile file)
    {
        var content = file.Content ?? string.Empty;

        return new ProposedFileChangeViewModel(file.Path!.Trim(), content)
        {
            Hunks = (file.Steps ?? [])
                .Select(step =>
                {
                    var snippet = step.Snippet ?? string.Empty;

                    return new ProposedHunk(
                        step.Title!.Trim(),
                        step.Explanation?.Trim() ?? string.Empty,
                        snippet,
                        HunkLocationResolver.Resolve(content, snippet));
                })
                .ToList(),
        };
    }

    // Edit-mode already read the file's real current content while resolving its edits during
    // validation, so OriginalContent is set here immediately instead of waiting for the UI to lazily
    // read it later (ProposedFileChangeViewModel.RefreshOriginalContent) - the diff is correct the
    // moment the response is parsed.
    private static ProposedFileChangeViewModel MapEditModeFile(ClaudeJsonFile file, ResolvedFileEdits resolved)
    {
        return new ProposedFileChangeViewModel(file.Path!.Trim(), resolved.Applied.Content)
        {
            OriginalContent = resolved.OriginalContent,
            Hunks = file.Edits!
                .Zip(resolved.Applied.Outcomes)
                .Select(pair => new ProposedHunk(
                    pair.First.Title!.Trim(),
                    pair.First.Explanation?.Trim() ?? string.Empty,
                    pair.First.Replace ?? string.Empty,
                    pair.Second.Location))
                .ToList(),
        };
    }

    // Bundles one edit-mode file's real on-disk content (read during validation) with the already-
    // applied result of its edits, so Map doesn't need to re-read the file or re-run the edits.
    private record ResolvedFileEdits(string OriginalContent, EditApplicationResult Applied);

    // Scans for the first top-level `{ ... }` object in the text, skipping over braces that appear
    // inside JSON string values (e.g. a file's "content" is full of literal code with its own braces)
    // so it isn't fooled into closing early. Tolerates surrounding commentary or markdown fences,
    // since a model asked for "JSON only" will still sometimes wrap it in prose or a ```json block.
    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escapeNext = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                }
                else if (c == '\\')
                {
                    escapeNext = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }
}
