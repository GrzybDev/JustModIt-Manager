namespace JustModIt.Manager.Core.Catalog;

/// <summary>Why the site refused the API key.</summary>
public enum ApiKeyRefusal
{
    /// <summary>The site does not know the key, or it was revoked or has expired.</summary>
    Invalid,

    /// <summary>The key may not reach everything the app needs.</summary>
    NoAccess,

    /// <summary>The key works again once its owner settles what paused it, such as new API terms.</summary>
    Paused,
}

/// <summary>
/// Whether the site still takes the API key; a refused key is dropped for the rest of the run, so a key that was revoked
/// or replaced never locks an installed copy out.
/// </summary>
internal sealed class ApiKeyStatus
{
    private readonly TaskCompletionSource<ApiKeyRefusal> _refused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ApiKeyRefusal> Refused => _refused.Task;

    public bool IsRefused => _refused.Task.IsCompleted;

    /// <summary>Drops the key for the rest of the run; only the first refusal counts.</summary>
    /// <param name="reason">Why the site refused the key.</param>
    public void Refuse(ApiKeyRefusal reason) => _refused.TrySetResult(reason);
}

/// <summary>What the site says about the key the request carried.</summary>
internal sealed record ApiKeyDetails
{
    public IReadOnlyList<string> Scopes { get; init => field = value ?? []; } = [];
}
