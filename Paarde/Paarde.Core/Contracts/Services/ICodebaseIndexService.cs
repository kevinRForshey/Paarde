using Paarde.Core.Models;

namespace Paarde.Core.Contracts.Services;

// T14.2.1: "suggest relevant files" for a prompt, without a cloud embeddings provider. Scores
// whole files (not embedding vectors) via TF-IDF against the query text - a deliberately cheap,
// keyless, infrastructure-free stand-in for real semantic search; see
// TfIdfCodebaseIndexService for the tradeoffs this accepts.
public interface ICodebaseIndexService
{
    Task<IReadOnlyList<RelevantFileMatch>> FindRelevantFilesAsync(string workingDirectoryPath, string queryText, int maxResults = 10, CancellationToken cancellationToken = default);
}
