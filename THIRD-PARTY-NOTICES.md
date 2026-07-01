# Third-Party Notices

RepoDetox is licensed under the MIT License (see `LICENSE`). It depends on the
third-party components listed below. Each remains under its own license; this
file is provided for attribution.

Four projects live in this repository, and each has its own dependency set:

- `RepoDetox.Core` — shared domain library (analyzers, history-rewrite services)
- `RepoDetox` — CLI + stdio MCP server executable
- `RepoDetox.Gui` — desktop GUI (Avalonia)
- `RepoDetoxMCPSharp` — streamable-HTTP MCP server executable (MCPSharp product-line variant)

## NuGet packages — RepoDetox.Core (library)

| Package | License |
| --- | --- |
| Microsoft.Extensions.Configuration | MIT |
| Microsoft.Extensions.Configuration.CommandLine | MIT |
| Microsoft.Extensions.Configuration.EnvironmentVariables | MIT |
| Microsoft.Extensions.Configuration.Json | MIT |
| Microsoft.Extensions.Logging.Abstractions | MIT |

## NuGet packages — RepoDetox (CLI + stdio MCP)

| Package | License |
| --- | --- |
| CommandLineParser | MIT |
| Microsoft.Extensions.Hosting | MIT |
| ModelContextProtocol | Apache-2.0 |
| Serilog.Extensions.Hosting | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |

## NuGet packages — RepoDetox.Gui (desktop GUI)

| Package | License |
| --- | --- |
| Avalonia | MIT |
| Avalonia.Controls.DataGrid | MIT |
| Avalonia.Desktop | MIT |
| Avalonia.Diagnostics | MIT |
| Avalonia.Fonts.Inter | MIT |
| Avalonia.Themes.Fluent | MIT |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.DependencyInjection | MIT |
| Microsoft.Extensions.Logging | MIT |
| Serilog | Apache-2.0 |
| Serilog.Extensions.Logging | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |

## NuGet packages — RepoDetoxMCPSharp (HTTP MCP server)

| Package | License |
| --- | --- |
| Microsoft.AspNetCore.App | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | MIT |
| ModelContextProtocol.AspNetCore | Apache-2.0 |
| Serilog.AspNetCore | Apache-2.0 |
| Serilog.Enrichers.Environment | Apache-2.0 |
| Serilog.Enrichers.Process | Apache-2.0 |
| Serilog.Enrichers.Thread | Apache-2.0 |
| Serilog.Settings.Configuration | Apache-2.0 |
| Serilog.Sinks.Console | Apache-2.0 |
| Serilog.Sinks.File | Apache-2.0 |

The full text of the MIT and Apache-2.0 licenses is available at
<https://opensource.org/license/mit> and
<https://www.apache.org/licenses/LICENSE-2.0> respectively.

## Transitive natives (via Avalonia)

Avalonia pulls in SkiaSharp and HarfBuzzSharp (both MIT), which wrap the native
**Skia** (BSD-3-Clause, © Google) and **HarfBuzz** ("Old MIT") libraries — both
permissive and compatible with MIT distribution.

## Fonts

`Avalonia.Fonts.Inter` embeds the **Inter** typeface under the **SIL Open Font
License 1.1** (© The Inter Project Authors). The font remains under the OFL,
which permits bundling and redistribution within an MIT-licensed application.

## Git runtime dependency (not shipped)

RepoDetox drives history rewriting by shelling out to **git** (<https://git-scm.com/>),
which must be installed separately and available on `PATH` at runtime. Git is
licensed under GPL-2.0 and is **not** redistributed with this project.

## Note on filter-repo / filter-branch

History rewriting is performed by original C# code that reads and writes the
publicly-documented git fast-import stream format
(<https://git-scm.com/docs/git-fast-import>). **No source code is copied from
git-filter-repo or git-filter-branch** — only the documented stream format and
plumbing commands are referenced. The general technique of filtering a
`git fast-export` stream is shared with those tools (both associated with the
Git project, GPL-2.0), but RepoDetox reuses the documented git commands, not
their source.

## Trademarks

"Git" is a trademark of Software Freedom Conservancy, Inc. Use of the name in
this project does not imply endorsement by, or affiliation with, the Git
project or SFC.
