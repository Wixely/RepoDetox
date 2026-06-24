namespace RepoDetox;

/// <summary>
/// Sink for progress/warning messages and interactive confirmation, supplied by the caller
/// (CLI console or GUI) so the core history-rewrite services have no direct console coupling.
/// </summary>
public interface IOperationReporter
{
    /// <summary>Reports a single progress or warning line to the user.</summary>
    void Report(string message);

    /// <summary>
    /// Asks the user to confirm a destructive operation. Returns <c>true</c> to proceed.
    /// </summary>
    Task<bool> ConfirmAsync(string question, CancellationToken cancellationToken);
}
