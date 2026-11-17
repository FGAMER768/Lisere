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

    /// <summary>Asks for confirmation before a long render; true starts it.</summary>
    Task<bool> ConfirmRenderAsync(RenderPlan plan);

    /// <summary>
    /// Shows the blocking wait panel. <paramref name="onCancel"/> adds a cancel
    /// button; the operation remains responsible for hiding the panel.
    /// </summary>
    Task ShowWaitAsync(string message, Action? onCancel = null);

    Task HideWaitAsync();
}
