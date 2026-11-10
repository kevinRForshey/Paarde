using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;
using Paarde.Core.ViewModels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Core.Services;

// Drives "Apply Selected" across every file in a tutorial result, on top of
// IFileChangeApplyService's single-file write. Each file is handled independently: a conflict or a
// failed write on one file is recorded and the batch moves on to the next rather than stopping.
public class ApplyOrchestrationService : IApplyOrchestrationService
{
    private readonly IFileChangeApplyService _fileChangeApplyService;
    private readonly IApplyBackupStore _applyBackupStore;
    private readonly IApplyHistoryStore _applyHistoryStore;
    private readonly IApplyHistoryLog _applyHistoryLog;
    private readonly IGitStatusService _gitStatusService;
    private readonly IRunSessionStore _runSessionStore;

    // See GitStatusService's constructor remark - optional/defaulted so the many existing
    // direct-construction call sites (DI registration, every test) don't need to change.
    private readonly ILogger<ApplyOrchestrationService> _logger;

    public ApplyOrchestrationService(
        IFileChangeApplyService fileChangeApplyService,
        IApplyBackupStore applyBackupStore,
        IApplyHistoryStore applyHistoryStore,
        IApplyHistoryLog applyHistoryLog,
        IGitStatusService gitStatusService,
        IRunSessionStore runSessionStore,
        ILogger<ApplyOrchestrationService>? logger = null)
    {
        _fileChangeApplyService = fileChangeApplyService;
        _applyBackupStore = applyBackupStore;
        _applyHistoryStore = applyHistoryStore;
        _applyHistoryLog = applyHistoryLog;
        _gitStatusService = gitStatusService;
        _runSessionStore = runSessionStore;
        _logger = logger ?? NullLogger<ApplyOrchestrationService>.Instance;
    }

    public ApplyPreview Preview(IReadOnlyList<ProposedFileChangeViewModel> files, string workingDirectoryPath)
    {
        var filesToApply = files.Where(f => f.HasAnySelectedChange).ToList();

        // A whole-file fallback (no resolved hunks) counts as one change; otherwise count each
        // selected step individually, matching what the per-file SelectionSummary already shows.
        var changeCount = filesToApply.Sum(f => f.HasSelectableHunks ? f.SelectedHunkCount : 1);

        // Surfaced up front so the confirmation prompt can warn about protected targets
        // before ApplySelected skips them, rather than the user only finding out after the fact.
        var protectedPathFiles = filesToApply
            .Where(f => ProtectedPathChecker.IsProtected(workingDirectoryPath, f.FilePath))
            .Select(f => f.FilePath)
            .ToList();

        // Warn about files this apply would overwrite that git already sees as dirty, not
        // every dirty file in the repository - a stray unrelated uncommitted file elsewhere isn't
        // this apply's problem. GitStatus.NotARepository (no repo, git missing, or the call failed)
        // naturally yields IsGitRepository = false and an empty intersection below.
        var gitStatus = _gitStatusService.GetStatus(workingDirectoryPath);
        var dirtyFileSet = gitStatus.DirtyFiles.Select(Normalize).ToHashSet();
        var gitDirtyFiles = filesToApply
            .Select(f => f.FilePath)
            .Where(path => dirtyFileSet.Contains(Normalize(path)))
            .ToList();

        return new ApplyPreview(filesToApply.Count, changeCount)
        {
            ProtectedPathCount = protectedPathFiles.Count,
            // Array.Empty<string>() rather than the (possibly empty) List<string> above: record
            // equality falls back to reference equality for a non-IEquatable property like
            // IReadOnlyList<string>, and Array.Empty<string>() is a cached singleton, so this keeps
            // "no protected files" comparing equal to ApplyPreview.ProtectedPathFiles's own default
            // instead of failing Assert.Equal purely because one side is a List and the other an array.
            ProtectedPathFiles = protectedPathFiles.Count == 0 ? Array.Empty<string>() : protectedPathFiles,
            IsGitRepository = gitStatus.IsRepository,
            GitDirtyFileCount = gitDirtyFiles.Count,
            GitDirtyFiles = gitDirtyFiles.Count == 0 ? Array.Empty<string>() : gitDirtyFiles,
        };
    }

    private static string Normalize(string relativeFilePath) => relativeFilePath.Replace('\\', '/').TrimStart('/');

    public ApplyBatchResult ApplySelected(IReadOnlyList<ProposedFileChangeViewModel> files, string workingDirectoryPath, bool allowProtectedPaths = false, bool stageAppliedFiles = true)
    {
        var results = new List<FileApplyResult>();
        var appliedChanges = new List<AppliedFileChange>();

        foreach (var file in files.Where(f => f.HasAnySelectedChange))
        {
            var (result, preApplyContent) = ApplyOne(file, workingDirectoryPath, allowProtectedPaths);
            results.Add(result);

            if (preApplyContent is not null)
            {
                appliedChanges.Add(new AppliedFileChange(file.FilePath, preApplyContent));
            }
        }

        // RecordBatch/Record both no-op for an empty list, so a batch that applied nothing (every
        // file conflicted, was blocked, or failed) doesn't push an undo entry, a log entry, or a
        // pointless resave of an unchanged session.
        _applyHistoryStore.RecordBatch(appliedChanges);
        _applyHistoryLog.Record(ApplyHistoryEntryKind.Applied, appliedChanges.Select(c => c.FilePath).ToList());

        var filesStagedInGit = false;

        if (appliedChanges.Count > 0)
        {
            _runSessionStore.Resave();

            // Stage exactly the files this batch actually wrote - never the whole working
            // tree - so the user's next `git status` already reflects what Paarde just applied.
            // Fails open (same posture as GitStatusService.GetStatus): not a repository, or git
            // isn't installed, just means nothing gets staged, it never blocks or undoes the apply
            // that already succeeded. stageAppliedFiles lets the caller opt out entirely (surfaced
            // as a checkbox in the UI) without touching that fail-open behavior.
            if (stageAppliedFiles)
            {
                filesStagedInGit = _gitStatusService.StageFiles(workingDirectoryPath, appliedChanges.Select(c => c.FilePath).ToList());
            }
        }

        return new ApplyBatchResult(results) { FilesStagedInGit = filesStagedInGit };
    }

    // Undoes the most recently applied batch by writing each of its files' pre-apply content back
    // to disk. False (with an empty result) if nothing's been applied this session, or
    // everything applied has already been undone.
    public bool TryUndoLastApply(string workingDirectoryPath, out RevertResult result)
    {
        if (!_applyHistoryStore.TryPopLastBatch(out var changes))
        {
            result = new RevertResult(Array.Empty<RevertFileResult>());
            return false;
        }

        result = WriteBack(changes, workingDirectoryPath);
        var succeededPaths = result.Results.Where(r => r.Succeeded).Select(r => r.FilePath).ToList();
        _applyHistoryLog.Record(ApplyHistoryEntryKind.UndoLastApply, succeededPaths);

        if (succeededPaths.Count > 0)
        {
            _runSessionStore.Resave();
        }

        return true;
    }

    // Restores every file this session has touched back to its pre-apply baseline (T4.2.1's
    // session-wide snapshot, not just the most recent batch). Clears the undo history afterward -
    // once every tracked file is back at its original content, none of the previously recorded
    // batches describe a state worth returning to.
    public RevertResult RevertAllApplied(string workingDirectoryPath)
    {
        var changes = _applyBackupStore.AllSnapshots
            .Select(kvp => new AppliedFileChange(kvp.Key, kvp.Value))
            .ToList();

        var result = WriteBack(changes, workingDirectoryPath);
        _applyHistoryStore.Clear();
        var succeededPaths = result.Results.Where(r => r.Succeeded).Select(r => r.FilePath).ToList();
        _applyHistoryLog.Record(ApplyHistoryEntryKind.RevertAllApplied, succeededPaths);

        if (succeededPaths.Count > 0)
        {
            _runSessionStore.Resave();
        }

        return result;
    }

    private RevertResult WriteBack(IReadOnlyList<AppliedFileChange> changes, string workingDirectoryPath)
    {
        var results = new List<RevertFileResult>();

        foreach (var change in changes)
        {
            try
            {
                _fileChangeApplyService.WriteRaw(change.FilePath, change.ContentBeforeApply, workingDirectoryPath);
                results.Add(new RevertFileResult(change.FilePath, Succeeded: true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Failed to write {FilePath} back to {WorkingDirectory} while reverting.", change.FilePath, workingDirectoryPath);
                results.Add(new RevertFileResult(change.FilePath, Succeeded: false, ex.Message));
            }
        }

        return new RevertResult(results);
    }

    private (FileApplyResult Result, string? PreApplyContent) ApplyOne(ProposedFileChangeViewModel file, string workingDirectoryPath, bool allowProtectedPaths)
    {
        // Checked before touching disk at all - unlike Conflict, there's nothing to re-diff
        // and retry here, so this short-circuits ahead of the read/write below rather than sharing
        // their try/catch.
        if (!allowProtectedPaths && ProtectedPathChecker.IsProtected(workingDirectoryPath, file.FilePath))
        {
            return (new FileApplyResult(file.FilePath, FileApplyOutcome.ProtectedPathBlocked), null);
        }

        string fullPath;
        string currentDiskContent;

        try
        {
            // Read what's actually on disk right now, not file.OriginalContent (the baseline this
            // proposal was diffed against) - if the file changed since then (someone edited it
            // outside the app, or a previous apply already wrote part of it), writing over it would
            // silently discard that. FilePathContainment.Resolve enforces the same "stay inside the
            // project folder" rule the write itself will, so this read can't be used to probe
            // outside it either.
            fullPath = FilePathContainment.Resolve(workingDirectoryPath, file.FilePath);
            currentDiskContent = FileContentReader.ReadOrEmpty(fullPath);
        }
        catch (InvalidOperationException ex)
        {
            // The path-containment check (or a blank working directory) rejected it - treat the same
            // as any other failed write rather than letting it escape the batch or the read.
            _logger.LogWarning(ex, "Failed to read {FilePath} ahead of applying it in {WorkingDirectory}.", file.FilePath, workingDirectoryPath);
            return (new FileApplyResult(file.FilePath, FileApplyOutcome.Failed, ex.Message), null);
        }

        if (currentDiskContent != file.OriginalContent)
        {
            // Don't write - the caller can offer file.RefreshOriginalContent(workingDirectoryPath) to
            // re-diff against what's actually there and let the user decide again.
            return (new FileApplyResult(file.FilePath, FileApplyOutcome.Conflict), null);
        }

        try
        {
            // Delegates the actual write to IFileChangeApplyService rather than writing
            // BuildSelectedContent() to fullPath directly here, so there's exactly one place that
            // knows how selection turns into bytes on disk and marks each hunk's IsApplied
            // afterward - this orchestration only decides which files get attempted and
            // reports what happened to each.
            _fileChangeApplyService.Apply(file, workingDirectoryPath);

            // Recorded only after a write actually succeeds - a conflicted, blocked, or
            // failed attempt never touched the file, so there's nothing to back up. SnapshotIfAbsent
            // is a no-op past the first successful write to this path, so the backup always reflects
            // what was on disk before this session touched the file at all, not an intermediate state.
            _applyBackupStore.SnapshotIfAbsent(file.FilePath, currentDiskContent);

            return (new FileApplyResult(file.FilePath, FileApplyOutcome.Applied), currentDiskContent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to apply {FilePath} in {WorkingDirectory}.", file.FilePath, workingDirectoryPath);
            return (new FileApplyResult(file.FilePath, FileApplyOutcome.Failed, ex.Message), null);
        }
    }
}
