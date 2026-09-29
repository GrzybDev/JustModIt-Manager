namespace JustModIt.Manager.Core;

/// <summary>
/// Reports on the calling thread, unlike <see cref="Progress{T}"/>, which posts each report to its context and delivers
/// thousands of them late and out of order.
/// </summary>
/// <param name="report">Called with each report, on the thread that reports it.</param>
public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
