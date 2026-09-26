using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Host;

/// <summary>What an app hands the shared startup about itself.</summary>
public sealed record AppOptions
{
    /// <summary>The app's texts, as its build merged them: <see cref="TextCatalog.Load"/> of the app's assembly.</summary>
    public required TextCatalog Texts { get; init; }
}
