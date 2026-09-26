namespace JustModIt.Manager.Core;

/// <summary>The system does not say where the user's data belongs, so the app cannot start safely.</summary>
public sealed class AppDataUnavailableException : Exception
{
    /// <summary>Creates the exception with a general message.</summary>
    public AppDataUnavailableException()
        : base("The folder for the user's application data could not be resolved.")
    {
    }

    /// <summary>Creates the exception with a message of its own.</summary>
    /// <param name="message">The English message for the logs.</param>
    public AppDataUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception for an error that kept the folder from being resolved.</summary>
    /// <param name="message">The English message for the logs.</param>
    /// <param name="innerException">The error that kept the folder from being resolved.</param>
    public AppDataUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
