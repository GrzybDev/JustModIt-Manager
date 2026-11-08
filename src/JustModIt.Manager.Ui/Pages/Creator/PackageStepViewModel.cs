using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The last step: the package's description, its build and a test install of it.</summary>
public sealed partial class PackageStepViewModel : ObservableObject, ICreatorStepView
{
    private readonly ProjectPageViewModel _page;
    private readonly CreatorSession _session;

    private (GameKey? Key, string? Folder) _game;

    /// <summary>Creates the step for a project.</summary>
    /// <param name="page">The project's page.</param>
    /// <param name="session">The project.</param>
    public PackageStepViewModel(ProjectPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _game = (session.Version?.Game.Key, session.Version?.GameFolder);
        Description = new PackageDescriptionViewModel(page, session);
        Test = new TestInstallViewModel(page);
        Build = new BuildViewModel(page, session, Description, Test);
        _session.PropertyChanged += OnSessionChanged;
    }

    public PackageDescriptionViewModel Description { get; }

    public BuildViewModel Build { get; }

    public TestInstallViewModel Test { get; }

    public void OnShown()
    {
        if (_session.Review?.Plan is { Format.Kind: PackageKind.Manifest } && _session.Project.Metadata.Title.Length == 0
            && _session.Version?.Game is { } game)
        {
            _session.Describe(project => project with { Metadata = project.Metadata with { Title = game.Name } });
        }

        _ = Description.HintAsync();
        Show();
        Test.ShowTest();
    }

    public Task<bool> CanLeaveAsync() => Build.CanStopAsync();

    public void Dispose()
    {
        Build.Dispose();
        Test.Dispose();
        _session.PropertyChanged -= OnSessionChanged;
    }

    [RelayCommand]
    private Task FinishAsync() => _page.FinishAsync();

    [RelayCommand]
    private void Back() => _page.GoTo(CreatorStep.Review);

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        var version = _session.Version;
        if (_game != (version?.Game.Key, version?.GameFolder))
        {
            _game = (version?.Game.Key, version?.GameFolder);
            // A package and its test belong to the game it was built for: another copy of the game, e.g. from another store,
            // has other files, so its package is built anew, and a test install never takes the other copy's package.
            Test.Forget();
            Build.Forget();
        }

        // The files compared anew are what the failed build asked for, so its failure no longer stands in the way.
        if (e.PropertyName == nameof(CreatorSession.Comparison) && _session.Comparison is not null)
        {
            Build.FilesCompared();
        }

        Show();
    }

    private void Show()
    {
        Description.Show();
        Build.Show();
    }
}
