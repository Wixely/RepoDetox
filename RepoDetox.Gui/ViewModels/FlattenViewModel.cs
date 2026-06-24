using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class FlattenViewModel : TabViewModelBase
{
    private readonly RepositoryFlattenService _flattenService;

    public FlattenViewModel(
        RepositoryFlattenService flattenService,
        RepositorySession session,
        OperationCoordinator coordinator)
        : base(session, coordinator)
    {
        _flattenService = flattenService;
    }

    [RelayCommand]
    private Task FlattenAsync() => Coordinator.RunAsync(
        startMessage: null,
        async (reporter, cancellationToken) =>
        {
            var request = new FlattenRequest(Session.RepositoryPath!, SkipConfirmation: false);
            var result = await _flattenService.FlattenAsync(request, reporter, cancellationToken);
            return result.Message;
        });
}
