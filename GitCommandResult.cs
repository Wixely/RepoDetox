namespace RepoDetox;

public sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
