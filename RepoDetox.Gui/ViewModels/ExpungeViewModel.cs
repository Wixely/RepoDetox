using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class ExpungeViewModel : TabViewModelBase
{
    private readonly RepositoryExpungeService _expungeService;

    [ObservableProperty]
    private string secretsText = string.Empty;

    [ObservableProperty]
    private string replacement = "***REMOVED***";

    [ObservableProperty]
    private bool includeMessages = true;

    public ExpungeViewModel(
        RepositoryExpungeService expungeService,
        RepositorySession session,
        OperationCoordinator coordinator)
        : base(session, coordinator)
    {
        _expungeService = expungeService;
    }

    [RelayCommand]
    private Task ExpungeAsync() => Coordinator.RunAsync(
        startMessage: null,
        async (reporter, cancellationToken) =>
        {
            var secrets = ParseSecrets(SecretsText);
            if (secrets.Count == 0)
            {
                return "Enter at least one secret string (one per line).";
            }

            var request = new ExpungeRequest(
                Session.RepositoryPath!,
                SkipConfirmation: false,
                secrets,
                string.IsNullOrEmpty(Replacement) ? "***REMOVED***" : Replacement,
                IncludeMessages);

            var result = await _expungeService.ExpungeAsync(request, reporter, cancellationToken);
            return result.Message;
        });

    private static IReadOnlyList<string> ParseSecrets(string text) =>
        text.Replace("\r\n", "\n").Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
}
