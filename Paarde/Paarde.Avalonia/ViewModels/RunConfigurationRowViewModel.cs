using Paarde.Core.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Avalonia.ViewModels;

// specs/build-and-run/requirements.md R2.6: the escape hatch for every case detection gets wrong
// or doesn't cover - the user can add, edit, or delete a RunConfiguration by hand regardless of
// whether RunConfigurationDetector found anything. Same per-row-ViewModel-with-onChanged-callback
// shape as LanguageServerOptionsRowViewModel, plus a delete callback since that list's rows are
// fixed (one per known language) while this one's aren't.
public partial class RunConfigurationRowViewModel : ObservableObject
{
    private readonly string _id;
    private readonly Action<RunConfigurationRowViewModel> _onChanged;
    private readonly Action<RunConfigurationRowViewModel> _onDeleteRequested;

    private bool _isAutoDetected;

    public RunConfigurationRowViewModel(
        RunConfiguration configuration,
        Action<RunConfigurationRowViewModel> onChanged,
        Action<RunConfigurationRowViewModel> onDeleteRequested)
    {
        _id = configuration.Id;
        name = configuration.Name;
        languageId = configuration.LanguageId;
        workingDirectoryPath = configuration.WorkingDirectoryPath;
        projectRelativePath = configuration.ProjectRelativePath;
        buildCommand = configuration.BuildCommand;
        runCommand = configuration.RunCommand;
        _isAutoDetected = configuration.IsAutoDetected;
        _onChanged = onChanged;
        _onDeleteRequested = onDeleteRequested;
    }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string languageId;

    [ObservableProperty]
    private string workingDirectoryPath;

    [ObservableProperty]
    private string projectRelativePath;

    [ObservableProperty]
    private string buildCommand;

    [ObservableProperty]
    private string runCommand;

    // R2.3: once the user edits anything here, a future detection pass must leave this row
    // alone rather than silently overwriting it - surfaced in the row itself so the "auto" badge
    // in SettingsView disappears the moment that happens.
    public bool IsAutoDetected => _isAutoDetected;

    public RunConfiguration ToConfiguration() =>
        new(_id, Name, LanguageId, WorkingDirectoryPath, ProjectRelativePath, BuildCommand, RunCommand, _isAutoDetected);

    partial void OnNameChanged(string value) => MarkEditedAndNotify();

    partial void OnLanguageIdChanged(string value) => MarkEditedAndNotify();

    partial void OnWorkingDirectoryPathChanged(string value) => MarkEditedAndNotify();

    partial void OnProjectRelativePathChanged(string value) => MarkEditedAndNotify();

    partial void OnBuildCommandChanged(string value) => MarkEditedAndNotify();

    partial void OnRunCommandChanged(string value) => MarkEditedAndNotify();

    private void MarkEditedAndNotify()
    {
        if (_isAutoDetected)
        {
            _isAutoDetected = false;
            OnPropertyChanged(nameof(IsAutoDetected));
        }

        _onChanged(this);
    }

    [RelayCommand]
    private void Delete() => _onDeleteRequested(this);
}
