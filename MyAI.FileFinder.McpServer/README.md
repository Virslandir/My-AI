# FileFinder local MCP server

This server is a local, read-only Codex integration. It searches file names and returns file paths; it never reads file contents or changes files.

## Allowed locations

By default, searches are limited to the current Windows user's profile directory. To allow additional roots, set `FILE_FINDER_ALLOWED_ROOTS` before starting Codex. Separate Windows paths with semicolons:

```powershell
$env:FILE_FINDER_ALLOWED_ROOTS = "C:\Users\YourName;D:\Archive"
```

Restart Codex after changing the environment variable. A requested location must be inside an allowed root.

## Limits

- Searches time out after 60 seconds.
- At most 1,000 matching files are collected for a query.
- Results are paged, with a maximum page size of 100.
