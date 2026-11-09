namespace Paarde.Presentation.Tests;

// specs/presentation-extraction R1.2/R6.2: Paarde.Presentation is shared with a future WinUI 3
// front end, so it must never pick up a dependency on any Avalonia-family assembly.
public class PresentationAssemblyReferenceTests
{
    private static readonly string[] _forbiddenPrefixes = ["Avalonia", "FluentAvalonia", "AvaloniaEdit", "TextMateSharp"];

    [Fact]
    public void PresentationAssembly_ReferencesNoAvaloniaFamilyAssembly()
    {
        var presentationAssembly = typeof(PresentationAssemblyMarker).Assembly;

        var offending = presentationAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => _forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offending);
    }
}
