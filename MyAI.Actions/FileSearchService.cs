namespace MyAI.Actions;

/// <summary>Searches files without failing an entire search when a folder is inaccessible.</summary>
public sealed class FileSearchService
{
    public Task<FileSearchResponse> SearchAsync(FileSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.Run(() => Search(query, cancellationToken), cancellationToken);
    }

    private static FileSearchResponse Search(FileSearchQuery query, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(query.SearchRoot))
        {
            throw new DirectoryNotFoundException($"The search location does not exist: {query.SearchRoot}");
        }

        var files = new List<FileSearchResult>();
        var skippedFolders = 0;
        var directories = new Stack<string>();
        directories.Push(query.SearchRoot);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var file = new FileInfo(filePath);
                        if (query.Matches(file)) files.Add(new(file.Name, file.FullName, file.Length, file.LastWriteTime));
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }

                foreach (var subdirectory in Directory.EnumerateDirectories(directory)) directories.Push(subdirectory);
            }
            catch (UnauthorizedAccessException) { skippedFolders++; }
            catch (IOException) { skippedFolders++; }
        }

        return new(files.OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray(), skippedFolders);
    }
}

public sealed record FileSearchResponse(IReadOnlyList<FileSearchResult> Files, int SkippedFolderCount);
