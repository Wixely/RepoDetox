using CommandLine;

namespace RepoDetox;

[Verb("preview", HelpText = "Serve the current analysis in a browser-friendly debug view. Requires Preview:Enabled=true in appsettings.json.")]
public sealed class PreviewOptions : RepositoryOptions
{
    [Option('p', "port", Default = 5078, HelpText = "Local HTTP port for the preview endpoint.")]
    public int Port { get; set; }
}
