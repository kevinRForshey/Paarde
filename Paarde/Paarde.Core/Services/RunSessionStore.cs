using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// Writes one JSON file per run under sessionsFolderPath via IFileService - append-only across runs
// (never overwrites a previous run's file) so a later history page (T7.2.x) has every past run to
// list, not just the latest. Not thread-safe: like IPromptResultStore/IApplyHistoryLog, every
// caller today is the UI thread (PromptViewModel.RunPromptAsync, ApplyOrchestrationService).
public class RunSessionStore : IRunSessionStore
{
    private readonly IFileService _fileService;
    private readonly IPromptResultStore _promptResultStore;
    private readonly IApplyHistoryLog _applyHistoryLog;
    private readonly string _sessionsFolderPath;

    // Holds exactly the fields a session needs that aren't readable from IPromptResultStore/
    // IApplyHistoryLog at Resave time (those two are always read fresh, see Save below) - grouped
    // into one record rather than loose nullable fields so there's no "is everything set yet"
    // null-forgiving operator anywhere: _active is either the one complete session in progress, or
    // there isn't one yet.
    private sealed record ActiveSession(string Id, DateTimeOffset StartedAt, string FileName, string Prompt, string RawOutput, string UserPromptText);

    private ActiveSession? _active;

    // Tiebreaks the filename when two runs land in the same millisecond (easily hit back-to-back,
    // e.g. in tests): a monotonic counter sorts by call order, whereas the guid id (random per run)
    // would sort filenames randomly within that millisecond instead.
    private static long _sequenceCounter;

    public RunSessionStore(IFileService fileService, IPromptResultStore promptResultStore, IApplyHistoryLog applyHistoryLog, string sessionsFolderPath)
    {
        _fileService = fileService;
        _promptResultStore = promptResultStore;
        _applyHistoryLog = applyHistoryLog;
        _sessionsFolderPath = sessionsFolderPath;
    }

    public void RecordRun(string prompt, string rawOutput, string userPromptText)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var sequence = Interlocked.Increment(ref _sequenceCounter);

        // Sortable timestamp prefix, then a monotonic sequence number, so files list in run order
        // without having to open and read StartedAt from each one; the guid suffix is just the run's
        // id and plays no role in ordering.
        var fileName = $"{startedAt:yyyyMMddHHmmssfff}_{sequence:D19}_{id}.json";

        _active = new ActiveSession(id, startedAt, fileName, prompt, rawOutput, userPromptText);
        Save(_active);
    }

    public void Resave()
    {
        if (_active is { } active)
        {
            Save(active);
        }
    }

    public RunSession? RestoreLastSession()
    {
        var latestFileName = GetSessionFileNamesNewestFirst().FirstOrDefault();

        if (latestFileName is null)
        {
            return null;
        }

        var session = _fileService.Read<RunSession>(_sessionsFolderPath, latestFileName);

        if (session is null)
        {
            return null;
        }

        _active = new ActiveSession(session.Id, session.StartedAt, latestFileName, session.Prompt, session.RawOutput, session.UserPromptText);

        var files = session.Files.Select(RunSessionFileConverter.ToProposedFileChangeViewModel).ToList();
        _promptResultStore.SetResult(session.Tutorial, files, session.WorkingDirectoryPath);
        _applyHistoryLog.Restore(session.ApplyLog);

        return session;
    }

    public IReadOnlyList<RunSession> ListSessions() => GetSessionFileNamesNewestFirst()
        .Select(fileName => _fileService.Read<RunSession>(_sessionsFolderPath, fileName))
        .OfType<RunSession>()
        .ToList();

    // Sortable filename prefix (see RecordRun) means lexicographic order is also run-started order -
    // no need to open every file to compare StartedAt. Newest first, since that's what both
    // RestoreLastSession and the History page's listing want.
    private IEnumerable<string> GetSessionFileNamesNewestFirst()
    {
        if (!Directory.Exists(_sessionsFolderPath))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.GetFiles(_sessionsFolderPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderByDescending(name => name, StringComparer.Ordinal);
    }

    // Reads Files/Tutorial/WorkingDirectoryPath and the apply log fresh on every call (rather than
    // caching them on RecordRun) so Resave always reflects whatever selections/applies have
    // happened since - the whole point of calling it again instead of leaving the RecordRun
    // snapshot in place.
    private void Save(ActiveSession active)
    {
        var session = new RunSession(
            active.Id,
            active.StartedAt,
            _promptResultStore.WorkingDirectoryPath,
            active.Prompt,
            active.RawOutput,
            _promptResultStore.Tutorial,
            _promptResultStore.Files.Select(RunSessionFileConverter.ToRunSessionFile).ToList(),
            _applyHistoryLog.Entries.ToList(),
            active.UserPromptText);

        _fileService.Save(_sessionsFolderPath, active.FileName, session);
    }
}
