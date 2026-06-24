# Third-Party Notices

RepoDetox itself is licensed under the MIT License (see [LICENSE](LICENSE)).

History rewriting is performed in original C# code derived from the publicly
documented git fast-import stream format
(https://git-scm.com/docs/git-fast-import). **No code is copied** from
git-filter-repo or any other project; only the documented stream format is
referenced. The general technique of filtering a `git fast-export` stream is also
used by `git filter-branch` and git-filter-repo (both part of / associated with
the Git project, which is licensed under GPLv2) — RepoDetox reuses the documented
git commands, not their source code.

RepoDetox depends on the following NuGet packages at build/run time. All are
permissive and compatible with MIT distribution.

### Shared / CLI (`RepoDetox.Core`, `RepoDetox`)

| Package | Version | License |
| --- | --- | --- |
| CommandLineParser | 2.9.1 | MIT |
| Microsoft.Extensions.Hosting / Logging (and related Microsoft.Extensions.* packages) | 10.0.x | MIT |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 |
| Serilog.Settings.Configuration | 10.0.0 | Apache-2.0 |
| Serilog.Sinks.Console | 6.1.1 | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |

### Desktop GUI (`RepoDetox.Gui`)

| Package | Version | License |
| --- | --- | --- |
| Avalonia (+ Avalonia.Desktop / Themes.Fluent / Fonts.Inter / Diagnostics) | 11.2.3 | MIT |
| CommunityToolkit.Mvvm | 8.4.0 | MIT |
| Microsoft.Extensions.DependencyInjection / Logging | 10.0.x | MIT |
| Serilog | 4.2.0 | Apache-2.0 |
| Serilog.Extensions.Logging | 9.0.1 | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |

Avalonia transitively includes SkiaSharp and HarfBuzzSharp (both MIT) which wrap the
native Skia (BSD-3-Clause, © Google) and HarfBuzz (MIT-style "Old MIT") libraries — all
permissive and compatible with MIT distribution. `Avalonia.Fonts.Inter` embeds the **Inter**
typeface under the **SIL Open Font License 1.1** (© The Inter Project Authors); the font
remains under the OFL, which permits bundling and redistribution within an MIT-licensed app.

## Apache License 2.0 attribution (Serilog)

Serilog and its sinks are Copyright © Serilog Contributors and are licensed under
the Apache License, Version 2.0. You may obtain a copy of the license at
https://www.apache.org/licenses/LICENSE-2.0. The Apache-2.0 license is compatible
with redistribution under MIT; the Serilog binaries remain under Apache-2.0 and
carry no NOTICE file requiring additional attribution beyond this notice.

## Runtime requirement

RepoDetox shells out to **Git** (https://git-scm.com/), which must be installed
separately and available on `PATH`. Git is not redistributed with RepoDetox and
is licensed under the GNU General Public License, version 2.
