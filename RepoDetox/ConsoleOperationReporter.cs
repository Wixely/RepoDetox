namespace RepoDetox;

/// <summary>Console implementation of <see cref="IOperationReporter"/> used by the CLI.</summary>
public sealed class ConsoleOperationReporter : IOperationReporter
{
    public void Report(string message) => Console.WriteLine(message);

    public Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken)
    {
        Console.Write($"{question} [y/N]: ");
        var response = Console.ReadLine();
        var confirmed = string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(confirmed);
    }
}
