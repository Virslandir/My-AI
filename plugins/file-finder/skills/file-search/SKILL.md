---
name: file-search
description: Search configured local folders for files by filename terms and extensions when the user asks to locate files on this Windows computer.
---

Use the `filefinder.search_files` MCP tool for requests to locate files on this computer.

- Supply all requested filename alternatives in `nameTerms`.
- Supply all requested extension alternatives in `extensions`.
- Supply requested folders in `locations`; omit locations only when the configured default roots are intended.
- Never claim a path exists without using the tool.
- The tool is search-only: do not use it to read, open, edit, move, or delete files.
- Present its returned Markdown table and preserve full paths when the user needs them.
