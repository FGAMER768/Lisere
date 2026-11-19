namespace Lisere.Services;

/// <summary>
/// Produces user-facing error text. Localized errors keep their specific key,
/// explicitly safe messages pass through, and every other exception becomes a
/// generic message so decoder, JSON, ffmpeg or OS details never reach the UI.
/// </summary>
public static class UserErrorDescriber
{
    private const string UnexpectedErrorKey = "errorUnexpected";

    public static string Describe(ILocalizationService strings, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case IUserMessageError message:
                    return message.UserMessage;

                case ILocalizedError localized:
                    return localized.MessageArguments.Count == 0
                        ? strings[localized.MessageKey]
                        : strings.Format(localized.MessageKey, localized.MessageArguments.ToArray());
            }
        }

        return strings[UnexpectedErrorKey];
    }
}
