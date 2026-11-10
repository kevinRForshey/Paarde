using System.Diagnostics;

using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.Tests.TestSupport;
using Paarde.Core.ViewModels;

namespace Paarde.Core.Tests.Services;

// RunGit/RunGitCapture below spawn real `git` child processes via Process.Start - see
// RealProcessCollection.cs for why that requires this class to be serialized against the other
// real-process tests rather than left to xUnit's default cross-class parallelism.
[Collection("RealProcess")]
public class ApplyOrchestrationServiceTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _sessionsFolder;
    private readonly ApplyBackupStore _backupStore = new();
    private readonly ApplyHistoryStore _historyStore = new();
    private readonly ApplyHistoryLog _historyLog = new();
    private readonly PromptResultStore _promptResultStore = new();
    private readonly RunSessionStore _runSessionStore;
    private readonly ApplyOrchestrationService _service;

    public ApplyOrchestrationServiceTests()
    {
        _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
        _sessionsFolder = Directory.CreateTempSubdirectory("PaardeTests_Sessions_").FullName;
        _runSessionStore = new RunSessionStore(new FileService(), _promptResultStore, _historyLog, _sessionsFolder);
        _service = new ApplyOrchestrationService(new FileChangeApplyService(), _backupStore, _historyStore, _historyLog, new GitStatusService(), _runSessionStore);
    }

    public void Dispose()
    {
        TempDirectory.Delete(_workingDirectory);
        TempDirectory.Delete(_sessionsFolder);
    }

    [Fact]
    public void Preview_NoFilesSelected_ReturnsZero()
    {
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed", "original") { IsSelected = false } };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.Equal(new ApplyPreview(0, 0), preview);
    }

    [Fact]
    public void Preview_WholeFileFallbackSelected_CountsAsOneFileOneChange()
    {
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed", "original") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.Equal(new ApplyPreview(1, 1), preview);
    }

    [Fact]
    public void Preview_MixOfFilesWithSomeHunksSelected_CountsOnlySelectedHunks()
    {
        var hunks = new[]
        {
            new ProposedHunk("Step 1", string.Empty, "x", null),
            new ProposedHunk("Step 2", string.Empty, "y", null) { IsSelected = false },
        };
        var fileWithHunks = new ProposedFileChangeViewModel("a.txt", "proposed", "original") { Hunks = hunks };
        var fileExcluded = new ProposedFileChangeViewModel("b.txt", "proposed", "original") { IsSelected = false };

        var preview = _service.Preview(new[] { fileWithHunks, fileExcluded }, _workingDirectory);

        Assert.Equal(new ApplyPreview(1, 1), preview);
    }

    [Fact]
    public void ApplySelected_NothingSelected_WritesNothingAndReturnsNoResults()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original") { IsSelected = false };

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Empty(result.Results);
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void ApplySelected_NewFile_WritesItAndReportsApplied()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed content");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal("proposed content", File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void ApplySelected_MarksHunksAppliedAfterSuccessfulWrite()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "line1");
        var hunk = new ProposedHunk("Step 1", string.Empty, "line2", new HunkLocation(1, 1));
        var file = new ProposedFileChangeViewModel("a.txt", "line1\nline2", "line1") { Hunks = new[] { hunk } };

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.True(hunk.IsApplied);
    }

    [Fact]
    public void ApplySelected_FileUnchangedSinceProposal_WritesAndReportsApplied()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal("proposed", File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void ApplySelected_FileChangedOnDiskSinceProposal_ReportsConflictAndDoesNotWrite()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "changed on disk");
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Equal(1, result.ConflictCount);
        Assert.Equal("a.txt", Assert.Single(result.ConflictedFiles).FilePath);
        Assert.Equal("changed on disk", File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void ApplySelected_OneFileConflictsAnotherDoesNot_StillAppliesTheOther()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "conflicted.txt"), "changed on disk");
        var conflicted = new ProposedFileChangeViewModel("conflicted.txt", "proposed", "original");
        var clean = new ProposedFileChangeViewModel("clean.txt", "proposed content");

        var result = _service.ApplySelected(new[] { conflicted, clean }, _workingDirectory);

        Assert.Equal(1, result.ConflictCount);
        Assert.Equal(1, result.AppliedCount);
        Assert.True(File.Exists(Path.Combine(_workingDirectory, "clean.txt")));
    }

    [Fact]
    public void ApplySelected_PathTraversal_ReportsFailedAndDoesNotEscapeTheBatch()
    {
        var malicious = new ProposedFileChangeViewModel("../escape.txt", "malicious");
        var clean = new ProposedFileChangeViewModel("clean.txt", "proposed content");

        var result = _service.ApplySelected(new[] { malicious, clean }, _workingDirectory);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.AppliedCount);
        Assert.NotNull(Assert.Single(result.FailedFiles).ErrorMessage);
    }

    [Fact]
    public void Preview_FileUnderBinDirectory_ReportsProtectedPath()
    {
        var files = new[] { new ProposedFileChangeViewModel("bin/Debug/a.dll.txt", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.Equal(1, preview.ProtectedPathCount);
        Assert.Equal("bin/Debug/a.dll.txt", Assert.Single(preview.ProtectedPathFiles));
    }

    [Fact]
    public void Preview_FileMatchedByGitIgnore_ReportsProtectedPath()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, ".gitignore"), "*.log");
        var files = new[] { new ProposedFileChangeViewModel("output.log", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.Equal(1, preview.ProtectedPathCount);
        Assert.Equal("output.log", Assert.Single(preview.ProtectedPathFiles));
    }

    [Fact]
    public void Preview_NoProtectedFiles_ReportsZero()
    {
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.Equal(0, preview.ProtectedPathCount);
        Assert.Empty(preview.ProtectedPathFiles);
    }

    [Fact]
    public void Preview_WorkingDirectoryNotAGitRepository_ReportsNotARepositoryAndNoDirtyFiles()
    {
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.False(preview.IsGitRepository);
        Assert.Equal(0, preview.GitDirtyFileCount);
        Assert.Empty(preview.GitDirtyFiles);
    }

    [Fact]
    public void Preview_CleanGitRepository_ReportsIsRepositoryWithNoDirtyFiles()
    {
        InitRepository();
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "committed");
        CommitAll();
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.True(preview.IsGitRepository);
        Assert.Equal(0, preview.GitDirtyFileCount);
        Assert.Empty(preview.GitDirtyFiles);
    }

    [Fact]
    public void Preview_SelectedFileIsDirtyInGit_ReportsIt()
    {
        InitRepository();
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        CommitAll();
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "uncommitted edit");
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.True(preview.IsGitRepository);
        Assert.Equal(1, preview.GitDirtyFileCount);
        Assert.Equal("a.txt", Assert.Single(preview.GitDirtyFiles));
    }

    [Fact]
    public void Preview_DirtyFileNotAmongSelectedFiles_IsNotReported()
    {
        InitRepository();
        File.WriteAllText(Path.Combine(_workingDirectory, "unrelated.txt"), "uncommitted, never added to git");
        var files = new[] { new ProposedFileChangeViewModel("a.txt", "proposed content") };

        var preview = _service.Preview(files, _workingDirectory);

        Assert.True(preview.IsGitRepository);
        Assert.Equal(0, preview.GitDirtyFileCount);
        Assert.Empty(preview.GitDirtyFiles);
    }

    private void InitRepository() => RunGit("init");

    private void CommitAll()
    {
        RunGit("add", "-A");
        RunGit("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "test commit");
    }

    private void RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
        }
    }

    [Fact]
    public void ApplySelected_FileUnderGitDirectory_BlocksByDefaultAndDoesNotWrite()
    {
        var file = new ProposedFileChangeViewModel(".git/config", "malicious");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Equal(1, result.ProtectedPathBlockedCount);
        Assert.Equal(".git/config", Assert.Single(result.ProtectedPathBlockedFiles).FilePath);
        Assert.False(File.Exists(Path.Combine(_workingDirectory, ".git", "config")));
    }

    [Fact]
    public void ApplySelected_FileMatchedByGitIgnore_BlocksByDefault()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, ".gitignore"), "*.log");
        var file = new ProposedFileChangeViewModel("output.log", "proposed content");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Equal(1, result.ProtectedPathBlockedCount);
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "output.log")));
    }

    [Fact]
    public void ApplySelected_ProtectedPathWithAllowProtectedPathsTrue_WritesAnyway()
    {
        var file = new ProposedFileChangeViewModel("bin/output.txt", "proposed content");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory, allowProtectedPaths: true);

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal("proposed content", File.ReadAllText(Path.Combine(_workingDirectory, "bin", "output.txt")));
    }

    [Fact]
    public void ApplySelected_OneProtectedOneClean_StillAppliesTheCleanOne()
    {
        var protectedFile = new ProposedFileChangeViewModel("obj/generated.txt", "malicious");
        var clean = new ProposedFileChangeViewModel("clean.txt", "proposed content");

        var result = _service.ApplySelected(new[] { protectedFile, clean }, _workingDirectory);

        Assert.Equal(1, result.ProtectedPathBlockedCount);
        Assert.Equal(1, result.AppliedCount);
        Assert.True(File.Exists(Path.Combine(_workingDirectory, "clean.txt")));
    }

    [Fact]
    public void ApplySelected_ExistingFileFirstWrite_SnapshotsItsPreApplyContent()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");

        _service.ApplySelected(new[] { file }, _workingDirectory);

        var found = _backupStore.TryGetSnapshot("a.txt", out var snapshot);
        Assert.True(found);
        Assert.Equal("original", snapshot);
    }

    [Fact]
    public void ApplySelected_NewFileFirstWrite_SnapshotsEmptyContent()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed content");

        _service.ApplySelected(new[] { file }, _workingDirectory);

        var found = _backupStore.TryGetSnapshot("a.txt", out var snapshot);
        Assert.True(found);
        Assert.Equal(string.Empty, snapshot);
    }

    [Fact]
    public void ApplySelected_SecondWriteToSamePathInSameSession_DoesNotOverwriteTheFirstSnapshot()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        var firstFile = new ProposedFileChangeViewModel("a.txt", "first edit", "original");
        _service.ApplySelected(new[] { firstFile }, _workingDirectory);

        var secondFile = new ProposedFileChangeViewModel("a.txt", "second edit", "first edit");
        _service.ApplySelected(new[] { secondFile }, _workingDirectory);

        _backupStore.TryGetSnapshot("a.txt", out var snapshot);
        Assert.Equal("original", snapshot);
    }

    [Fact]
    public void ApplySelected_ConflictedFile_DoesNotCreateASnapshot()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "changed on disk");
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.False(_backupStore.TryGetSnapshot("a.txt", out _));
    }

    [Fact]
    public void ApplySelected_ProtectedPathBlockedFile_DoesNotCreateASnapshot()
    {
        var file = new ProposedFileChangeViewModel(".git/config", "malicious");

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.False(_backupStore.TryGetSnapshot(".git/config", out _));
    }

    [Fact]
    public void ApplySelected_NothingSelected_DoesNotAddAHistoryLogEntry()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original") { IsSelected = false };

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Empty(_historyLog.Entries);
    }

    [Fact]
    public void ApplySelected_FilesApplied_AddsAnAppliedHistoryLogEntryWithSucceededFilesOnly()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "conflicted.txt"), "changed on disk");
        var conflicted = new ProposedFileChangeViewModel("conflicted.txt", "proposed", "original");
        var clean = new ProposedFileChangeViewModel("clean.txt", "proposed content");

        _service.ApplySelected(new[] { conflicted, clean }, _workingDirectory);

        var entry = Assert.Single(_historyLog.Entries);
        Assert.Equal(ApplyHistoryEntryKind.Applied, entry.Kind);
        Assert.Equal("clean.txt", Assert.Single(entry.FilePaths));
    }

    // ApplySelected/TryUndoLastApply/RevertAllApplied each resave the active run session so
    // its persisted apply log stays current - verified end-to-end through the real RunSessionStore
    // rather than a mock, consistent with this test class's existing no-mocking convention.
    [Fact]
    public void ApplySelected_NothingSelected_DoesNotResaveTheSession()
    {
        _runSessionStore.RecordRun("prompt", "raw output", "user prompt");
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original") { IsSelected = false };

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Empty(ReadActiveSession().ApplyLog);
    }

    [Fact]
    public void ApplySelected_FilesApplied_ResavesTheSessionWithTheNewApplyLogEntry()
    {
        _runSessionStore.RecordRun("prompt", "raw output", "user prompt");
        var file = new ProposedFileChangeViewModel("clean.txt", "proposed content");

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Single(ReadActiveSession().ApplyLog);
    }

    [Fact]
    public void TryUndoLastApply_FileRestored_ResavesTheSessionWithTheUndoHistoryEntry()
    {
        _runSessionStore.RecordRun("prompt", "raw output", "user prompt");
        var file = new ProposedFileChangeViewModel("clean.txt", "proposed content");
        _service.ApplySelected(new[] { file }, _workingDirectory);

        _service.TryUndoLastApply(_workingDirectory, out _);

        Assert.Equal(2, ReadActiveSession().ApplyLog.Count);
    }

    [Fact]
    public void RevertAllApplied_FilesReverted_ResavesTheSessionWithTheRevertHistoryEntry()
    {
        _runSessionStore.RecordRun("prompt", "raw output", "user prompt");
        var file = new ProposedFileChangeViewModel("clean.txt", "proposed content");
        _service.ApplySelected(new[] { file }, _workingDirectory);

        _service.RevertAllApplied(_workingDirectory);

        Assert.Equal(2, ReadActiveSession().ApplyLog.Count);
    }

    [Fact]
    public void ApplySelected_FilesApplied_BeforeAnyRunWasRecorded_DoesNotThrowOrWriteASessionFile()
    {
        var file = new ProposedFileChangeViewModel("clean.txt", "proposed content");

        _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.Empty(Directory.GetFiles(_sessionsFolder));
    }

    private RunSession ReadActiveSession() =>
        new FileService().Read<RunSession>(_sessionsFolder, Path.GetFileName(Assert.Single(Directory.GetFiles(_sessionsFolder))))!;

    [Fact]
    public void TryUndoLastApply_NothingAppliedThisSession_ReturnsFalseWithEmptyResult()
    {
        var found = _service.TryUndoLastApply(_workingDirectory, out var result);

        Assert.False(found);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void TryUndoLastApply_OneFileApplied_RestoresItsPreApplyContent()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original");
        _service.ApplySelected(new[] { file }, _workingDirectory);

        var found = _service.TryUndoLastApply(_workingDirectory, out var result);

        Assert.True(found);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void TryUndoLastApply_TwoBatchesApplied_OnlyUndoesTheMostRecentOne()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "first edit", "original") }, _workingDirectory);
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "second edit", "first edit") }, _workingDirectory);

        _service.TryUndoLastApply(_workingDirectory, out _);

        Assert.Equal("first edit", File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void TryUndoLastApply_CalledTwiceAfterOneBatch_SecondCallReturnsFalse()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "proposed", "original") }, _workingDirectory);
        _service.TryUndoLastApply(_workingDirectory, out _);

        var found = _service.TryUndoLastApply(_workingDirectory, out var result);

        Assert.False(found);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void TryUndoLastApply_ConflictedOrBlockedFilesContributeNothingToTheBatch()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "conflicted.txt"), "changed on disk");
        var conflicted = new ProposedFileChangeViewModel("conflicted.txt", "proposed", "original");
        var clean = new ProposedFileChangeViewModel("clean.txt", "proposed content");
        _service.ApplySelected(new[] { conflicted, clean }, _workingDirectory);

        var found = _service.TryUndoLastApply(_workingDirectory, out var result);

        Assert.True(found);
        Assert.Equal("clean.txt", Assert.Single(result.Results).FilePath);
    }

    [Fact]
    public void TryUndoLastApply_WriteBackFails_ReportsFailureViaFailedCountAndFailedFiles()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            // Mirrors FileChangeApplyServiceTests' permission-failure guard - not reliably
            // reproducible on Windows ACLs or as root.
            return;
        }

        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "original");
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "proposed", "original") }, _workingDirectory);

        // Removing write permission on the file itself (not the directory) blocks WriteRaw's
        // overwrite without preventing Dispose()'s recursive directory delete afterward, which
        // only needs write permission on the containing directory.
        File.SetUnixFileMode(path, UnixFileMode.UserRead);

        var found = _service.TryUndoLastApply(_workingDirectory, out var result);

        Assert.True(found);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal("a.txt", Assert.Single(result.FailedFiles).FilePath);
    }

    [Fact]
    public void TryUndoLastApply_FileRestored_AddsAnUndoLastApplyHistoryLogEntry()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "proposed", "original") }, _workingDirectory);

        _service.TryUndoLastApply(_workingDirectory, out _);

        Assert.Equal(2, _historyLog.Entries.Count);
        var entry = _historyLog.Entries[1];
        Assert.Equal(ApplyHistoryEntryKind.UndoLastApply, entry.Kind);
        Assert.Equal("a.txt", Assert.Single(entry.FilePaths));
    }

    [Fact]
    public void TryUndoLastApply_NothingToUndo_DoesNotAddAHistoryLogEntry()
    {
        _service.TryUndoLastApply(_workingDirectory, out _);

        Assert.Empty(_historyLog.Entries);
    }

    [Fact]
    public void RevertAllApplied_NothingAppliedThisSession_ReturnsEmptyResult()
    {
        var result = _service.RevertAllApplied(_workingDirectory);

        Assert.Empty(result.Results);
    }

    [Fact]
    public void RevertAllApplied_FileAppliedMultipleTimes_RestoresTheSessionBaselineNotTheIntermediateEdit()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "first edit", "original") }, _workingDirectory);
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "second edit", "first edit") }, _workingDirectory);

        var result = _service.RevertAllApplied(_workingDirectory);

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
    }

    [Fact]
    public void RevertAllApplied_MultipleFilesApplied_RestoresEachOne()
    {
        var first = new ProposedFileChangeViewModel("a.txt", "proposed a");
        var second = new ProposedFileChangeViewModel("b.txt", "proposed b");
        _service.ApplySelected(new[] { first, second }, _workingDirectory);

        var result = _service.RevertAllApplied(_workingDirectory);

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(_workingDirectory, "a.txt")));
        Assert.Equal(string.Empty, File.ReadAllText(Path.Combine(_workingDirectory, "b.txt")));
    }

    [Fact]
    public void RevertAllApplied_AfterReverting_ClearsUndoHistory()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "a.txt"), "original");
        _service.ApplySelected(new[] { new ProposedFileChangeViewModel("a.txt", "proposed", "original") }, _workingDirectory);

        _service.RevertAllApplied(_workingDirectory);

        Assert.False(_service.TryUndoLastApply(_workingDirectory, out _));
    }

    [Fact]
    public void RevertAllApplied_FilesReverted_AddsARevertAllAppliedHistoryLogEntry()
    {
        var first = new ProposedFileChangeViewModel("a.txt", "proposed a");
        var second = new ProposedFileChangeViewModel("b.txt", "proposed b");
        _service.ApplySelected(new[] { first, second }, _workingDirectory);

        _service.RevertAllApplied(_workingDirectory);

        Assert.Equal(2, _historyLog.Entries.Count);
        var entry = _historyLog.Entries[1];
        Assert.Equal(ApplyHistoryEntryKind.RevertAllApplied, entry.Kind);
        Assert.Equal(new[] { "a.txt", "b.txt" }, entry.FilePaths.OrderBy(f => f));
    }

    [Fact]
    public void RevertAllApplied_NothingAppliedThisSession_DoesNotAddAHistoryLogEntry()
    {
        _service.RevertAllApplied(_workingDirectory);

        Assert.Empty(_historyLog.Entries);
    }

    [Fact]
    public void ApplySelected_GitRepository_StagesTheAppliedFile()
    {
        InitRepository();
        var file = new ProposedFileChangeViewModel("a.txt", "proposed content");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.True(result.FilesStagedInGit);
        Assert.Equal("a.txt", Assert.Single(RunGitCapture("diff", "--cached", "--name-only")));
    }

    [Fact]
    public void ApplySelected_StageAppliedFilesFalse_DoesNotStageTheAppliedFile()
    {
        InitRepository();
        var file = new ProposedFileChangeViewModel("a.txt", "proposed content");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory, stageAppliedFiles: false);

        Assert.False(result.FilesStagedInGit);
        Assert.Empty(RunGitCapture("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void ApplySelected_NotAGitRepository_ReportsFilesNotStaged()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed content");

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.False(result.FilesStagedInGit);
    }

    [Fact]
    public void ApplySelected_GitRepository_DoesNotStageAConflictedFile()
    {
        InitRepository();
        File.WriteAllText(Path.Combine(_workingDirectory, "conflicted.txt"), "changed on disk");
        var conflicted = new ProposedFileChangeViewModel("conflicted.txt", "proposed", "original");

        var result = _service.ApplySelected(new[] { conflicted }, _workingDirectory);

        Assert.False(result.FilesStagedInGit);
        Assert.Empty(RunGitCapture("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void ApplySelected_NothingSelected_DoesNotAttemptToStage()
    {
        InitRepository();
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original") { IsSelected = false };

        var result = _service.ApplySelected(new[] { file }, _workingDirectory);

        Assert.False(result.FilesStagedInGit);
    }

    private string[] RunGitCapture(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
        }

        return output
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
