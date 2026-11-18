namespace Lisere.Services;

/// <summary>
/// Reports on the thread that runs the work; the dialog service marshals the
/// actual UI update, which keeps the progress sequence deterministic.
/// </summary>
public sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
