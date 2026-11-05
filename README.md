# My AI — File Finder

My AI is a small, local-first Windows file-search utility with a Codex integration. It helps you locate files by filename and extension without uploading or exposing file contents to an AI service. The repository currently includes a WPF desktop app, a shared search library, and a read-only Model Context Protocol (MCP) server packaged as a Codex plugin.

## Features

- Search one or more local folders and their subfolders.
- Match case-insensitive filename terms and file extensions. Multiple values are alternatives; when both a name and extension are provided, a match must satisfy both criteria.
- Browse for folders in the desktop app, cancel an active search, and open a result’s location in Windows Explorer.
- Use Codex to search for local files through the `filefinder.search_files` MCP tool.
- Keep searches local and read-only: the MCP server returns file paths and metadata (such as name, extension, modified time, and size); it does not read file contents or modify files.

## Projects

| Project | Purpose |
| --- | --- |
| `MyAI.WpfUI` | Windows WPF desktop file-search application. |
| `MyAI.Actions` | Shared file-search query and service used by the app and MCP server. |
| `MyAI.FileFinder.McpServer` | Local stdio MCP server that exposes the `search_files` tool. |
| `plugins/file-finder` | Codex plugin manifest, MCP configuration, and file-search skill. |

## Requirements

- Windows (the desktop UI uses WPF and Windows Explorer).
- .NET 10 SDK.
- Codex, if you want to use the MCP server or plugin.

## Install and run the desktop app

Clone the repository and build/run the WPF project:

```powershell
git clone https://github.com/Virslandir/My-AI.git
cd My-AI
dotnet run --project .\MyAI.WpfUI\MyAI.WpfUI.csproj
```

In File Finder, choose a search folder, enter one or more filename terms and/or extensions, and select **Search**. Separate alternatives with commas, semicolons, or new lines. For example, filename terms `report, invoice` and extensions `pdf, docx` search for files matching either name term and either extension. Results can be double-clicked or opened from the context menu to show the file in Explorer.

To build without launching the app:

```powershell
dotnet build .\MyAI.WpfUI\MyAI.WpfUI.csproj
```

## Codex MCP plugin

The `file-finder` plugin gives Codex a local tool to locate files on your Windows computer. Its skill directs file-location requests to the `filefinder.search_files` tool and instructs Codex to preserve the returned full paths. It is not a general file-reading tool: results contain paths and file metadata only.

### Configure and start the MCP server

The repository includes a project-local Codex configuration at `.codex/config.toml`. When Codex is run with this repository as its working directory and the project configuration is enabled/trusted, the MCP server is started with `dotnet run --project MyAI.FileFinder.McpServer` and exposes `search_files`.

By default, the server only searches within the current Windows user’s profile directory. To permit additional roots, set `FILE_FINDER_ALLOWED_ROOTS` before starting Codex. On Windows, separate full paths with semicolons:

```powershell
$env:FILE_FINDER_ALLOWED_ROOTS = "C:\Users\YourName;D:\Archive"
```

Restart Codex after changing this variable. Any `locations` passed to the tool must be inside one of the configured allowed roots; requests outside those roots are rejected. Keep this list limited to folders you want Codex to be able to search.

The MCP server applies additional limits: searches time out after 60 seconds, collect at most 1,000 matches, and return paged results (default 50 per page; maximum 100). Inaccessible folders are skipped and reported.

### Install the bundled Codex plugin

The plugin source is in `plugins/file-finder`:

- `.codex-plugin/plugin.json` — plugin metadata and skill location.
- `.mcp.json` — MCP server launch configuration for the packaged executable.
- `skills/file-search/SKILL.md` — guidance for when and how Codex should use the tool.

The repository also includes a local plugin catalog at `.agents/plugins/marketplace.json`, which registers the plugin as `file-finder` in the `myai-local` catalog. Add that local catalog/plugin through the Codex plugin manager for your installation, then enable the plugin and restart Codex if prompted. The bundled plugin MCP configuration expects the server executable at `plugins/file-finder/bin/FileFinder.McpServer.exe`; publish/copy the MCP server there before using that launch configuration. Alternatively, use the repository-local `.codex/config.toml` to run the server directly from source with the .NET SDK.

## Security and privacy notes

- The MCP server is read-only and does not open, read, edit, move, or delete files.
- Search roots are restricted by `FILE_FINDER_ALLOWED_ROOTS` (or, by default, the current user profile). A requested location must be within an allowed root.
- Filename searches traverse subfolders but do not follow directory reparse points.
- File names, paths, and metadata are returned to Codex as tool results. Configure allowed roots with that in mind.

## License

No license is currently specified in this repository. Check with the repository owner before redistributing or reusing the code.
