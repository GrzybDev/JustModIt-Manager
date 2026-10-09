namespace JustModIt.Manager.Core;

/// <summary>The system does not say where the user's data belongs, so the app cannot start safely.</summary>
public sealed class AppDataUnavailableException : Exception
{
    /// <summary>Creates the exception with a general message.</summary>
    public AppDataUnavailableException()
        : base("The folder for the user's application data could not be resolved.")
    {
    }
}
