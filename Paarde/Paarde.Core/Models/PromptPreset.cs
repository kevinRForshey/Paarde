namespace Paarde.Core.Models;

// Named presets that append extra guidance onto the instruction preamble (default or
// user-overridden, T5.3.2) ahead of the user's own prompt text. General is the no-op default -
// it adds no extra instruction text.
public enum PromptPreset
{
    General,
    Feature,
    Bugfix,
    Refactor,
    Explain,
}
