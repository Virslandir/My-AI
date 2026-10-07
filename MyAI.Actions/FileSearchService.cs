namespace MyAI.Actions;

/// <summary>Searches files without failing an entire search when a folder is inaccessible.</summary>
public sealed class FileSearchService
{
    public Task<FileSearchResponse> SearchAsync(FileSearchQuery query, int? maximumResults = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (maximumResults is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults), "The maximum result count must be positive.");
        }

        return Task.Run(() => Search(query, maximumResults, cancellationToken), cancellationToken);
    }

    private static FileSearchResponse Search(FileSearchQuery query, int? maximumResults, CancellationToken cancellationToken)
    {
        var missingLocation = query.SearchRoots.FirstOrDefault(location => !Directory.Exists(location));
        if (missingLocation is not null)
        {
            throw new DirectoryNotFoundException($"The search location does not exist: {missingLocation}");
        }

        var reparsePointRoot = query.SearchRoots.FirstOrDefault(location => (File.GetAttributes(location) & FileAttributes.ReparsePoint) != 0);
        if (reparsePointRoot is not null)
        {
            throw new ArgumentException($"The search location '{reparsePointRoot}' is a reparse point (such as a junction or symbolic link), which is not allowed.", nameof(query));
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
                        if (query.Matches(file))
                        {
                            files.Add(new(file.Name, file.FullName, file.Length, file.LastWriteTime));
                            if (maximumResults is not null && files.Count >= maximumResults)
                            {
                                return CreateResponse(files, skippedFolders, true);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }

                foreach (var subdirectory in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                    {
                        directories.Push(subdirectory);
                    }
                }
            }
            catch (UnauthorizedAccessException) { skippedFolders++; }
            catch (IOException) { skippedFolders++; }
        }

        return CreateResponse(files, skippedFolders, false);
    }

    private static FileSearchResponse CreateResponse(IEnumerable<FileSearchResult> files, int skippedFolders, bool limitReached) =>
        new(files.DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray(), skippedFolders, limitReached);
}

public sealed record FileSearchResponse(IReadOnlyList<FileSearchResult> Files, int SkippedFolderCount, bool ResultLimitReached = false);
