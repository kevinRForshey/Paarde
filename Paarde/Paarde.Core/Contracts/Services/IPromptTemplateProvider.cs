using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// Separates "what instructions get sent to the LLM" from PromptViewModel so the template can be
// edited (and, eventually, overridden per T5.3.2) without touching ViewModel code, and so a new
// contract revision can ship as a new versioned resource rather than mutating the one every past
// response was parsed against.
public interface IPromptTemplateProvider
{
    // The template version that matches the JSON schema ClaudeJsonResponseParser expects today;
    // bump this (and add a new TutorialInstructionPromptV{N}.txt) whenever that schema changes.
    int CurrentVersion { get; }

    string GetTutorialInstructionPrompt();

    // Extra guidance appended after the preamble (default or user-overridden) and before
    // the depth instruction/user prompt. PromptPreset.General returns "" - it adds no extra text.
    string GetPresetInstruction(PromptPreset preset);

    // Extra guidance controlling how much explanation text each step should contain.
    // TutorialDepth.Standard returns "" - it relies on the base preamble's own wording.
    string GetDepthInstruction(TutorialDepth depth);
}
