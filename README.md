# RepoDetox

Placeholder repository for a .NET 8 command-line tool that inspects a git repository, lists files that were deleted from history and are no longer present on any live ref, and can vacuum those paths out of history.

## Current Commands

- `list`: shows files that were deleted in history and are no longer present on any live ref.
- `flatten`: rewrites the repository to a single root commit that matches the current HEAD state, removing all other refs and history.
- `vacuum`: rewrites history to remove files that were deleted and are no longer present on any live ref, then expires reflogs and runs garbage collection.
- `anonymise`: rewrites history to anonymise commit/tag usernames and emails without removing files.
- `preview`: starts a local browser view for the current analysis to support editor debugging. This is opt-in and requires `Preview:Enabled` to be set to `true` in `appsettings.json`.

## Prerequisites

- .NET 8 SDK or newer to build/publish
- Git on `PATH`
- `git-filter-repo.py` on `PATH` for the `vacuum` and `anonymise` commands

If `git-filter-repo.py` is missing, a straightforward install is:

```powershell
python -m pip install git-filter-repo
```

Upstream installation instructions: https://github.com/newren/git-filter-repo/blob/main/INSTALL.md

## Portable Build

Publish a self-contained portable Windows build with:

```powershell
dotnet publish -c Release
```

The output will be in `bin\Release\net8.0\win-x64\publish\` and can be copied to another Windows x64 machine without installing .NET. `appsettings.json` is included in that publish output.

## Example

```powershell
dotnet run -- list --repo C:\path\to\repo
```

```powershell
dotnet run -- list C:\path\to\repo
```

Run `dotnet run` with no arguments to see the CLI help.

You can pass the repository either as a positional argument like `list C:\path\to\repo` or with `--repo C:\path\to\repo`.

To anonymise commit metadata without removing files, use `anonymise`. By default it rewrites both usernames and emails; use `--users` or `--emails` to target one side only. This rewrites commit hashes, so any clones, forks, pull requests, signed objects, or tooling that references existing hashes can be affected.

To delete all history and keep only the current repository state, use `flatten`. This creates a single new root commit and removes all other refs, so prior hashes and tags stop being valid.
