namespace JustModIt.Manager.Core.IO;

public interface IDocumentStore<T>
    where T : class
{
    /// <summary>Loads the stored document.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Null when nothing is stored yet, or when the stored document could not be read.</returns>
    Task<T?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="document"/> in place of the previous one.</summary>
    /// <param name="document">The document to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SaveAsync(T document, CancellationToken cancellationToken);
}
