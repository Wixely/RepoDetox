using System.ComponentModel;
using ModelContextProtocol.Server;

namespace RepoDetox;

/// <summary>
/// Exposes RepoDetox's operations as MCP tools so an agent can drive them over stdio. Each tool
/// reuses the same Core services as the CLI. Destructive operations require an explicit
/// <c>confirm=true</c> argument before they rewrite history.
/// </summary>
[McpServerToolType]
public static class RepoDetoxMcpTools
{
    private const string ConfirmHint =
        "This is a destructive history rewrite. Re-call with confirm=true to proceed. " +
        "Rewriting history changes commit hashes and can affect clones, forks, and pull requests.";

    [McpServerTool(Name = "analyze_repository")]
    [Description("Scan a git repository and report files that were deleted from history but are still stored (removable), plus counts and estimated space savings. Read-only; does not modify the repository.")]
    public static async Task<string> AnalyzeRepository(
        RepositoryAnalyzer analyzer,
        [Description("Absolute path to the git repository.")] string path)
    {
        var result = await analyzer.AnalyzeAsync(path);
        return string.Join(Environment.NewLine, ScanReportFormatter.Describe(result));
    }

    [McpServerTool(Name = "vacuum_repository")]
    [Description("DESTRUCTIVE. Rewrite history to remove files that were deleted and are no longer present on any live ref, then garbage-collect to reclaim space. Pass confirm=true to actually run.")]
    public static async Task<string> VacuumRepository(
        RepositoryVacuumService service,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        if (!confirm)
        {
            return ConfirmHint;
        }

        var reporter = new CapturingOperationReporter();
        var result = await service.VacuumAsync(new VacuumRequest(path, SkipConfirmation: true), reporter);
        return Combine(reporter, result.Message);
    }

    [McpServerTool(Name = "anonymise_repository")]
    [Description("DESTRUCTIVE. Rewrite author/committer/tagger identities across all history. By default both username and email are replaced with a deterministic hash; supply setName/setEmail to use fixed values instead. Pass confirm=true to actually run.")]
    public static async Task<string> AnonymiseRepository(
        RepositoryAnonymiseService service,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Anonymise usernames (ignored if setName is provided).")] bool anonymiseUsers = true,
        [Description("Anonymise emails (ignored if setEmail is provided).")] bool anonymiseEmails = true,
        [Description("Set every username to this exact value instead of hashing.")] string? setName = null,
        [Description("Set every email to this exact value instead of hashing.")] string? setEmail = null,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        if (!confirm)
        {
            return ConfirmHint;
        }

        var fixedName = NullIfBlank(setName);
        var fixedEmail = NullIfBlank(setEmail);
        var (nameMode, emailMode) = IdentityRewritePlan.Resolve(anonymiseUsers, anonymiseEmails, fixedName, fixedEmail);

        var reporter = new CapturingOperationReporter();
        var request = new AnonymiseRequest(path, SkipConfirmation: true, nameMode, emailMode, fixedName, fixedEmail);
        var result = await service.AnonymiseAsync(request, reporter);
        return Combine(reporter, result.Message);
    }

    [McpServerTool(Name = "flatten_repository")]
    [Description("DESTRUCTIVE. Collapse all history into a single root commit matching the current HEAD state and delete every other branch, tag, and ref. Pass confirm=true to actually run.")]
    public static async Task<string> FlattenRepository(
        RepositoryFlattenService service,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        if (!confirm)
        {
            return ConfirmHint;
        }

        var reporter = new CapturingOperationReporter();
        var result = await service.FlattenAsync(new FlattenRequest(path, SkipConfirmation: true), reporter);
        return Combine(reporter, result.Message);
    }

    [McpServerTool(Name = "expunge_secrets")]
    [Description("DESTRUCTIVE. Replace literal secret strings with a token everywhere they appear in history (file contents and, by default, commit/tag messages), then update the working tree. Use this to scrub accidentally-committed secrets. Pass confirm=true to actually run. The secret was already committed, so rotate/revoke it regardless.")]
    public static async Task<string> ExpungeSecrets(
        RepositoryExpungeService service,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Literal secret strings to remove from history.")] string[] secrets,
        [Description("Text each secret is replaced with. Default '***REMOVED***'.")] string replacement = "***REMOVED***",
        [Description("Also replace inside commit/tag messages.")] bool includeMessages = true,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        if (secrets is null || secrets.Length == 0)
        {
            return "Provide at least one secret string to remove.";
        }

        if (!confirm)
        {
            return ConfirmHint + " Remember to rotate/revoke the secret regardless.";
        }

        var reporter = new CapturingOperationReporter();
        var request = new ExpungeRequest(
            path,
            SkipConfirmation: true,
            secrets,
            string.IsNullOrEmpty(replacement) ? "***REMOVED***" : replacement,
            includeMessages);
        var result = await service.ExpungeAsync(request, reporter);
        return Combine(reporter, result.Message);
    }

    private static string Combine(CapturingOperationReporter reporter, string message)
    {
        var log = reporter.GetText();
        return string.IsNullOrEmpty(log) ? message : $"{log}{Environment.NewLine}{message}";
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
