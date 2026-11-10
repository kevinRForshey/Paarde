using Paarde.Avalonia.Views;
using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Services;

public class WindowAppearanceService : IWindowAppearanceService
{
    public void ApplyTransparency(bool enabled) => (App.MainWindow as MainWindow)?.ApplyTransparency(enabled);
}
