namespace RepoDetox;

/// <summary>
/// Collects reported progress/warning lines into a string instead of writing to the console.
/// Used by the MCP server so an operation's output is returned to the calling agent as the tool
/// result. Confirmation is auto-approved because MCP tools gate destructive actions with an
/// explicit <c>confirm</c> argument and run with <c>SkipConfirmation = true</c>.
/// </summary>
public sealed class CapturingOperationReporter : IOperationReporter
{
    private readonly List<string> _lines = [];

    public void Report(string message) => _lines.Add(message);

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken) => Task.FromResult(true);

    public string GetText() => string.Join(Environment.NewLine, _lines);
}
