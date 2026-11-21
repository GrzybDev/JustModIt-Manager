namespace JustModIt.Manager.Agent.Shell;

/// <summary>
/// The app's program where the app uninstalls itself, such as an AppImage, which the system lists among no installed apps.
/// </summary>
public interface IAppProgram
{
    /// <summary>The program's file, which uninstalling the app names and removes last.</summary>
    string File { get; }

    /// <summary>
    /// Removes the program's file, then what its runs left outside the app's data, such as downloaded updates; only the file
    /// can fail, which leaves everything as it was.
    /// </summary>
    void Remove();
}
