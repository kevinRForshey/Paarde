using Paarde.Core.Helpers;
using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace Paarde.Avalonia.ViewModels;

// specs/multi-language-support/design.md "Settings UI": one row per configured language, bound to
// LanguageServerOptionsProvider's collection (R6.1) rather than a fixed count, so a language added
// later to LanguageServerOptions.Defaults needs no XAML change. Edits here save the whole
// collection back through SettingsViewModel - see OnExecutablePathChanged/OnArgumentsChanged.
public partial class LanguageServerOptionsRowViewModel : ObservableObject
{
    private readonly Action<LanguageServerOptionsRowViewModel> _onChanged;

    public LanguageServerOptionsRowViewModel(LanguageServerOptions options, Action<LanguageServerOptionsRowViewModel> onChanged)
    {
        LanguageId = options.LanguageId;
        executablePath = options.ExecutablePath;
        arguments = options.Arguments;
        _onChanged = onChanged;
        RefreshIsAvailable();
    }

    public string LanguageId { get; }

    [ObservableProperty]
    private string executablePath;

    [ObservableProperty]
    private string arguments;

    // R6.2: a simple PATH-existence preflight, same check StdioLanguageServerClient.IsAvailableAsync
    // performs - no process is spawned just to populate this indicator.
    [ObservableProperty]
    private bool isAvailable;

    public LanguageServerOptions ToOptions() => new(LanguageId, ExecutablePath, Arguments);

    partial void OnExecutablePathChanged(string value)
    {
        RefreshIsAvailable();
        _onChanged(this);
    }

    partial void OnArgumentsChanged(string value) => _onChanged(this);

    private void RefreshIsAvailable() => IsAvailable = ExecutablePathResolver.ExistsOnPath(ExecutablePath);
}
