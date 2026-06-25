using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RepoDetox.Gui.Services;

namespace RepoDetox.Gui.ViewModels;

public sealed partial class AnonymiseViewModel : TabViewModelBase
{
    private readonly RepositoryAnonymiseService _anonymiseService;
    private readonly RepositoryContributorService _contributorService;

    // Mode: anonymise everyone (default) vs replace specific contributors.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnonymiseEveryoneMode))]
    private bool replaceSpecificMode;

    // "Anonymise everyone" settings.
    [ObservableProperty]
    private bool anonymiseUsers = true;

    [ObservableProperty]
    private bool anonymiseEmails = true;

    [ObservableProperty]
    private string? fixedName;

    [ObservableProperty]
    private string? fixedEmail;

    // "Replace specific contributors" settings.
    [ObservableProperty]
    private Contributor? selectedSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsCustom))]
    private bool targetIsExisting = true;

    [ObservableProperty]
    private Contributor? selectedTarget;

    [ObservableProperty]
    private string? customTargetName;

    [ObservableProperty]
    private string? customTargetEmail;

    [ObservableProperty]
    private string contributorsStatus = "Load the repository's contributors to start.";

    public AnonymiseViewModel(
        RepositoryAnonymiseService anonymiseService,
        RepositoryContributorService contributorService,
        RepositorySession session,
        OperationCoordinator coordinator)
        : base(session, coordinator)
    {
        _anonymiseService = anonymiseService;
        _contributorService = contributorService;
    }

    public bool AnonymiseEveryoneMode
    {
        get => !ReplaceSpecificMode;
        set => ReplaceSpecificMode = !value;
    }

    public bool TargetIsCustom
    {
        get => !TargetIsExisting;
        set => TargetIsExisting = !value;
    }

    public ObservableCollection<Contributor> Contributors { get; } = [];

    public ObservableCollection<IdentityMapping> Mappings { get; } = [];

    [RelayCommand]
    private async Task LoadContributorsAsync()
    {
        if (!Session.IsValidRepository || string.IsNullOrWhiteSpace(Session.RepositoryPath))
        {
            ContributorsStatus = "Select a valid repository first.";
            return;
        }

        try
        {
            var contributors = await _contributorService.GetContributorsAsync(Session.RepositoryPath);
            Contributors.Clear();
            foreach (var contributor in contributors)
            {
                Contributors.Add(contributor);
            }

            ContributorsStatus = $"Loaded {contributors.Count} contributor(s).";
        }
        catch (Exception ex)
        {
            ContributorsStatus = $"Could not load contributors: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddMapping()
    {
        if (SelectedSource is null)
        {
            return;
        }

        string? targetName;
        string? targetEmail;

        if (TargetIsExisting)
        {
            if (SelectedTarget is null)
            {
                return;
            }

            targetName = SelectedTarget.Name;
            targetEmail = SelectedTarget.Email;
        }
        else
        {
            targetName = NullIfBlank(CustomTargetName);
            targetEmail = NullIfBlank(CustomTargetEmail);
            if (targetName is null && targetEmail is null)
            {
                return;
            }
        }

        Mappings.Add(new IdentityMapping(SelectedSource.Name, SelectedSource.Email, targetName, targetEmail));
    }

    [RelayCommand]
    private void RemoveMapping(IdentityMapping mapping) => Mappings.Remove(mapping);

    [RelayCommand]
    private Task AnonymiseAsync() => Coordinator.RunAsync(
        startMessage: null,
        async (reporter, cancellationToken) =>
        {
            AnonymiseRequest request;

            if (ReplaceSpecificMode)
            {
                if (Mappings.Count == 0)
                {
                    return "Add at least one contributor replacement first.";
                }

                // Only the mapped contributors change; everyone else is left unchanged.
                request = new AnonymiseRequest(
                    Session.RepositoryPath!,
                    SkipConfirmation: false,
                    IdentityRewriteMode.Keep,
                    IdentityRewriteMode.Keep,
                    FixedName: null,
                    FixedEmail: null,
                    Mappings.ToList());
            }
            else
            {
                var name = NullIfBlank(FixedName);
                var email = NullIfBlank(FixedEmail);
                var (nameMode, emailMode) = IdentityRewritePlan.Resolve(AnonymiseUsers, AnonymiseEmails, name, email);

                request = new AnonymiseRequest(
                    Session.RepositoryPath!,
                    SkipConfirmation: false,
                    nameMode,
                    emailMode,
                    name,
                    email);
            }

            var result = await _anonymiseService.AnonymiseAsync(request, reporter, cancellationToken);
            return result.Message;
        });

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
