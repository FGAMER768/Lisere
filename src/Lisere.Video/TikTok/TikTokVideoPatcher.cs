using System.Buffers.Binary;
using System.Text;

namespace Lisere.Video.TikTok;

public sealed record TikTokPatchResult(int PatchedSamples);

public interface ITikTokVideoPatcher
{
    Task<TikTokPatchResult> PatchInPlaceAsync(
        string path,
        CancellationToken cancellationToken);
}

public sealed class TikTokPatchException : Exception
{
    public TikTokPatchException(string message)
        : base(message)
    {
    }

    public TikTokPatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Local port of CompressBase's OBX audio-tail method
/// (https://compressbase.com/method-api). The video is never re-encoded: the
/// last four AAC frames are replaced by a tiny in-band PCE followed by zero
/// padding, and a free box records the attribution. The operation is refused
/// when the container or AAC layout is not one the method can patch safely.
/// </summary>
public sealed class TikTokVideoPatcher : ITikTokVideoPatcher
{
    public const string Attribution = "Patched by Compressbase";

    private const long MaxMoovBytes = 256L * 1024 * 1024;

    public Task<TikTokPatchResult> PatchInPlaceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => PatchInPlace(path, cancellationToken), cancellationToken);
    }

    private static TikTokPatchResult PatchInPlace(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new TikTokPatchException("Le fichier vidéo est introuvable.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Mp4Box> topLevel;
        byte[] moov;

        using (var input = File.OpenRead(path))
        {
            topLevel = Mp4Parser.ReadTopLevel(input);
            ValidateTopLevel(topLevel);

            var moovBox = topLevel.Single(box => box.Type == "moov");
            if (moovBox.Size > MaxMoovBytes)
            {
                throw new TikTokPatchException("Le moov du fichier MP4 est trop volumineux.");
            }

            moov = new byte[checked((int)moovBox.Size)];
            input.Position = moovBox.Offset;
            input.ReadExactly(moov);
        }

        if (ContainsEncryption(moov))
        {
            throw new TikTokPatchException(
                "La méthode TikTok ne prend pas en charge les pistes chiffrées.");
        }

        var plan = AacTail.Read(moov, topLevel);
        var tempPath = path + ".lisere-tiktok.tmp";

        try
        {
            using (var input = File.OpenRead(path))
            using (var output = File.Create(tempPath))
            {
                var patches = BuildPatches(plan, cancellationToken);
                patches.Sort((left, right) => left.Offset.CompareTo(right.Offset));
                long cursor = 0;

                foreach (var patch in patches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CopyRange(input, output, cursor, patch.Offset - cursor);
                    input.Position = patch.Offset;
                    output.Write(patch.Bytes);
                    cursor = patch.Offset + patch.Bytes.Length;
                }

                CopyRange(input, output, cursor, input.Length - cursor);
                WriteAttribution(output);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        return new TikTokPatchResult(4);
    }

    private static void ValidateTopLevel(IReadOnlyList<Mp4Box> boxes)
    {
        if (boxes.Count == 0)
        {
            throw new TikTokPatchException("Le fichier n'est pas un MP4 valide.");
        }

        if (boxes.Any(box => box.Type is "moof" or "mfra" or "sidx" or "ssix"))
        {
            throw new TikTokPatchException(
                "La méthode TikTok exige un MP4 non fragmenté et indexé en tête.");
        }

        if (boxes.Count(box => box.Type == "moov") != 1)
        {
            throw new TikTokPatchException("Le MP4 doit contenir exactement un moov.");
        }

        if (!boxes.Any(box => box.Type == "mdat"))
        {
            throw new TikTokPatchException("Le MP4 ne contient aucune donnée média.");
        }
    }

    private static bool ContainsEncryption(byte[] moov) =>
        moov.AsSpan().IndexOf("sinf"u8) >= 0 ||
        moov.AsSpan().IndexOf("encv"u8) >= 0 ||
        moov.AsSpan().IndexOf("enca"u8) >= 0;

    private static List<SamplePatch> BuildPatches(
        AacTailPlan plan,
        CancellationToken cancellationToken)
    {
        var patches = new List<SamplePatch>(plan.SampleOffsets.Count);
        for (var index = 0; index < plan.SampleOffsets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var patched = new byte[plan.SampleSizes[index]];
            if (plan.PceSampleIndex < 0 || plan.PceSampleIndex == index)
            {
                plan.ReplacementPrefix.CopyTo(patched, 0);
            }
            patches.Add(new SamplePatch(plan.SampleOffsets[index], patched));
        }

        return patches;
    }

    private static void CopyRange(Stream input, Stream output, long start, long length)
    {
        if (length <= 0)
        {
            return;
        }

        input.Position = start;
        var buffer = new byte[64 * 1024];
        var remaining = length;

        while (remaining > 0)
        {
            var read = input.Read(
                buffer,
                0,
                (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                throw new TikTokPatchException("Le fichier MP4 est tronqué.");
            }

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteAttribution(Stream output)
    {
        var text = Encoding.ASCII.GetBytes(Attribution);
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(
            header,
            checked((uint)(8 + text.Length)));
        "free"u8.CopyTo(header[4..]);
        output.Write(header);
        output.Write(text);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SamplePatch(long Offset, byte[] Bytes);
}
