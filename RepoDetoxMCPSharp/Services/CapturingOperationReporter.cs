using RepoDetox;

namespace RepoDetoxMCPSharp.Services;

/// <summary>
/// Collects reported progress/warning lines instead of writing to the console. The MCP tool combines
/// this text with the operation result and returns it to the calling agent. Confirmation is
/// auto-approved because destructive tools require an explicit <c>confirm=true</c> argument and run
/// with <c>SkipConfirmation=true</c>.
/// </summary>
public sealed class CapturingOperationReporter : IOperationReporter
{
    private readonly List<string> _lines = [];

    public void Report(string message) => _lines.Add(message);

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken) => Task.FromResult(true);

    public string GetText() => string.Join(Environment.NewLine, _lines);
}
