# RepoDetox

A .NET 8 toolkit for cleaning and anonymising git repositories by rewriting history. It
ships as both a **command-line tool** and a cross-platform **desktop GUI**, sharing one
core engine. All history rewriting is performed in pure C# using git's built-in
`git fast-export` / `git fast-import` — no Python or `git-filter-repo` dependency.

## Projects

| Project | Output | Description |
| --- | --- | --- |
| `RepoDetox.Core` | library | Engine: analysis + history-rewrite services, console-free |
| `RepoDetox` | CLI exe | Command-line front-end (`list`/`vacuum`/`anonymise`/`flatten`/`preview`) |
| `RepoDetox.Gui` | desktop exe | Avalonia GUI with a tab per feature |

## Commands

- `list`: shows files that were deleted in history and are no longer present on any live ref.
- `flatten`: rewrites the repository to a single root commit that matches the current HEAD state, removing all other refs and history.
- `vacuum`: rewrites history to remove files that were deleted and are no longer present on any live ref, then expires reflogs and runs garbage collection.
- `anonymise`: rewrites history to anonymise commit/tag usernames and emails without removing files.
- `expunge`: rewrites history to replace literal secret strings everywhere they appear (file contents and, by default, commit/tag messages) — for scrubbing accidentally-committed secrets.
- `mcp`: runs RepoDetox as an stdio [Model Context Protocol](https://modelcontextprotocol.io) server so an AI agent can call the operations as tools. See [MCP server](#mcp-server).
- `preview`: starts a local browser view for the current analysis to support editor debugging. This is opt-in and requires `Preview:Enabled` to be set to `true` in `appsettings.json`.

## Prerequisites

- .NET 8 SDK or newer to build/publish
- Git on `PATH`
- For the GUI on **Linux**: a desktop with X11 and fontconfig present
  (`libx11`, `libfontconfig1`); these system libraries are not bundled.

## Desktop GUI

Pick a repository once at the top of the window, then switch between the **Analyze**,
**Vacuum**, **Anonymise**, and **Flatten** tabs to run any operation on it. Destructive
operations ask for confirmation, progress streams to a shared output log, and a Cancel
button aborts a running operation.

The **Repo Browser** button opens a modal that scans your drives for git repositories in
the background (results stream in live, sortable by path or last-changed date, cached to
disk for instant re-open, with a recent-selections list); choosing one selects it and
analyses it immediately. Run it during development with:

```powershell
dotnet run --project RepoDetox.Gui
```

## Portable Build

Publish a self-contained, single-file standalone build (no .NET install required on
the target machine). Choose the CLI or the GUI project:

```powershell
# CLI
dotnet publish RepoDetox\RepoDetox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# GUI
dotnet publish RepoDetox.Gui\RepoDetox.Gui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Use `-r linux-x64` to target Linux. Output goes to `<project>\bin\Release\net8.0\<rid>\publish\`.

Tagged releases (`v*`) are built automatically by GitHub Actions
([.github/workflows/build-release-packages.yml](.github/workflows/build-release-packages.yml)),
which publishes standalone CLI and GUI zips for `win-x64` and `linux-x64` and attaches them
to the GitHub release.

## CLI usage

```powershell
dotnet run --project RepoDetox -- list --repo C:\path\to\repo
```

```powershell
dotnet run --project RepoDetox -- list C:\path\to\repo
```

Run `dotnet run --project RepoDetox` with no arguments to see the CLI help.

You can pass the repository either as a positional argument like `list C:\path\to\repo` or with `--repo C:\path\to\repo`.

To anonymise commit metadata without removing files, use `anonymise`. By default it rewrites both usernames and emails; use `--users` or `--emails` to target one side only. Each original identity is replaced with a deterministic per-identity hash (e.g. `anonymous-user-xxxxxxxxxxxx`).

To instead set an exact value for everyone, pass `--set-name` and/or `--set-email`:

```powershell
# Collapse every contributor to a single identity
dotnet run --project RepoDetox -- anonymise C:\path\to\repo --set-name "Anon" --set-email "anon@example.invalid"

# Set a fixed name but leave emails untouched
dotnet run --project RepoDetox -- anonymise C:\path\to\repo --set-name "Anon"

# Fixed name, hashed email
dotnet run --project RepoDetox -- anonymise C:\path\to\repo --set-name "Anon" --emails
```

`--set-name`/`--set-email` apply to authors, committers, and taggers. Passing either one targets only that side unless you also pass `--users`/`--emails`. Anonymising rewrites commit hashes, so any clones, forks, pull requests, signed objects, or tooling that references existing hashes can be affected.

To delete all history and keep only the current repository state, use `flatten`. This creates a single new root commit and removes all other refs, so prior hashes and tags stop being valid.

To scrub an accidentally-committed secret out of history, use `expunge`. It replaces each literal
secret string with a token (default `***REMOVED***`) in every blob and, by default, in commit/tag
messages, then updates your working tree to match:

```powershell
# Prefer a file (one secret per line) so the value isn't left in your shell history:
dotnet run --project RepoDetox -- expunge C:\path\to\repo --secrets-file C:\path\to\secrets.txt

# Or pass it inline (visible in shell history / process list):
dotnet run --project RepoDetox -- expunge C:\path\to\repo --secret "AKIA...EXAMPLE"
```

Use `--replacement <text>` to change the token and `--contents-only` to leave messages untouched.
**Important:** expunge only removes the secret from *this* repository's history — it was already
committed and may exist in clones, forks, backups, or CI logs, so rotate/revoke the secret too.

## MCP server

`repodetox mcp` starts an stdio Model Context Protocol server that exposes the same operations as
tools, so an agent can drive RepoDetox: `analyze_repository` (read-only), `vacuum_repository`,
`anonymise_repository`, `flatten_repository`, and `expunge_secrets`. Destructive tools require an
explicit `confirm=true` argument before they rewrite history. stdout carries only the JSON-RPC
protocol; all logging goes to stderr.

Register it with an MCP client (e.g. an `mcp.json`):

```json
{
  "mcpServers": {
    "repodetox": {
      "command": "C:\\path\\to\\RepoDetox.exe",
      "args": ["mcp"]
    }
  }
}
```

The existing CLI verbs are unchanged; `mcp` is an additional subcommand.

## How history rewriting works

`vacuum` and `anonymise` stream the repository through git's own plumbing:

```
git fast-export --all  →  in-process C# transform  →  git fast-import --force
```

The transform parses the [fast-import stream format](https://git-scm.com/docs/git-fast-import)
as raw bytes and either rewrites identity lines (anonymise) or drops the file-change
operations for removed paths (vacuum). Both commands then run `git reflog expire` and
`git gc --prune=now --aggressive` to reclaim space. This is an original implementation
derived from the public git documentation; it borrows no third-party code. The technique
(filtering a fast-export stream) is the same general approach used by `git filter-branch`
and git-filter-repo.

## License

RepoDetox is released under the [MIT License](LICENSE). Third-party dependency licenses
are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
