using System.Threading;
using Lisere.Views.Dialogs;

namespace Lisere.Services;

public sealed class DialogService : IDialogService
{
    private readonly ILocalizationService _localization;
    private readonly SemaphoreSlim _messageGate = new(1, 1);
    private int _pendingMessages;
    private bool _isMessageOpen;
    private bool _isWaitOpen;
    private MessageDialog? _messageDialog;
    private WaitDialog? _waitDialog;

    public DialogService(ILocalizationService localization) =>
        _localization = localization;

    public bool IsAnyDialogOpen => _isMessageOpen || _isWaitOpen;

    public event EventHandler? DialogOpenStateChanged;

    public void Attach(MessageDialog messageDialog, WaitDialog waitDialog)
    {
        _messageDialog = messageDialog;
        _waitDialog = waitDialog;
    }

    public async Task ShowMessageAsync(string message, DialogKind kind)
    {
        _pendingMessages++;
        await _messageGate.WaitAsync();

        try
        {
            if (_messageDialog is null)
            {
                return;
            }

            SetMessageOpen(true);
            await _messageDialog.ShowAsync(message, kind, TitleFor(kind));
        }
        finally
        {
            _pendingMessages--;

            if (_pendingMessages == 0)
            {
                SetMessageOpen(false);
            }

            _messageGate.Release();
        }
    }

    public async Task ShowWaitAsync(string message)
    {
        if (_waitDialog is null)
        {
            return;
        }

        SetWaitOpen(true);

        try
        {
            await _waitDialog.ShowAsync(message, _localization["dialogWaitTitle"]);
        }
        catch
        {
            SetWaitOpen(false);
            throw;
        }
    }

    public async Task HideWaitAsync()
    {
        if (_waitDialog is null)
        {
            return;
        }

        try
        {
            await _waitDialog.HideAsync();
        }
        finally
        {
            SetWaitOpen(false);
        }
    }

    private string TitleFor(DialogKind kind) => kind switch
    {
        DialogKind.Error => _localization["dialogErrorTitle"],
        DialogKind.Warning => _localization["dialogWarningTitle"],
        _ => _localization["dialogInfoTitle"]
    };

    private void SetMessageOpen(bool open)
    {
        if (_isMessageOpen == open)
        {
            return;
        }

        _isMessageOpen = open;
        RaiseDialogStateChanged();
    }

    private void SetWaitOpen(bool open)
    {
        if (_isWaitOpen == open)
        {
            return;
        }

        _isWaitOpen = open;
        RaiseDialogStateChanged();
    }

    private void RaiseDialogStateChanged() =>
        DialogOpenStateChanged?.Invoke(this, EventArgs.Empty);
}
