using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Services;

public class HeadroomPromptSnoozeStore : IHeadroomPromptSnoozeStore
{
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(24);

    private readonly ILocalSettingsService _localSettingsService;

    public HeadroomPromptSnoozeStore(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task<bool> IsSnoozedAsync(HeadroomPromptKind kind)
    {
        var snoozedUntil = await _localSettingsService.ReadSettingAsync<DateTimeOffset?>(KeyFor(kind));

        return snoozedUntil is not null && snoozedUntil > DateTimeOffset.UtcNow;
    }

    public Task SnoozeAsync(HeadroomPromptKind kind) =>
        _localSettingsService.SaveSettingAsync(KeyFor(kind), (DateTimeOffset?)(DateTimeOffset.UtcNow + SnoozeDuration));

    private static string KeyFor(HeadroomPromptKind kind) => kind switch
    {
        HeadroomPromptKind.Install => HeadroomSettingsKeys.InstallPromptSnoozedUntilUtc,
        HeadroomPromptKind.Wiring => HeadroomSettingsKeys.WiringPromptSnoozedUntilUtc,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
