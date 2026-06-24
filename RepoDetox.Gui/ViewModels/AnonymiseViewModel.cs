using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class AnonymiseViewModel : TabViewModelBase
{
    private readonly RepositoryAnonymiseService _anonymiseService;

    [ObservableProperty]
    private bool anonymiseUsers = true;

    [ObservableProperty]
    private bool anonymiseEmails = true;

    [ObservableProperty]
    private string? fixedName;

    [ObservableProperty]
    private string? fixedEmail;

    public AnonymiseViewModel(
        RepositoryAnonymiseService anonymiseService,
        RepositorySession session,
        OperationCoordinator coordinator)
        : base(session, coordinator)
    {
        _anonymiseService = anonymiseService;
    }

    [RelayCommand]
    private Task AnonymiseAsync() => Coordinator.RunAsync(
        startMessage: null,
        async (reporter, cancellationToken) =>
        {
            var name = NullIfBlank(FixedName);
            var email = NullIfBlank(FixedEmail);
            var (nameMode, emailMode) = IdentityRewritePlan.Resolve(AnonymiseUsers, AnonymiseEmails, name, email);

            var request = new AnonymiseRequest(
                Session.RepositoryPath!,
                SkipConfirmation: false,
                nameMode,
                emailMode,
                name,
                email);

            var result = await _anonymiseService.AnonymiseAsync(request, reporter, cancellationToken);
            return result.Message;
        });

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
