namespace MyAI.Actions;

public sealed record FileSearchResult(string Name, string FullPath, long SizeInBytes, DateTime LastWriteTime)
{
    public string DirectoryPath => Path.GetDirectoryName(FullPath) ?? string.Empty;
}
