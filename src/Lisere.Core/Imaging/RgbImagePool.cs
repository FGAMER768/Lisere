using System.Runtime.CompilerServices;

namespace Lisere.Core.Imaging;

/// <summary>
/// Réserve de tampons RGB de taille exacte. Le rendu vidéo loue un tampon par
/// frame décodée, par sortie de moteur et par étape de colorimétrie ; sans
/// réserve, une frame 4K ajoutait plusieurs tableaux de 25 Mo par image au
/// Large Object Heap, que le GC ne compacte pas.
/// <para>
/// La réserve est bornée en octets : au-delà du budget, les tampons rendus sont
/// abandonnés au GC au lieu d'être conservés. La mémoire atteint donc un
/// plateau au lieu de croître avec la durée de la vidéo.
/// </para>
/// <para>
/// La réserve ne devine pas la propriété : rendre un tampon encore utilisé le
/// corrompt silencieusement. Les appelants doivent rendre un tampon seulement
/// quand plus personne ne le lit (voir <see cref="Rent"/>).
/// </para>
/// </summary>
public sealed class RgbImagePool
{
    public const long DefaultBudgetBytes = 512L * 1024 * 1024;

    private readonly long _budgetBytes;
    private readonly object _gate = new();
    private readonly Dictionary<int, Stack<byte[]>> _buckets = [];
    private readonly HashSet<byte[]> _rented = new(ArrayReferenceComparer.Instance);
    private readonly HashSet<byte[]> _retained = new(ArrayReferenceComparer.Instance);

    private long _retainedBytes;
    private long _peakRetainedBytes;
    private int _allocatedBuffers;
    private int _reusedBuffers;
    private int _returnedBuffers;
    private int _adoptedBuffers;
    private int _droppedBuffers;

    public RgbImagePool(long budgetBytes = DefaultBudgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        _budgetBytes = budgetBytes;
    }

    public long BudgetBytes => _budgetBytes;

    /// <summary>Tampons loués et pas encore rendus, tous tailles confondues.</summary>
    public int OutstandingBuffers
    {
        get
        {
            lock (_gate)
            {
                return _rented.Count;
            }
        }
    }

    public RgbImage Rent(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var length = checked(width * height * 3);
        return new RgbImage(width, height, RentBuffer(length));
    }

    public byte[] RentBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        lock (_gate)
        {
            if (_buckets.TryGetValue(length, out var bucket) && bucket.Count > 0)
            {
                var reused = bucket.Pop();
                _retainedBytes -= reused.Length;
                _retained.Remove(reused);
                _rented.Add(reused);
                _reusedBuffers++;
                return reused;
            }

            _allocatedBuffers++;
            var allocated = new byte[length];
            _rented.Add(allocated);
            return allocated;
        }
    }

    public void Return(RgbImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ReturnBuffer(image.Pixels);
    }

    /// <summary>
    /// Rend un tampon. Un tampon déjà rendu est ignoré : une double restitution
    /// ne doit pas permettre à deux frames de partager le même tableau.
    /// </summary>
    public void ReturnBuffer(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        lock (_gate)
        {
            if (_rented.Remove(buffer))
            {
                _returnedBuffers++;
            }
            else if (_retained.Contains(buffer))
            {
                // Double restitution : le tampon est déjà dans la réserve.
                // L'ajouter une seconde fois permettrait à deux frames de le
                // partager, ce qui corromprait les images en silence.
                return;
            }
            else
            {
                // Un moteur peut rendre un tableau qu'il a alloué lui-même :
                // l'adopter évite de le jeter quand la réserve a de la place.
                _adoptedBuffers++;
            }

            if (_retainedBytes + buffer.Length > _budgetBytes)
            {
                _droppedBuffers++;
                return;
            }

            if (!_buckets.TryGetValue(buffer.Length, out var bucket))
            {
                bucket = new Stack<byte[]>();
                _buckets[buffer.Length] = bucket;
            }

            bucket.Push(buffer);
            _retained.Add(buffer);
            _retainedBytes += buffer.Length;
            _peakRetainedBytes = Math.Max(_peakRetainedBytes, _retainedBytes);
        }
    }

    public RgbImagePoolStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new RgbImagePoolStatistics(
                _allocatedBuffers,
                _reusedBuffers,
                _returnedBuffers,
                _adoptedBuffers,
                _droppedBuffers,
                _rented.Count,
                _retainedBytes,
                _peakRetainedBytes,
                _budgetBytes);
        }
    }

    private sealed class ArrayReferenceComparer : IEqualityComparer<byte[]>
    {
        public static readonly ArrayReferenceComparer Instance = new();

        public bool Equals(byte[]? first, byte[]? second) => ReferenceEquals(first, second);

        public int GetHashCode(byte[] array) => RuntimeHelpers.GetHashCode(array);
    }
}

/// <summary>Compteurs observables d'une réserve, pour les tests et la trace de rendu.</summary>
public readonly record struct RgbImagePoolStatistics(
    int AllocatedBuffers,
    int ReusedBuffers,
    int ReturnedBuffers,
    int AdoptedBuffers,
    int DroppedBuffers,
    int OutstandingBuffers,
    long RetainedBytes,
    long PeakRetainedBytes,
    long BudgetBytes);
