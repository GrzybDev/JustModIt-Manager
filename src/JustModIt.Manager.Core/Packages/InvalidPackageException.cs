namespace JustModIt.Manager.Core.Packages;

public enum PackageError
{
    NotAnArchive,
    MissingManifest,
    InvalidManifest,

    /// <summary>The package was made for a newer version of the app.</summary>
    UnsupportedFormatVersion,

    /// <summary>The package needs an operation or feature that only a newer version of the app has.</summary>
    UnsupportedCapability,

    UnsafeEntryName,
    SymbolicLink,
    UnexpectedEntry,
    DuplicateEntry,
    MissingPayload,
    InvalidPath,
    TooManyEntries,
    TooLarge,
    PayloadMismatch,
    Empty,
    UnreadableEntry,
}

public static class PackageErrors
{
    /// <summary>The package is fine, but only a newer version of the app can install it.</summary>
    /// <param name="error">Why the package was refused.</param>
    public static bool NeedsNewerApp(this PackageError error) =>
        error is PackageError.UnsupportedFormatVersion or PackageError.UnsupportedCapability;
}

public sealed class InvalidPackageException : Exception
{
    /// <summary>Creates the error for a package that cannot be installed.</summary>
    /// <param name="error">What is wrong with the package.</param>
    /// <param name="detail">The offending entry, path, capability or version; meant for logs, not for users.</param>
    /// <param name="innerException">The error that revealed it, if any.</param>
    public InvalidPackageException(PackageError error, string? detail = null, Exception? innerException = null)
        : base(detail is null ? $"Invalid package: {error}." : $"Invalid package: {error} ({detail}).", innerException)
    {
        Error = error;
        Detail = detail;
    }

    /// <summary>Creates the error for an invalid manifest.</summary>
    public InvalidPackageException()
        : this(PackageError.InvalidManifest)
    {
    }

    /// <summary>Creates the error for an invalid manifest with a message of its own.</summary>
    /// <param name="message">The English message for the logs.</param>
    public InvalidPackageException(string message)
        : this(message, null!)
    {
    }

    /// <summary>Creates the error for an invalid manifest caused by another error.</summary>
    /// <param name="message">The English message for the logs.</param>
    /// <param name="innerException">The error that caused it.</param>
    public InvalidPackageException(string message, Exception innerException)
        : base(message, innerException) =>
        Error = PackageError.InvalidManifest;

    public PackageError Error { get; }

    public string? Detail { get; }
}
