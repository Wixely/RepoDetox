using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class VacuumViewModel : TabViewModelBase
{
    private readonly RepositoryVacuumService _vacuumService;

    public VacuumViewModel(
        RepositoryVacuumService vacuumService,
        RepositorySession session,
        OperationCoordinator coordinator)
        : base(session, coordinator)
    {
        _vacuumService = vacuumService;
    }

    [RelayCommand]
    private Task VacuumAsync() => Coordinator.RunAsync(
        startMessage: null,
        async (reporter, cancellationToken) =>
        {
            var request = new VacuumRequest(Session.RepositoryPath!, SkipConfirmation: false);
            var result = await _vacuumService.VacuumAsync(request, reporter, cancellationToken);
            return result.Message;
        });
}
