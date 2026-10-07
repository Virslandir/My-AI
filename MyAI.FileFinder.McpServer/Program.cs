using System.Text;
using System.Text.Json;
using MyAI.Actions;

var server = new FileFinderMcpServer();
await server.RunAsync();

internal sealed class FileFinderMcpServer
{
    private const int MaximumSearchResults = 1_000;
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private static readonly string[] SupportedProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26"];
    private readonly FileSearchService _searchService = new();
    private readonly IReadOnlyList<string> _allowedRoots = GetAllowedRoots();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task RunAsync()
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var request = JsonDocument.Parse(line);
                await HandleRequestAsync(request.RootElement);
            }
            catch (JsonException)
            {
                await WriteErrorAsync(null, -32700, "Invalid JSON-RPC request.");
            }
            catch (Exception exception)
            {
                await WriteErrorAsync(null, -32603, exception.Message);
            }
        }
    }

    private async Task HandleRequestAsync(JsonElement request)
    {
        if (!request.TryGetProperty("method", out var methodElement))
        {
            await WriteErrorAsync(GetId(request), -32600, "The request must include a method.");
            return;
        }

        var id = GetId(request);
        var method = methodElement.GetString();
        if (id is null) return; // Notifications do not require a response.

        switch (method)
        {
            case "initialize":
                await WriteResultAsync(id.Value, CreateInitializeResult(request));
                break;
            case "ping":
                await WriteResultAsync(id.Value, new { });
                break;
            case "tools/list":
                await WriteResultAsync(id.Value, new { tools = new[] { CreateSearchToolDefinition() } });
                break;
            case "tools/call":
                await HandleToolCallAsync(id.Value, request);
                break;
            default:
                await WriteErrorAsync(id, -32601, $"Unknown method: {method}");
                break;
        }
    }

    private object CreateInitializeResult(JsonElement request)
    {
        var requestedVersion = request.TryGetProperty("params", out var parameters) &&
            parameters.TryGetProperty("protocolVersion", out var version)
            ? version.GetString()
            : null;
        var protocolVersion = SupportedProtocolVersions.Contains(requestedVersion, StringComparer.Ordinal)
            ? requestedVersion
            : SupportedProtocolVersions[0];

        return new
        {
            protocolVersion,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "FileFinder", version = "1.0.1" },
            instructions = "Use search_files only to locate file paths in configured local folders. It does not read file contents or modify files."
        };
    }

    private static object CreateSearchToolDefinition() => new
    {
        name = "search_files",
        title = "Search local files",
        description = "Search configured local folders by one or more case-insensitive filename terms and extensions. Returns paths and file metadata only; it never reads, opens, changes, or deletes files.",
        inputSchema = new
        {
            type = "object",
            properties = new
            {
                locations = new { type = "array", items = new { type = "string" }, description = "Optional folders to search. Each must be within a configured allowed root." },
                nameTerms = new { type = "array", items = new { type = "string" }, description = "Filename text alternatives. A file matches when it contains any supplied term." },
                extensions = new { type = "array", items = new { type = "string" }, description = "Extension alternatives, with or without a leading period." },
                page = new { type = "integer", minimum = 1, defaultValue = 1 },
                pageSize = new { type = "integer", minimum = 1, maximum = MaximumPageSize, defaultValue = DefaultPageSize }
            }
        },
        outputSchema = new
        {
            type = "object",
            properties = new
            {
                locationsSearched = new { type = "array", items = new { type = "string" } },
                totalMatches = new { type = "integer" },
                page = new { type = "integer" },
                pageSize = new { type = "integer" },
                hasMore = new { type = "boolean" },
                searchWasLimited = new { type = "boolean" },
                skippedFolderCount = new { type = "integer" },
                results = new { type = "array" }
            }
        },
        annotations = new { readOnlyHint = true, destructiveHint = false, openWorldHint = false, idempotentHint = true }
    };

    private async Task HandleToolCallAsync(JsonElement id, JsonElement request)
    {
        if (!request.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("name", out var name) ||
            !string.Equals(name.GetString(), "search_files", StringComparison.Ordinal))
        {
            await WriteErrorAsync(id, -32602, "Only the search_files tool is available.");
            return;
        }

        try
        {
            var arguments = parameters.TryGetProperty("arguments", out var argumentElement) ? argumentElement : default;
            var locations = GetStringArray(arguments, "locations");
            var nameTerms = GetStringArray(arguments, "nameTerms");
            var extensions = GetStringArray(arguments, "extensions");
            var page = GetPositiveInteger(arguments, "page", 1, int.MaxValue);
            var pageSize = GetPositiveInteger(arguments, "pageSize", DefaultPageSize, MaximumPageSize);

            if (nameTerms.Count == 0 && extensions.Count == 0)
            {
                throw new ArgumentException("Provide at least one nameTerms or extensions value.");
            }

            var searchLocations = locations.Count == 0 ? _allowedRoots : ValidateLocations(locations);
            var query = new FileSearchQuery(searchLocations, nameTerms, extensions);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await _searchService.SearchAsync(query, MaximumSearchResults, timeout.Token);
            var allResults = response.Files;
            var start = checked((page - 1) * pageSize);
            var pageResults = start >= allResults.Count ? [] : allResults.Skip(start).Take(pageSize)
                .Select(file => new SearchResultItem(file.Name, Path.GetExtension(file.FullPath), Path.GetDirectoryName(file.FullPath) ?? string.Empty, file.FullPath, file.LastWriteTime.ToUniversalTime(), file.SizeInBytes))
                .ToArray();
            var result = new SearchFilesResult(searchLocations, allResults.Count, page, pageSize,
                start + pageResults.Length < allResults.Count, response.ResultLimitReached, response.SkippedFolderCount, pageResults);

            await WriteResultAsync(id, new
            {
                content = new[] { new { type = "text", text = CreateResultTable(result) } },
                structuredContent = result
            });
        }
        catch (OperationCanceledException)
        {
            await WriteResultAsync(id, new { content = new[] { new { type = "text", text = "The file search timed out after 60 seconds." } }, isError = true });
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            await WriteResultAsync(id, new { content = new[] { new { type = "text", text = exception.Message } }, isError = true });
        }
    }

    private IReadOnlyList<string> ValidateLocations(IReadOnlyList<string> locations)
    {
        var normalizedLocations = locations.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var location in normalizedLocations)
        {
            if (!_allowedRoots.Any(allowedRoot => IsWithin(location, allowedRoot)))
            {
                throw new ArgumentException($"The location '{location}' is not within an allowed root. Allowed roots: {string.Join(", ", _allowedRoots)}");
            }
        }

        return normalizedLocations;
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetAllowedRoots()
    {
        var configuredRoots = Environment.GetEnvironmentVariable("FILE_FINDER_ALLOWED_ROOTS");
        var roots = string.IsNullOrWhiteSpace(configuredRoots)
            ? new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
            : configuredRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement arguments, string propertyName) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
            : [];

    private static int GetPositiveInteger(JsonElement arguments, string propertyName, int defaultValue, int maximum)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if (!value.TryGetInt32(out var number) || number < 1 || number > maximum)
        {
            throw new ArgumentException($"{propertyName} must be between 1 and {maximum}.");
        }

        return number;
    }

    private static string CreateResultTable(SearchFilesResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Found {result.TotalMatches:N0} file(s). Showing page {result.Page} with {result.Results.Count:N0} result(s).");
        if (result.SearchWasLimited) builder.AppendLine($"Search results are limited to the first {MaximumSearchResults:N0} matches.");
        if (result.SkippedFolderCount > 0) builder.AppendLine($"Skipped {result.SkippedFolderCount:N0} inaccessible folder(s).");
        builder.AppendLine();
        builder.AppendLine("| File name | Extension | Folder | Modified | Size |");
        builder.AppendLine("| --- | --- | --- | --- | ---: |");
        foreach (var file in result.Results)
        {
            builder.AppendLine($"| `{EscapeCell(file.Name)}` | `{EscapeCell(file.Extension)}` | `{EscapeCell(file.Directory)}` | {file.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm} | {FormatSize(file.SizeBytes)} |");
        }

        if (result.Results.Count == 0) builder.AppendLine("| No results on this page. |  |  |  |  |");
        return builder.ToString();
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|").Replace("`", "'");
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / 1024d / 1024d:F1} MB",
        _ => $"{bytes / 1024d / 1024d / 1024d:F1} GB"
    };

    private async Task WriteResultAsync(JsonElement id, object result) => await WriteAsync(new { jsonrpc = "2.0", id, result });
    private async Task WriteErrorAsync(JsonElement? id, int code, string message) => await WriteAsync(new { jsonrpc = "2.0", id, error = new { code, message } });
    private async Task WriteAsync(object response) => await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, _jsonOptions));
    private static JsonElement? GetId(JsonElement request) => request.TryGetProperty("id", out var id) ? id.Clone() : null;

    private sealed record SearchResultItem(string Name, string Extension, string Directory, string FullPath, DateTime LastWriteTimeUtc, long SizeBytes);
    private sealed record SearchFilesResult(IReadOnlyList<string> LocationsSearched, int TotalMatches, int Page, int PageSize, bool HasMore, bool SearchWasLimited, int SkippedFolderCount, IReadOnlyList<SearchResultItem> Results);
}
