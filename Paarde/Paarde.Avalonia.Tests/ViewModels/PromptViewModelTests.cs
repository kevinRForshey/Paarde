using Paarde.Avalonia.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;
using Paarde.Core.Helpers;
using Paarde.Core.Models;
using Paarde.Core.Services;

namespace Paarde.Avalonia.Tests.ViewModels;

public class PromptViewModelTests
{
    private readonly FakeLlmRunner _llmRunner = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeFolderPickerService _folderPickerService = new();
    private readonly FakeFilePickerService _filePickerService = new();
    private readonly FakePromptResultStore _promptResultStore = new();
    private readonly PromptTemplateProvider _promptTemplateProvider = new();
    private readonly FakeRunSessionStore _runSessionStore = new();
    private readonly FakeCodebaseIndexService _codebaseIndexService = new();
    private readonly FakeWorkingDirectoryContext _workingDirectoryContext = new();

    private PromptViewModel CreateSut() =>
        new(_llmRunner, _localSettingsService, _folderPickerService, _filePickerService, _promptResultStore, _promptTemplateProvider, _runSessionStore, _codebaseIndexService, _workingDirectoryContext);

    [Fact]
    public async Task RunPromptAsync_SuccessWithNoFiles_StoresTutorialAndReportsCompletion()
    {
        var sut = CreateSut();
        sut.PromptText = "Explain recursion";
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "Recursion is when a function calls itself."}""",
        };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.False(sut.IsRunning);
        Assert.Equal("Claude Code finished.", sut.StatusMessage);
        Assert.Equal(1, _promptResultStore.SetResultCallCount);
        Assert.Empty(_promptResultStore.Files);
    }

    [Fact]
    public async Task RunPromptAsync_SuccessWithFiles_ReportsFileCountAndStoresFiles()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        const string output =
            """
            {
              "responseType": "development",
              "tutorial": "Adds a Foo class.",
              "files": [
                { "path": "Foo.cs", "content": "class Foo {}", "steps": [] }
              ]
            }
            """;
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = output };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal("Claude Code finished. 1 file(s) ready to review on the Diffs page.", sut.StatusMessage);
        Assert.Single(_promptResultStore.Files);
        Assert.Equal("Foo.cs", _promptResultStore.Files[0].FilePath);
    }

    [Fact]
    public async Task RunPromptAsync_Success_RecordsTheRunWithThePromptActuallySentAndTheRawOutput()
    {
        var sut = CreateSut();
        sut.PromptText = "Explain recursion";
        const string output = """{"responseType": "answer", "answer": "Recursion is when a function calls itself."}""";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = output };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(1, _runSessionStore.RecordRunCallCount);
        Assert.Contains("Explain recursion", _runSessionStore.LastRecordedPrompt);
        Assert.Equal(output, _runSessionStore.LastRecordedRawOutput);
        Assert.Equal("Explain recursion", _runSessionStore.LastRecordedUserPromptText);
    }

    [Fact]
    public async Task RunPromptAsync_NonZeroExitCode_StillRecordsTheRun()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = false, Output = "partial output", ExitCode = 1 };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(1, _runSessionStore.RecordRunCallCount);
        Assert.Equal("partial output", _runSessionStore.LastRecordedRawOutput);
    }

    [Fact]
    public async Task RunPromptAsync_RunnerNotAvailable_DoesNotRecordARun()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        _llmRunner.AvailableToReturn = false;

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(0, _runSessionStore.RecordRunCallCount);
    }

    [Fact]
    public async Task RunPromptAsync_ResponseWithEdits_AppliesThemAgainstTheRealWorkingDirectoryFile()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo\n{\n    int Bar() => 1;\n}");

            var sut = CreateSut();
            sut.PromptText = "Change Bar to return 2";
            sut.WorkingDirectoryPath = workingDirectory;
            const string output =
                """
                {
                  "responseType": "development",
                  "tutorial": "Changes Bar to return 2.",
                  "files": [
                    {
                      "path": "Foo.cs",
                      "edits": [
                        { "title": "Change Bar", "search": "    int Bar() => 1;", "replace": "    int Bar() => 2;" }
                      ]
                    }
                  ]
                }
                """;
            _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = output };

            await sut.RunPromptCommand.ExecuteAsync(null);

            Assert.Equal("Claude Code finished. 1 file(s) ready to review on the Diffs page.", sut.StatusMessage);
            var file = Assert.Single(_promptResultStore.Files);
            Assert.Equal("class Foo\n{\n    int Bar() => 2;\n}", file.ProposedContent);
            Assert.Equal("class Foo\n{\n    int Bar() => 1;\n}", file.OriginalContent);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunPromptAsync_NonZeroExitCode_ReportsFailureAndDoesNotStoreResult()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = false, ExitCode = 1, ErrorMessage = "boom" };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.False(sut.IsRunning);
        Assert.Equal("Claude Code exited with code 1.", sut.StatusMessage);
        Assert.Equal("boom", sut.CliOutput);
        Assert.Equal(0, _promptResultStore.SetResultCallCount);
    }

    [Fact]
    public async Task RunPromptAsync_LlmRunnerThrows_ReportsErrorAndResetsIsRunning()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ExceptionToThrow = new InvalidOperationException("claude not found");

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.False(sut.IsRunning);
        Assert.Equal("Failed to run Claude Code: claude not found", sut.StatusMessage);
        Assert.Equal(0, _promptResultStore.SetResultCallCount);
    }

    [Fact]
    public async Task RunPromptAsync_PassesInstructionPreambleAndWorkingDirectoryToLlmRunner()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        sut.WorkingDirectoryPath = "/tmp/my-project";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal("/tmp/my-project", _llmRunner.LastWorkingDirectory);
        Assert.Contains("Add a Foo class", _llmRunner.LastPrompt);
        Assert.Contains("The working directory is: /tmp/my-project", _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task RunPromptAsync_NoWorkingDirectory_DoesNotAppendWorkingDirectoryLine()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.DoesNotContain("The working directory is:", _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task RunPromptAsync_NoTemplateOverrideSaved_UsesTheProvidersDefaultTemplate()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.StartsWith(_promptTemplateProvider.GetTutorialInstructionPrompt(), _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task RunPromptAsync_TemplateOverrideSaved_UsesTheOverrideInsteadOfTheDefault()
    {
        await _localSettingsService.SaveSettingAsync("PromptTemplateOverride", "Custom system instructions.");
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.StartsWith("Custom system instructions.", _llmRunner.LastPrompt);
        Assert.DoesNotContain(_promptTemplateProvider.GetTutorialInstructionPrompt(), _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task RunPromptAsync_BlankTemplateOverrideSaved_FallsBackToTheDefault()
    {
        await _localSettingsService.SaveSettingAsync("PromptTemplateOverride", "   ");
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.StartsWith(_promptTemplateProvider.GetTutorialInstructionPrompt(), _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task CancelPromptAsync_WhileRunning_CancelsTheRunAndReportsCancelled()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.CompleteImmediately = false;

        var runTask = sut.RunPromptCommand.ExecuteAsync(null);
        while (!sut.IsRunning)
        {
            await Task.Yield();
        }

        Assert.True(sut.CancelPromptCommand.CanExecute(null));
        sut.CancelPromptCommand.Execute(null);
        await runTask;

        Assert.False(sut.IsRunning);
        Assert.Equal("Cancelled.", sut.StatusMessage);
        Assert.True(_llmRunner.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void CanCancelPrompt_NotRunning_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.CancelPromptCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancelPromptCommand_CannotExecuteAfterRunCompletes()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.False(sut.CancelPromptCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunPromptAsync_TimeoutElapses_ReportsTimeoutMessageAndCancelsTheRun()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        sut.TimeoutSeconds = 0;
        _llmRunner.CompleteImmediately = false;

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.False(sut.IsRunning);
        Assert.Equal("Timed out after 0 second(s). You can increase the timeout on the Settings page.", sut.StatusMessage);
        Assert.True(_llmRunner.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void TimeoutSeconds_DefaultsTo300()
    {
        var sut = CreateSut();

        Assert.Equal(300, sut.TimeoutSeconds);
    }

    [Fact]
    public async Task TimeoutSeconds_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.TimeoutSeconds = 600;

        Assert.Equal(600, await _localSettingsService.ReadSettingAsync<int>("PromptTimeoutSeconds"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedTimeoutSeconds()
    {
        await _localSettingsService.SaveSettingAsync("PromptTimeoutSeconds", 120);
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal(120, sut.TimeoutSeconds);
    }

    [Fact]
    public void SelectedPreset_DefaultsToGeneral()
    {
        var sut = CreateSut();

        Assert.Equal(PromptPreset.General, sut.SelectedPreset);
    }

    [Fact]
    public void SelectedDepth_DefaultsToStandard()
    {
        var sut = CreateSut();

        Assert.Equal(TutorialDepth.Standard, sut.SelectedDepth);
    }

    [Fact]
    public async Task SelectedPreset_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.SelectedPreset = PromptPreset.Bugfix;

        Assert.Equal("Bugfix", await _localSettingsService.ReadSettingAsync<string>("SavedPromptPreset"));
    }

    [Fact]
    public async Task SelectedDepth_WhenChanged_PersistsToSettings()
    {
        var sut = CreateSut();
        sut.SelectedDepth = TutorialDepth.Brief;

        Assert.Equal("Brief", await _localSettingsService.ReadSettingAsync<string>("SavedTutorialDepth"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedPresetAndDepth()
    {
        await _localSettingsService.SaveSettingAsync("SavedPromptPreset", "Refactor");
        await _localSettingsService.SaveSettingAsync("SavedTutorialDepth", "Detailed");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal(PromptPreset.Refactor, sut.SelectedPreset);
        Assert.Equal(TutorialDepth.Detailed, sut.SelectedDepth);
    }

    [Fact]
    public async Task RunPromptAsync_GeneralPresetAndStandardDepth_AddsNoExtraInstructions()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "ok"}""",
        };

        await sut.RunPromptCommand.ExecuteAsync(null);

        var expectedPrompt = _promptTemplateProvider.GetTutorialInstructionPrompt() + "\n\n" + sut.PromptText;
        Assert.Equal(expectedPrompt, _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task RunPromptAsync_PresetSelected_AppendsPresetInstructionToPrompt()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        sut.SelectedPreset = PromptPreset.Feature;

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Contains(_promptTemplateProvider.GetPresetInstruction(PromptPreset.Feature), _llmRunner.LastPrompt);
    }

    [Fact]
    public async Task RunPromptAsync_DepthSelected_AppendsDepthInstructionToPrompt()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        sut.SelectedDepth = TutorialDepth.Brief;

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Contains(_promptTemplateProvider.GetDepthInstruction(TutorialDepth.Brief), _llmRunner.LastPrompt);
    }

    [Fact]
    public void AttachedPaths_DefaultsToEmpty()
    {
        var sut = CreateSut();

        Assert.Empty(sut.AttachedPaths);
    }

    [Fact]
    public async Task AddContextFilesCommand_AddsPickedFilesAndPersistsThem()
    {
        var sut = CreateSut();
        _filePickerService.FilesToReturn = ["/tmp/a.cs", "/tmp/b.cs"];

        await sut.AddContextFilesCommand.ExecuteAsync(null);

        Assert.Equal(["/tmp/a.cs", "/tmp/b.cs"], sut.AttachedPaths);
        Assert.Equal(["/tmp/a.cs", "/tmp/b.cs"], await _localSettingsService.ReadSettingAsync<List<string>>("SavedContextPaths"));
    }

    [Fact]
    public async Task AddContextFilesCommand_PickedFileAlreadyAttached_DoesNotDuplicateIt()
    {
        var sut = CreateSut();
        _filePickerService.FilesToReturn = ["/tmp/a.cs"];
        await sut.AddContextFilesCommand.ExecuteAsync(null);

        await sut.AddContextFilesCommand.ExecuteAsync(null);

        Assert.Equal(["/tmp/a.cs"], sut.AttachedPaths);
    }

    [Fact]
    public async Task AddContextFolderCommand_AddsPickedFolderAndPersistsIt()
    {
        var sut = CreateSut();
        _folderPickerService.FolderToReturn = "/tmp/my-folder";

        await sut.AddContextFolderCommand.ExecuteAsync(null);

        Assert.Equal(["/tmp/my-folder"], sut.AttachedPaths);
        Assert.Equal(["/tmp/my-folder"], await _localSettingsService.ReadSettingAsync<List<string>>("SavedContextPaths"));
    }

    [Fact]
    public async Task AddContextFolderCommand_NoFolderPicked_DoesNotAddAnything()
    {
        var sut = CreateSut();
        _folderPickerService.FolderToReturn = null;

        await sut.AddContextFolderCommand.ExecuteAsync(null);

        Assert.Empty(sut.AttachedPaths);
    }

    [Fact]
    public void CanRemoveAttachedPath_NoSelection_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.RemoveAttachedPathCommand.CanExecute(null));
    }

    [Fact]
    public async Task RemoveAttachedPathCommand_RemovesTheSelectedPathAndPersists()
    {
        var sut = CreateSut();
        _filePickerService.FilesToReturn = ["/tmp/a.cs", "/tmp/b.cs"];
        await sut.AddContextFilesCommand.ExecuteAsync(null);
        sut.SelectedAttachedPath = "/tmp/a.cs";

        Assert.True(sut.RemoveAttachedPathCommand.CanExecute(null));
        await sut.RemoveAttachedPathCommand.ExecuteAsync(null);

        Assert.Equal(["/tmp/b.cs"], sut.AttachedPaths);
        Assert.Null(sut.SelectedAttachedPath);
        Assert.Equal(["/tmp/b.cs"], await _localSettingsService.ReadSettingAsync<List<string>>("SavedContextPaths"));
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresSavedContextPaths()
    {
        await _localSettingsService.SaveSettingAsync("SavedContextPaths", new List<string> { "/tmp/a.cs", "/tmp/b.cs" });
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal(["/tmp/a.cs", "/tmp/b.cs"], sut.AttachedPaths);
    }

    [Fact]
    public async Task RunPromptAsync_AttachedFile_EmbedsItsContentInThePrompt()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempFile, "public class Foo {}");

            var sut = CreateSut();
            sut.PromptText = "Explain this file";
            sut.AttachedPaths.Add(tempFile);

            await sut.RunPromptCommand.ExecuteAsync(null);

            Assert.Contains("Additional context files:", _llmRunner.LastPrompt);
            Assert.Contains(tempFile, _llmRunner.LastPrompt);
            Assert.Contains("public class Foo {}", _llmRunner.LastPrompt);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunPromptAsync_AttachedFolder_EmbedsOnlyItsTopLevelFilesContent()
    {
        var folder = Directory.CreateTempSubdirectory("PaardeTests_");

        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "a.cs"), "class A {}");
            var nestedFolder = folder.CreateSubdirectory("nested");
            File.WriteAllText(Path.Combine(nestedFolder.FullName, "b.cs"), "class B {}");

            var sut = CreateSut();
            sut.PromptText = "Explain these files";
            sut.AttachedPaths.Add(folder.FullName);

            await sut.RunPromptCommand.ExecuteAsync(null);

            Assert.Contains("class A {}", _llmRunner.LastPrompt);
            Assert.DoesNotContain("class B {}", _llmRunner.LastPrompt);
        }
        finally
        {
            Directory.Delete(folder.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunPromptAsync_NoAttachedPaths_DoesNotAddContextFilesHeader()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Additional context files:", _llmRunner.LastPrompt);
    }

    [Fact]
    public void EstimatedTokenCountDisplay_DefaultsToEmpty()
    {
        var sut = CreateSut();

        Assert.Equal(string.Empty, sut.EstimatedTokenCountDisplay);
    }

    [Fact]
    public async Task OnNavigatedTo_PopulatesEstimatedTokenCountDisplay()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Contains("tokens", sut.EstimatedTokenCountDisplay);
    }

    [Fact]
    public async Task PromptText_WhenChanged_UpdatesEstimatedTokenCountDisplay()
    {
        var sut = CreateSut();
        sut.PromptText = "short";
        await Task.Yield();
        var shortDisplay = sut.EstimatedTokenCountDisplay;

        sut.PromptText = string.Concat(Enumerable.Repeat("much longer prompt text ", 50));
        await Task.Yield();

        Assert.NotEqual(shortDisplay, sut.EstimatedTokenCountDisplay);
    }

    [Fact]
    public async Task AttachedPaths_WhenChanged_UpdatesEstimatedTokenCountDisplay()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            File.WriteAllText(tempFile, string.Concat(Enumerable.Repeat("some context content ", 50)));

            var sut = CreateSut();
            await Task.Yield();
            var beforeDisplay = sut.EstimatedTokenCountDisplay;

            sut.AttachedPaths.Add(tempFile);
            await Task.Yield();

            Assert.NotEqual(beforeDisplay, sut.EstimatedTokenCountDisplay);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void UpdateMentionSuggestions_MatchingQuery_PopulatesMentionSuggestions()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo {}");
            File.WriteAllText(Path.Combine(workingDirectory, "Bar.cs"), "class Bar {}");

            var sut = CreateSut();
            sut.WorkingDirectoryPath = workingDirectory;

            sut.UpdateMentionSuggestions("Foo");

            Assert.Equal(["Foo.cs"], sut.MentionSuggestions);
            Assert.True(sut.HasMentionSuggestions);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public void UpdateMentionSuggestions_NoWorkingDirectory_LeavesMentionSuggestionsEmpty()
    {
        var sut = CreateSut();

        sut.UpdateMentionSuggestions("Foo");

        Assert.Empty(sut.MentionSuggestions);
        Assert.False(sut.HasMentionSuggestions);
    }

    [Fact]
    public void CloseMentionPopup_ClearsMentionSuggestions()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo {}");

            var sut = CreateSut();
            sut.WorkingDirectoryPath = workingDirectory;
            sut.UpdateMentionSuggestions("Foo");

            sut.CloseMentionPopup();

            Assert.Empty(sut.MentionSuggestions);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AttachMentionedFile_ResolvesTheRelativePathAgainstWorkingDirectoryAndAttachesIt()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;

        try
        {
            File.WriteAllText(Path.Combine(workingDirectory, "Foo.cs"), "class Foo {}");

            var sut = CreateSut();
            sut.WorkingDirectoryPath = workingDirectory;
            sut.UpdateMentionSuggestions("Foo");

            sut.AttachMentionedFile("Foo.cs");
            await Task.Yield();

            Assert.Equal([Path.Combine(workingDirectory, "Foo.cs")], sut.AttachedPaths);
            Assert.Empty(sut.MentionSuggestions);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task EstimatedTokenCountDisplay_MatchesTheExactPromptThatWouldBeSent()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        await Task.Yield();
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "ok"}""",
        };

        await sut.RunPromptCommand.ExecuteAsync(null);

        var expected = TokenEstimator.EstimateTokenCount(_llmRunner.LastPrompt!);
        Assert.Contains($"{expected:N0}", sut.EstimatedTokenCountDisplay);
    }

    [Fact]
    public async Task RunPromptAsync_RunnerNotAvailable_ReportsActionableMessageAndDoesNotRun()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.AvailableToReturn = false;

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.False(sut.IsRunning);
        Assert.Equal("The selected runner isn't available. Check its settings on the Settings page.", sut.StatusMessage);
        Assert.Equal(0, _llmRunner.CallCount);
    }

    [Fact]
    public async Task RunPromptAsync_InvalidJsonThenCorrectedJsonOnRetry_UsesTheRetriedResultWithoutFallbackNotice()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        _llmRunner.ResultsToReturnInOrder = new Queue<LlmRunResult>(
        [
            new LlmRunResult { Success = true, Output = "Here you go, no JSON today." },
            new LlmRunResult
            {
                Success = true,
                Output = """{"responseType": "development", "tutorial": "Adds a Foo class.", "files": [{"path": "Foo.cs", "content": "class Foo {}", "steps": []}]}""",
            },
        ]);

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(2, _llmRunner.CallCount);
        Assert.Equal("Claude Code finished. 1 file(s) ready to review on the Diffs page.", sut.StatusMessage);
        Assert.Single(_promptResultStore.Files);
        Assert.Equal("Foo.cs", _promptResultStore.Files[0].FilePath);
    }

    [Fact]
    public async Task RunPromptAsync_RetryPrompt_IncludesTheOriginalRequestAndTheValidationError()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        _llmRunner.ResultsToReturnInOrder = new Queue<LlmRunResult>(
        [
            new LlmRunResult { Success = true, Output = "not json" },
            new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "ok"}""" },
        ]);

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(2, _llmRunner.AllPrompts.Count);
        Assert.Contains("Add a Foo class", _llmRunner.AllPrompts[1]);
        Assert.Contains("No JSON object was found", _llmRunner.AllPrompts[1]);
    }

    [Fact]
    public async Task RunPromptAsync_InvalidJsonOnBothAttempts_FallsBackToMarkdownParsingAndNotesIt()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        const string markdown =
            """
            ## Tutorial
            Adds a Foo class.

            ## Files
            ### File: Foo.cs
            ```
            class Foo {}
            ```
            """;
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = markdown };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(2, _llmRunner.CallCount);
        Assert.Contains("wasn't valid structured JSON", sut.StatusMessage);
        Assert.Single(_promptResultStore.Files);
        Assert.Equal("Foo.cs", _promptResultStore.Files[0].FilePath);
    }

    [Fact]
    public async Task RunPromptAsync_RetryCallFails_KeepsTheFirstAttemptsFallbackParsedResult()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        const string markdown =
            """
            ## Tutorial
            Adds a Foo class.

            ## Files
            ### File: Foo.cs
            ```
            class Foo {}
            ```
            """;
        _llmRunner.ResultsToReturnInOrder = new Queue<LlmRunResult>(
        [
            new LlmRunResult { Success = true, Output = markdown },
            new LlmRunResult { Success = false, ExitCode = 1, ErrorMessage = "retry failed" },
        ]);

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(2, _llmRunner.CallCount);
        Assert.Contains("wasn't valid structured JSON", sut.StatusMessage);
        Assert.Single(_promptResultStore.Files);
        Assert.Equal(markdown, sut.CliOutput);
    }

    [Fact]
    public async Task RunPromptAsync_RetryOccurs_SumsCostAndTokensAcrossBothCalls()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ResultsToReturnInOrder = new Queue<LlmRunResult>(
        [
            new LlmRunResult { Success = true, Output = "not json", TotalCostUsd = 0.001, InputTokens = 10, OutputTokens = 5, DurationMs = 100 },
            new LlmRunResult
            {
                Success = true,
                Output = """{"responseType": "answer", "answer": "ok"}""",
                TotalCostUsd = 0.002,
                InputTokens = 20,
                OutputTokens = 8,
                DurationMs = 200,
            },
        ]);

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Contains("0.0030", sut.UsageSummary);
        Assert.Contains("30", sut.UsageSummary);
        Assert.Contains("13", sut.UsageSummary);
    }

    [Fact]
    public void CanRunPrompt_EmptyPromptText_CannotExecute()
    {
        var sut = CreateSut();
        sut.PromptText = string.Empty;

        Assert.False(sut.RunPromptCommand.CanExecute(null));
    }

    [Fact]
    public void CanRunPrompt_NonEmptyPromptText_CanExecute()
    {
        var sut = CreateSut();
        sut.PromptText = "Hello";

        Assert.True(sut.RunPromptCommand.CanExecute(null));
    }

    [Fact]
    public async Task SavePromptAsync_PersistsPromptTextAndSetsStatus()
    {
        var sut = CreateSut();
        sut.PromptText = "Remember me";

        await sut.SavePromptCommand.ExecuteAsync(null);

        Assert.Equal("Prompt saved.", sut.StatusMessage);
        Assert.Equal("Remember me", await _localSettingsService.ReadSettingAsync<string>("SavedPrompt"));
    }

    [Fact]
    public async Task BrowseWorkingDirectoryAsync_FolderPicked_UpdatesAndPersistsPath()
    {
        var sut = CreateSut();
        _folderPickerService.FolderToReturn = "/home/user/project";

        await sut.BrowseWorkingDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("/home/user/project", sut.WorkingDirectoryPath);
        Assert.Equal("/home/user/project", await _localSettingsService.ReadSettingAsync<string>("SavedWorkingDirectory"));
    }

    [Fact]
    public async Task BrowseWorkingDirectoryAsync_NoFolderPicked_LeavesPathUnchanged()
    {
        var sut = CreateSut();
        sut.WorkingDirectoryPath = "/existing/path";
        _folderPickerService.FolderToReturn = null;

        await sut.BrowseWorkingDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("/existing/path", sut.WorkingDirectoryPath);
    }

    [Fact]
    public async Task RunPromptAsync_StreamedTextDeltas_AppendToCliOutputAsTheyArrive()
    {
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        _llmRunner.TextDeltasToEmit = new[] { "Hello", ", world" };
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "Hello, world" };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal("Hello, world", sut.CliOutput);
    }

    [Fact]
    public async Task RunPromptAsync_StreamedTextDeltas_DoNotReparseOrStoreUntilTheRunFinishes()
    {
        // Each delta used to re-parse the whole growing CliOutput and push it into
        // IPromptResultStore (an O(n^2) cost under token-level streaming that froze the UI on
        // large responses) - it's now a cheap, UI-thread-only character-count bump (not exercised
        // here, since nothing pumps Dispatcher.UIThread in this plain xunit host), and the store is
        // only ever written once, from the complete output after the run finishes.
        var sut = CreateSut();
        sut.PromptText = "Add a Foo class";
        const string streamed =
            """
            ## Tutorial
            Adds a Foo class.

            ## Files

            ### File: Foo.cs
            ```
            class Foo {}
            ```
            """;
        _llmRunner.TextDeltasToEmit = new[] { streamed };
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = streamed };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(1, _promptResultStore.SetResultCallCount);
        Assert.Single(_promptResultStore.Files);
    }

    [Fact]
    public void ToolActivityLog_DefaultsToEmpty()
    {
        var sut = CreateSut();

        Assert.False(sut.HasToolActivity);
        Assert.Empty(sut.ToolActivityLog);
    }

    [Fact]
    public void HasToolActivity_ReflectsWhetherToolActivityLogHasEntries()
    {
        var sut = CreateSut();

        sut.ToolActivityLog.Add("Reading `Foo.cs`");

        Assert.True(sut.HasToolActivity);

        sut.ToolActivityLog.Clear();

        Assert.False(sut.HasToolActivity);
    }

    [Fact]
    public async Task RunPromptAsync_PriorToolActivityLogEntries_ClearsThemAtTheStartOfTheRun()
    {
        // Epic 11 (F11.2.2): onToolActivity dispatches through Dispatcher.UIThread.Post, same as
        // OnTextDelta above - not exercised here since nothing pumps the dispatcher in this plain
        // xunit host (see RunPromptAsync_StreamedTextDeltas_DoNotReparseOrStoreUntilTheRunFinishes'
        // own comment), so this only verifies the synchronous Clear() at the start of a run, not a
        // tool call actually appending to the log mid-run.
        var sut = CreateSut();
        sut.PromptText = "Do something";
        sut.ToolActivityLog.Add("Stale entry from a previous run");
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "done" };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Empty(sut.ToolActivityLog);
    }

    [Fact]
    public async Task RunPromptAsync_ResultWithUsageData_PopulatesUsageSummary()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "done"}""",
            TotalCostUsd = 0.0056,
            DurationMs = 1234,
            InputTokens = 10,
            OutputTokens = 5,
        };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Contains("0.0056", sut.UsageSummary);
        Assert.Contains("10", sut.UsageSummary);
        Assert.Contains("5", sut.UsageSummary);
    }

    [Fact]
    public async Task RunPromptAsync_ResultWithoutUsageData_LeavesUsageSummaryEmpty()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "done"}""" };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, sut.UsageSummary);
    }

    [Fact]
    public async Task RunPromptAsync_SuccessfulRun_ReturnsASessionId_MakesHasPreviousSessionTrue()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "done"}""", SessionId = "session-1" };

        Assert.False(sut.HasPreviousSession);

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.True(sut.HasPreviousSession);
    }

    [Fact]
    public async Task RunPromptAsync_SuccessfulRun_PassesTheSessionIdToThePromptResultStore()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "done"}""", SessionId = "session-1" };

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal("session-1", _promptResultStore.SessionId);
    }

    [Fact]
    public async Task RunPromptAsync_ContinuePreviousConversationChecked_PassesTheLastSessionIdToTheRunner()
    {
        var sut = CreateSut();
        sut.PromptText = "First turn";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "done"}""", SessionId = "session-1" };
        await sut.RunPromptCommand.ExecuteAsync(null);

        sut.ContinuePreviousConversation = true;
        sut.PromptText = "Follow-up turn";
        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal("session-1", _llmRunner.LastResumeSessionId);
    }

    [Fact]
    public async Task RunPromptAsync_ContinuePreviousConversationUnchecked_DoesNotPassAResumeSessionId()
    {
        var sut = CreateSut();
        sut.PromptText = "First turn";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "done"}""", SessionId = "session-1" };
        await sut.RunPromptCommand.ExecuteAsync(null);

        sut.PromptText = "Unrelated new turn";
        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Null(_llmRunner.LastResumeSessionId);
    }

    [Fact]
    public async Task OnWorkingDirectoryPathChanged_ClearsPreviousSessionAndUnchecksContinue()
    {
        var sut = CreateSut();
        sut.PromptText = "First turn";
        _llmRunner.ResultToReturn = new LlmRunResult { Success = true, Output = """{"responseType": "answer", "answer": "done"}""", SessionId = "session-1" };
        await sut.RunPromptCommand.ExecuteAsync(null);
        sut.ContinuePreviousConversation = true;

        sut.WorkingDirectoryPath = "/a/different/directory";

        Assert.False(sut.HasPreviousSession);
        Assert.False(sut.ContinuePreviousConversation);
    }

    [Fact]
    public async Task RunPromptAsync_WhileRunning_SetsElapsedTimeDisplay()
    {
        var sut = CreateSut();
        sut.PromptText = "Do something";

        Assert.Equal(string.Empty, sut.ElapsedTimeDisplay);

        await sut.RunPromptCommand.ExecuteAsync(null);

        Assert.Equal("0s", sut.ElapsedTimeDisplay);
    }

    [Fact]
    public async Task OnNavigatedTo_RestoresPromptAndWorkingDirectoryFromSettings()
    {
        await _localSettingsService.SaveSettingAsync("SavedPrompt", "Saved prompt text");
        await _localSettingsService.SaveSettingAsync("SavedWorkingDirectory", "/saved/dir");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);
        await Task.Yield();

        Assert.Equal("Saved prompt text", sut.PromptText);
        Assert.Equal("/saved/dir", sut.WorkingDirectoryPath);
    }

    private static RunSession CreateSession(string workingDirectoryPath, DateTimeOffset startedAt) => new(
        Guid.NewGuid().ToString("N"),
        startedAt,
        workingDirectoryPath,
        "composed prompt",
        "raw output",
        "tutorial text",
        Array.Empty<RunSessionFile>(),
        Array.Empty<ApplyHistoryEntry>(),
        "user prompt");

    [Fact]
    public void OnNavigatedTo_NoSessions_HasNoRecentWorkingDirectories()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.False(sut.HasRecentWorkingDirectories);
        Assert.Empty(sut.RecentWorkingDirectories);
    }

    [Fact]
    public void OnNavigatedTo_RecentWorkingDirectoriesAreDistinctAndCapped()
    {
        _runSessionStore.SessionsToList = Enumerable.Range(0, 8)
            .Select(i => CreateSession($"/working/dir-{i % 6}", DateTimeOffset.UtcNow.AddMinutes(-i)))
            .ToList();
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.True(sut.HasRecentWorkingDirectories);
        Assert.Equal(5, sut.RecentWorkingDirectories.Count);
        Assert.Equal(sut.RecentWorkingDirectories.Distinct().Count(), sut.RecentWorkingDirectories.Count);
    }

    [Fact]
    public async Task SelectedRecentWorkingDirectory_Set_SwitchesWorkingDirectoryAndPersistsIt()
    {
        _runSessionStore.SessionsToList = new[] { CreateSession("/recent/dir", DateTimeOffset.UtcNow) };
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);

        sut.SelectedRecentWorkingDirectory = sut.RecentWorkingDirectories[0];
        await Task.Yield();

        Assert.Equal("/recent/dir", sut.WorkingDirectoryPath);
        Assert.Null(sut.SelectedRecentWorkingDirectory);
        Assert.Equal("/recent/dir", await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory));
    }

    [Fact]
    public async Task OnNavigatedTo_WithForkLaunchRequest_LoadsPromptAndWorkingDirectoryButDoesNotRun()
    {
        var sut = CreateSut();

        sut.OnNavigatedTo(new PromptLaunchRequest("Forked prompt text", "/forked/dir", AutoRun: false));
        await Task.Yield();

        Assert.Equal("Forked prompt text", sut.PromptText);
        Assert.Equal("/forked/dir", sut.WorkingDirectoryPath);
        Assert.Equal(0, _runSessionStore.RecordRunCallCount);
    }

    [Fact]
    public async Task OnNavigatedTo_WithRerunLaunchRequest_LoadsAndImmediatelyRunsThePrompt()
    {
        var sut = CreateSut();
        _llmRunner.ResultToReturn = new LlmRunResult
        {
            Success = true,
            Output = """{"responseType": "answer", "answer": "Recursion is when a function calls itself."}""",
        };

        sut.OnNavigatedTo(new PromptLaunchRequest("Rerun prompt text", "/rerun/dir", AutoRun: true));
        await Task.Yield();

        Assert.Equal("Rerun prompt text", sut.PromptText);
        Assert.Equal("/rerun/dir", sut.WorkingDirectoryPath);
        Assert.Equal(1, _runSessionStore.RecordRunCallCount);
        Assert.Equal("Rerun prompt text", _runSessionStore.LastRecordedUserPromptText);
    }

    [Fact]
    public async Task SuggestRelevantFilesAsync_MatchesFromIndexService_PopulatesSuggestedRelevantFiles()
    {
        var sut = CreateSut();
        sut.WorkingDirectoryPath = "/my/project";
        sut.PromptText = "Fix the bug in Foo";
        _codebaseIndexService.MatchesToReturn = [new RelevantFileMatch("Foo.cs", 1.5), new RelevantFileMatch("Bar.cs", 0.5)];

        await sut.SuggestRelevantFilesCommand.ExecuteAsync(null);

        Assert.Equal(["Foo.cs", "Bar.cs"], sut.SuggestedRelevantFiles);
        Assert.Equal("/my/project", _codebaseIndexService.LastWorkingDirectoryPath);
        Assert.Equal("Fix the bug in Foo", _codebaseIndexService.LastQueryText);
    }

    [Fact]
    public async Task SuggestRelevantFilesAsync_MatchAlreadyAttached_ExcludesItFromSuggestions()
    {
        var sut = CreateSut();
        sut.WorkingDirectoryPath = "/my/project";
        sut.PromptText = "Fix the bug in Foo";
        sut.AttachedPaths.Add(Path.Combine("/my/project", "Foo.cs"));
        _codebaseIndexService.MatchesToReturn = [new RelevantFileMatch("Foo.cs", 1.5), new RelevantFileMatch("Bar.cs", 0.5)];

        await sut.SuggestRelevantFilesCommand.ExecuteAsync(null);

        Assert.Equal(["Bar.cs"], sut.SuggestedRelevantFiles);
    }

    [Fact]
    public void SuggestRelevantFilesCommand_BlankPromptOrWorkingDirectory_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.SuggestRelevantFilesCommand.CanExecute(null));

        sut.WorkingDirectoryPath = "/my/project";
        Assert.False(sut.SuggestRelevantFilesCommand.CanExecute(null));

        sut.PromptText = "Fix the bug";
        Assert.True(sut.SuggestRelevantFilesCommand.CanExecute(null));
    }

    [Fact]
    public void AttachSuggestedFile_ResolvesRelativePathAndAttachesIt_RemovingItFromSuggestions()
    {
        var sut = CreateSut();
        sut.WorkingDirectoryPath = "/my/project";
        sut.SuggestedRelevantFiles.Add("Foo.cs");

        sut.AttachSuggestedFile("Foo.cs");

        Assert.Contains(Path.GetFullPath(Path.Combine("/my/project", "Foo.cs")), sut.AttachedPaths);
        Assert.DoesNotContain("Foo.cs", sut.SuggestedRelevantFiles);
    }

    [Fact]
    public void DismissSuggestedRelevantFilesCommand_ClearsSuggestionsWithoutAttachingAnything()
    {
        var sut = CreateSut();
        sut.WorkingDirectoryPath = "/my/project";
        sut.SuggestedRelevantFiles.Add("Foo.cs");

        sut.DismissSuggestedRelevantFilesCommand.Execute(null);

        Assert.Empty(sut.SuggestedRelevantFiles);
        Assert.Empty(sut.AttachedPaths);
    }
}
