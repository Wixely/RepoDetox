using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

/// <summary>
/// Base for the feature-tab view models. Exposes the shared <see cref="RepositorySession"/> and
/// <see cref="OperationCoordinator"/> and a <see cref="CanRun"/> gate that tracks repository
/// validity and busy state.
/// </summary>
public abstract class TabViewModelBase : ObservableObject
{
    protected TabViewModelBase(RepositorySession session, OperationCoordinator coordinator)
    {
        Session = session;
        Coordinator = coordinator;

        Session.PropertyChanged += OnSessionPropertyChanged;
        Coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
    }

    public RepositorySession Session { get; }

    public OperationCoordinator Coordinator { get; }

    /// <summary>True when a valid repository is selected and no operation is currently running.</summary>
    public bool CanRun => Session.IsValidRepository && !Coordinator.IsBusy;

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RepositorySession.IsValidRepository))
        {
            OnPropertyChanged(nameof(CanRun));
        }
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OperationCoordinator.IsBusy))
        {
            OnPropertyChanged(nameof(CanRun));
        }
    }
}
