using Newtonsoft.Json;

using Paarde.Core.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeFileService : IFileService
{
    private readonly Dictionary<string, string> _files = new();

    public T? Read<T>(string folderPath, string fileName)
    {
        var key = Path.Combine(folderPath, fileName);

        return _files.TryGetValue(key, out var json) ? JsonConvert.DeserializeObject<T>(json) : default;
    }

    public void Save<T>(string folderPath, string fileName, T content) =>
        _files[Path.Combine(folderPath, fileName)] = JsonConvert.SerializeObject(content);

    public void Delete(string folderPath, string fileName) => _files.Remove(Path.Combine(folderPath, fileName));
}
