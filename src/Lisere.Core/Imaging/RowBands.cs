namespace Lisere.Core.Imaging;

/// <summary>
/// Découpe une passe d'image en bandes de lignes exécutées en parallèle.
/// Chaque bande écrit des lignes disjointes : le résultat est identique à une
/// boucle séquentielle, octet pour octet, quel que soit le nombre de cœurs.
/// <para>
/// Aucun plafond de parallélisme n'est imposé : la machine décide. Les images
/// trop courtes restent séquentielles pour ne pas payer la synchronisation.
/// </para>
/// </summary>
public static class RowBands
{
    public const int DefaultRows = 32;

    public static void For(
        int height,
        CancellationToken cancellationToken,
        Action<int, int> body,
        int rowsPerBand = DefaultRows)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (height <= 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rows = Math.Max(1, rowsPerBand);
        var bandCount = (height + rows - 1) / rows;

        if (bandCount == 1)
        {
            body(0, height);
            return;
        }

        try
        {
            Parallel.For(
                0,
                bandCount,
                new ParallelOptions { CancellationToken = cancellationToken },
                band =>
                {
                    var start = band * rows;
                    var end = Math.Min(height, start + rows);
                    body(start, end);
                });
        }
        catch (AggregateException aggregate)
        {
            // Une annulation observée par plusieurs bandes arrive emballée :
            // les appelants attendent une OperationCanceledException, pas une
            // agrégation de tâches annulées.
            var inner = aggregate.Flatten().InnerExceptions;

            if (inner.Count > 0 && inner.All(exception => exception is OperationCanceledException))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (inner.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner[0]).Throw();
            }

            throw;
        }
    }
}
