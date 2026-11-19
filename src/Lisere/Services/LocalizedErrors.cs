namespace Lisere.Services;

/// <summary>
/// An exception that carries a resource key instead of a user-facing message.
/// The UI localizes it at display time; the exception message stays useful for
/// logs and tests.
/// </summary>
public interface ILocalizedError
{
    string MessageKey { get; }

    IReadOnlyList<object?> MessageArguments { get; }
}

/// <summary>
/// An exception whose message is already localized and safe to show directly.
/// Used when the text is assembled from resource strings before throwing.
/// </summary>
public interface IUserMessageError
{
    string UserMessage { get; }
}

public sealed class UserMessageException : InvalidOperationException, IUserMessageError
{
    public UserMessageException(string userMessage)
        : base(userMessage)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}

public sealed class ImageCodecException : InvalidOperationException, ILocalizedError
{
    public ImageCodecException(
        string messageKey,
        string message,
        Exception? innerException = null,
        params object?[] messageArguments)
        : base(message, innerException)
    {
        MessageKey = messageKey;
        MessageArguments = messageArguments;
    }

    public string MessageKey { get; }

    public IReadOnlyList<object?> MessageArguments { get; }
}

public sealed class PresetFormatException : FormatException, ILocalizedError
{
    public PresetFormatException(
        string messageKey,
        string message,
        Exception? innerException = null,
        params object?[] messageArguments)
        : base(message, innerException)
    {
        MessageKey = messageKey;
        MessageArguments = messageArguments;
    }

    public string MessageKey { get; }

    public IReadOnlyList<object?> MessageArguments { get; }
}
