namespace Paarde.Presentation.Contracts.Services;

public interface IDialogService
{
    // Shows a two-choice confirmation dialog and resolves once the user picks one. True means the
    // user chose the (destructive/primary) confirmButtonText action; false covers both explicitly
    // choosing cancelButtonText and dismissing the dialog any other way.
    Task<bool> ConfirmAsync(string title, string message, string confirmButtonText, string cancelButtonText);

    // Shows a single-button informational dialog (e.g. an apply-results report) and resolves once
    // the user dismisses it. Nothing to confirm or cancel, so there's no bool to return.
    Task InfoAsync(string title, string message, string closeButtonText);
}
