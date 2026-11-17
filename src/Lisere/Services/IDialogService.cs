namespace Lisere.Services;

public enum DialogKind
{
    Info,
    Warning,
    Error
}

public interface IDialogService
{
    bool IsAnyDialogOpen { get; }

    event EventHandler? DialogOpenStateChanged;

    Task ShowMessageAsync(string message, DialogKind kind);

    Task ShowWaitAsync(string message);

    Task HideWaitAsync();
}
