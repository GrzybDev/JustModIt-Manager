namespace JustModIt.Manager.Core.Platform;

/// <summary>Lets another process of the app bring its window to the front, which only a process in the foreground may allow.</summary>
public interface IForegroundHandoff
{
    /// <summary>Allows a process to bring its window to the front.</summary>
    /// <param name="processId">The id of the app's process that will show its window.</param>
    void Allow(int processId);
}
