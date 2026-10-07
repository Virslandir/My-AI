namespace MyAI.Actions;

/// <summary>The criteria used to locate files. Name and extension alternatives are combined with AND.</summary>
public sealed class FileSearchQuery
{
    public FileSearchQuery(IEnumerable<string>? searchRoots, IEnumerable<string>? nameTerms, IEnumerable<string>? extensions)
    {
        SearchRoots = (searchRoots ?? [])
            .Select(location => location.Trim())
            .Where(location => location.Length > 0)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (SearchRoots.Count == 0)
        {
            throw new ArgumentException("At least one search location is required.", nameof(searchRoots));
        }

        NameTerms = Normalize(nameTerms, false);
        Extensions = Normalize(extensions, true);
    }

    public IReadOnlyList<string> SearchRoots { get; }
    public IReadOnlyList<string> NameTerms { get; }
    public IReadOnlyList<string> Extensions { get; }

    public bool Matches(FileInfo file) =>
        (NameTerms.Count == 0 || NameTerms.Any(term => file.Name.Contains(term, StringComparison.OrdinalIgnoreCase))) &&
        (Extensions.Count == 0 || Extensions.Any(extension => string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase)));

    public static IReadOnlyList<string> ParseAlternatives(string? text) =>
        (text ?? string.Empty).Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? values, bool extension)
    {
        return (values ?? []).Select(value => value.Trim()).Where(value => value.Length > 0)
            .Select(value => extension ? "." + value.TrimStart('.') : value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
