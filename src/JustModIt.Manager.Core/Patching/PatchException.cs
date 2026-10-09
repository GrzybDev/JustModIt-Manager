namespace JustModIt.Manager.Core.Patching;

public enum PatchError
{
    /// <summary>The patch cannot be decoded: it is damaged, truncated or not a VCDIFF patch.</summary>
    Damaged,

    /// <summary>Decoding would produce more than the expected size.</summary>
    OutputTooLarge,

    /// <summary>The result differs in size or content from what the patch promises, e.g. for another source file.</summary>
    OutputMismatch,

    /// <summary>The native xdelta3 library is missing or does not match this version of the app.</summary>
    EngineUnavailable,
}

public sealed class PatchException : Exception
{
    /// <summary>Creates the exception for a typed failure.</summary>
    /// <param name="error">Why patching failed.</param>
    /// <param name="detail">xdelta3's own description; meant for logs.</param>
    /// <param name="innerException">The exception behind the failure, if any.</param>
    public PatchException(PatchError error, string detail, Exception? innerException = null)
        : base($"Patching failed: {error} ({detail}).", innerException) =>
        Error = error;

    public PatchError Error { get; }
}
