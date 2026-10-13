namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The last step: the package's format and metadata, its build and a test install.</summary>
public sealed class PackageStepViewModel : ICreatorStepView
{
    public void OnShown()
    {
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(true);

    public void Dispose()
    {
    }
}
