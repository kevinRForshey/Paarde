using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeWindowAppearanceService : IWindowAppearanceService
{
    public List<bool> AppliedTransparency { get; } = [];

    public void ApplyTransparency(bool enabled) => AppliedTransparency.Add(enabled);
}
