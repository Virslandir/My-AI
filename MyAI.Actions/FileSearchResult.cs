namespace MyAI.Actions;

public sealed record FileSearchResult(string Name, string FullPath, long SizeInBytes, DateTime LastWriteTime);
