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
        var missingLocation = query.SearchRoots.FirstOrDefault(location => !Directory.Exists(location));
        if (missingLocation is not null)
        {
            throw new DirectoryNotFoundException($"The search location does not exist: {missingLocation}");
        }

        var files = new List<FileSearchResult>();
        var skippedFolders = 0;
        var directories = new Stack<string>();
        foreach (var searchRoot in query.SearchRoots) directories.Push(searchRoot);

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

        return new(files.DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray(), skippedFolders);
    }
}

public sealed record FileSearchResponse(IReadOnlyList<FileSearchResult> Files, int SkippedFolderCount);
