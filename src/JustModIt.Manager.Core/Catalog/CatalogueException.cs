using System.Net;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>Why the site or its storage could not give what was asked; the UI words each reason in its own text.</summary>
public enum CatalogueErrorReason
{
    Offline,
    Timeout,
    NotFound,
    Forbidden,
    RateLimited,
    Maintenance,
    ServerError,
    BadRequest,
    InvalidResponse,
    CorruptDownload,
    InsufficientSpace,
}

/// <summary>A failed request to the site's API or storage, with an English message meant for the logs only.</summary>
public sealed class CatalogueException : Exception
{
    /// <summary>Creates a server error with a general message.</summary>
    public CatalogueException()
        : this(CatalogueErrorReason.ServerError, "The request failed.")
    {
    }

    /// <summary>Creates a server error with a message of its own.</summary>
    /// <param name="message">The English message for the logs.</param>
    public CatalogueException(string message)
        : this(CatalogueErrorReason.ServerError, message)
    {
    }

    /// <summary>Creates a server error caused by another error.</summary>
    /// <param name="message">The English message for the logs.</param>
    /// <param name="innerException">The error that caused it.</param>
    public CatalogueException(string message, Exception innerException)
        : this(CatalogueErrorReason.ServerError, message, innerException)
    {
    }

    /// <summary>Creates the error for a failed request.</summary>
    /// <param name="reason">What went wrong, which the UI words.</param>
    /// <param name="message">The English message for the logs.</param>
    /// <param name="innerException">The error that caused it, if any.</param>
    /// <param name="statusCode">The HTTP status, when a response arrived.</param>
    /// <param name="retryAfter">How long the server asked to wait, when it said.</param>
    public CatalogueException(CatalogueErrorReason reason, string message, Exception? innerException = null,
        HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        Reason = reason;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public CatalogueErrorReason Reason { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    /// <summary>For <see cref="CatalogueErrorReason.InsufficientSpace"/>: what the download needs.</summary>
    public long? RequiredBytes { get; init; }

    /// <summary>For <see cref="CatalogueErrorReason.InsufficientSpace"/>: what is free where it would go.</summary>
    public long? AvailableBytes { get; init; }

    /// <summary>For <see cref="CatalogueErrorReason.InsufficientSpace"/>: the folder whose volume lacks the space.</summary>
    public string? Location { get; init; }

    /// <summary>Trying again later may succeed; the other reasons need something to change first.</summary>
    public bool IsTransient => Reason is CatalogueErrorReason.Offline or CatalogueErrorReason.Timeout
        or CatalogueErrorReason.RateLimited or CatalogueErrorReason.Maintenance or CatalogueErrorReason.ServerError;
}
