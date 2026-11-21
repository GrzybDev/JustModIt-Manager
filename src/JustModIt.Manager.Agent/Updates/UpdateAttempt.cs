using JustModIt.Manager.Core;

namespace JustModIt.Manager.Agent.Updates;

/// <summary>The record of the version the last run started the updater for, which tells an update that failed to apply.</summary>
public static class UpdateAttempt
{
    /// <summary>Reads the version the last run started the updater for.</summary>
    /// <param name="locations">The app's folders, whose state folder holds the record.</param>
    /// <returns>Null when no run started the updater since that version ran, or the record cannot be read.</returns>
    public static string? Read(AppDataLocations locations)
    {
        try
        {
            return File.ReadAllText(locations.UpdateAttemptFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
