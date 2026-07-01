using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using RepoDetox;
using RepoDetoxMCPSharp.Configuration;
using RepoDetoxMCPSharp.Services;

namespace RepoDetoxMCPSharp.Tools;

/// <summary>
/// MCP tools that drive the same <see cref="RepoDetox.Core"/> operations the CLI uses, over the
/// HTTP MCP transport. Every tool name is prefixed with <c>repodetox_</c> so it can't collide with
/// generic verbs (<c>analyze</c>, <c>expunge</c>) exposed by other MCP servers an agent might have
/// loaded in the same session. Destructive tools carry a per-op safety gate on top of the master
/// <c>RepoDetox:ReadOnly</c> switch and additionally require an explicit <c>confirm=true</c>
/// argument before they rewrite history.
/// </summary>
[McpServerToolType]
public sealed class RepoDetoxTools
{
    private const string ConfirmHint =
        "This is a destructive history rewrite. Re-call with confirm=true to proceed. " +
        "Rewriting history changes commit hashes and can affect clones, forks, and pull requests.";

    [McpServerTool(Name = "repodetox_analyze_repository"),
     Description("Scan a git repository and report files that were deleted from history but are still stored (removable), plus counts and estimated space savings. Read-only; does not modify the repository.")]
    public static async Task<string> Analyze(
        RepositoryAnalyzer analyzer,
        [Description("Absolute path to the git repository.")] string path)
    {
        var result = await analyzer.AnalyzeAsync(path).ConfigureAwait(false);
        return string.Join(Environment.NewLine, ScanReportFormatter.Describe(result));
    }

    [McpServerTool(Name = "repodetox_vacuum_repository"),
     Description("DESTRUCTIVE. Rewrite history to remove files that were deleted and are no longer present on any live ref, then garbage-collect to reclaim space. Requires RepoDetox:ReadOnly=false + RepoDetox:AllowVacuum=true. Pass confirm=true to actually run.")]
    public static async Task<string> Vacuum(
        RepositoryVacuumService service,
        IOptions<RepoDetoxSafetyOptions> safety,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        EnsureAllowed("repodetox_vacuum_repository", safety.Value, o => o.AllowVacuum, "AllowVacuum");
        if (!confirm) return ConfirmHint;

        var reporter = new CapturingOperationReporter();
        var result = await service.VacuumAsync(new VacuumRequest(path, SkipConfirmation: true), reporter).ConfigureAwait(false);
        return Combine(reporter, result.Message);
    }

    [McpServerTool(Name = "repodetox_anonymise_repository"),
     Description("DESTRUCTIVE. Rewrite author/committer/tagger identities across all history. By default both username and email are replaced with a deterministic hash; supply setName/setEmail to use fixed values instead. Requires RepoDetox:ReadOnly=false + RepoDetox:AllowAnonymise=true. Pass confirm=true to actually run.")]
    public static async Task<string> Anonymise(
        RepositoryAnonymiseService service,
        IOptions<RepoDetoxSafetyOptions> safety,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Anonymise usernames (ignored if setName is provided).")] bool anonymiseUsers = true,
        [Description("Anonymise emails (ignored if setEmail is provided).")] bool anonymiseEmails = true,
        [Description("Set every username to this exact value instead of hashing.")] string? setName = null,
        [Description("Set every email to this exact value instead of hashing.")] string? setEmail = null,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        EnsureAllowed("repodetox_anonymise_repository", safety.Value, o => o.AllowAnonymise, "AllowAnonymise");
        if (!confirm) return ConfirmHint;

        var fixedName = NullIfBlank(setName);
        var fixedEmail = NullIfBlank(setEmail);
        var (nameMode, emailMode) = IdentityRewritePlan.Resolve(anonymiseUsers, anonymiseEmails, fixedName, fixedEmail);

        var reporter = new CapturingOperationReporter();
        var request = new AnonymiseRequest(path, SkipConfirmation: true, nameMode, emailMode, fixedName, fixedEmail);
        var result = await service.AnonymiseAsync(request, reporter).ConfigureAwait(false);
        return Combine(reporter, result.Message);
    }

    [McpServerTool(Name = "repodetox_flatten_repository"),
     Description("DESTRUCTIVE. Collapse all history into a single root commit matching the current HEAD state and delete every other branch, tag, and ref. Requires RepoDetox:ReadOnly=false + RepoDetox:AllowFlatten=true. Pass confirm=true to actually run.")]
    public static async Task<string> Flatten(
        RepositoryFlattenService service,
        IOptions<RepoDetoxSafetyOptions> safety,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        EnsureAllowed("repodetox_flatten_repository", safety.Value, o => o.AllowFlatten, "AllowFlatten");
        if (!confirm) return ConfirmHint;

        var reporter = new CapturingOperationReporter();
        var result = await service.FlattenAsync(new FlattenRequest(path, SkipConfirmation: true), reporter).ConfigureAwait(false);
        return Combine(reporter, result.Message);
    }

    [McpServerTool(Name = "repodetox_expunge_secrets"),
     Description("DESTRUCTIVE. Replace literal secret strings with a token everywhere they appear in history (file contents and, by default, commit/tag messages), then update the working tree. Use this to scrub accidentally-committed secrets. Requires RepoDetox:ReadOnly=false + RepoDetox:AllowExpunge=true. Pass confirm=true to actually run. The secret was already committed, so rotate/revoke it regardless.")]
    public static async Task<string> Expunge(
        RepositoryExpungeService service,
        IOptions<RepoDetoxSafetyOptions> safety,
        [Description("Absolute path to the git repository.")] string path,
        [Description("Literal secret strings to remove from history.")] string[] secrets,
        [Description("Text each secret is replaced with. Default '***REMOVED***'.")] string replacement = "***REMOVED***",
        [Description("Also replace inside commit/tag messages.")] bool includeMessages = true,
        [Description("Must be true to actually rewrite history.")] bool confirm = false)
    {
        EnsureAllowed("repodetox_expunge_secrets", safety.Value, o => o.AllowExpunge, "AllowExpunge");
        if (secrets is null || secrets.Length == 0) return "Provide at least one secret string to remove.";
        if (!confirm) return ConfirmHint + " Remember to rotate/revoke the secret regardless.";

        var reporter = new CapturingOperationReporter();
        var request = new ExpungeRequest(
            path,
            SkipConfirmation: true,
            secrets,
            string.IsNullOrEmpty(replacement) ? "***REMOVED***" : replacement,
            includeMessages);
        var result = await service.ExpungeAsync(request, reporter).ConfigureAwait(false);
        return Combine(reporter, result.Message);
    }

    private static void EnsureAllowed(string toolName, RepoDetoxSafetyOptions options, Func<RepoDetoxSafetyOptions, bool> isEnabled, string operationKey)
    {
        if (options.ReadOnly)
        {
            throw new McpException(
                $"MCP tool '{toolName}' is blocked by server configuration. " +
                $"Set RepoDetox:ReadOnly=false (and RepoDetox:{operationKey}=true) to allow this history rewrite.");
        }
        if (!isEnabled(options))
        {
            throw new McpException(
                $"MCP tool '{toolName}' is blocked by server configuration. " +
                $"Set RepoDetox:{operationKey}=true to allow this history rewrite.");
        }
    }

    private static string Combine(CapturingOperationReporter reporter, string message)
    {
        var log = reporter.GetText();
        return string.IsNullOrEmpty(log) ? message : $"{log}{Environment.NewLine}{message}";
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
