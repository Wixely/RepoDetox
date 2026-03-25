# RepoDetox

Placeholder repository for a .NET 8 command-line tool that inspects a git repository, lists files that only exist in history, and can vacuum those paths out of history.

## Current Commands

- `list`: shows files that appear in repository history but no longer exist on the current branch.
- `vacuum`: rewrites history to remove the same file set after confirmation, can anonymize commit/tag usernames and emails, then expires reflogs and runs garbage collection.
- `preview`: starts a local browser view for the current analysis to support editor debugging. This is opt-in and requires `Preview:Enabled` to be set to `true` in `appsettings.json`.

## Prerequisites

- .NET 8 SDK or newer
- Git on `PATH`
- `git filter-repo` on `PATH` for the `vacuum` command

## Example

```powershell
dotnet run -- list --repo C:\path\to\repo
```

Run `dotnet run` with no arguments to see the CLI help.

To anonymize commit metadata during a rewrite, use `vacuum` with `--anonymize`, `--anonymize-users`, or `--anonymize-emails`. This rewrites commit hashes, so any clones, forks, pull requests, signed objects, or tooling that references existing hashes can be affected.
