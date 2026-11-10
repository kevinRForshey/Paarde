using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

public class PromptTemplateProvider : IPromptTemplateProvider
{
    public const int LatestVersion = 1;

    private const string ResourceName = "Paarde.Core.Prompts.TutorialInstructionPromptV1.txt";

    private readonly Dictionary<string, string> _cache = [];

    public int CurrentVersion => LatestVersion;

    public string GetTutorialInstructionPrompt() => GetCached(ResourceName);

    public string GetPresetInstruction(PromptPreset preset) => preset switch
    {
        PromptPreset.General => string.Empty,
        PromptPreset.Feature => GetCached("Paarde.Core.Prompts.PresetFeatureV1.txt"),
        PromptPreset.Bugfix => GetCached("Paarde.Core.Prompts.PresetBugfixV1.txt"),
        PromptPreset.Refactor => GetCached("Paarde.Core.Prompts.PresetRefactorV1.txt"),
        PromptPreset.Explain => GetCached("Paarde.Core.Prompts.PresetExplainV1.txt"),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    public string GetDepthInstruction(TutorialDepth depth) => depth switch
    {
        TutorialDepth.Standard => string.Empty,
        TutorialDepth.Brief => GetCached("Paarde.Core.Prompts.DepthBriefV1.txt"),
        TutorialDepth.Detailed => GetCached("Paarde.Core.Prompts.DepthDetailedV1.txt"),
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, null),
    };

    private string GetCached(string resourceName)
    {
        if (!_cache.TryGetValue(resourceName, out var content))
        {
            content = ReadEmbeddedResource(resourceName);
            _cache[resourceName] = content;
        }

        return content;
    }

    private static string ReadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(PromptTemplateProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt template resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);

        // A Windows checkout (core.autocrlf) gives the template CRLF line endings; the prompt sent
        // to the model is the same on every OS.
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
