# RepoDetox

Placeholder repository for a .NET 8 command-line tool that inspects a git repository, lists files that only exist in history, and can vacuum those paths out of history.

## Current Commands

- `list`: shows files that appear in repository history but no longer exist on the current branch.
- `flatten`: rewrites the repository to a single root commit that matches the current HEAD state, removing all other refs and history.
- `vacuum`: rewrites history to remove the same file set after confirmation, can anonymize commit/tag usernames and emails, then expires reflogs and runs garbage collection.
- `preview`: starts a local browser view for the current analysis to support editor debugging. This is opt-in and requires `Preview:Enabled` to be set to `true` in `appsettings.json`.

## Prerequisites

- .NET 8 SDK or newer to build/publish
- Git on `PATH`
- `git filter-repo` on `PATH` for the `vacuum` command

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

To anonymize commit metadata during a rewrite, use `vacuum` with `--anonymize`, `--anonymize-users`, or `--anonymize-emails`. This rewrites commit hashes, so any clones, forks, pull requests, signed objects, or tooling that references existing hashes can be affected.

To delete all history and keep only the current repository state, use `flatten`. This creates a single new root commit and removes all other refs, so prior hashes and tags stop being valid.
