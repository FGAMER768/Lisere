namespace Lisere.Video.TikTok;

/// <summary>
/// Minimal ISO-BMFF box reader. Offsets in a parsed tree are relative to the
/// buffer handed to <see cref="Mp4Parser.ParseRange"/>; top-level offsets are
/// absolute file offsets.
/// </summary>
internal sealed class Mp4Box
{
    public required string Type { get; init; }

    public required long Offset { get; init; }

    public required long Size { get; init; }

    public required int HeaderSize { get; init; }

    public bool ExtendsToEnd { get; init; }

    public List<Mp4Box> Children { get; init; } = [];

    public long BodyOffset => Offset + HeaderSize;

    public long End => Offset + Size;
}

internal static class Mp4Parser
{
    private static readonly HashSet<string> Containers =
    [
        "moov", "trak", "mdia", "minf", "stbl", "edts", "dinf"
    ];

    /// <summary>Reads only the top-level box headers, never the media payload.</summary>
    public static IReadOnlyList<Mp4Box> ReadTopLevel(FileStream stream)
    {
        var boxes = new List<Mp4Box>();
        var header = new byte[16];
        long offset = 0;

        while (offset < stream.Length)
        {
            stream.Position = offset;
            var read = stream.Read(header, 0, header.Length);
            if (read < 8)
            {
                throw new TikTokPatchException("Le fichier MP4 est tronqué.");
            }

            long size = ReadUInt32BigEndian(header, 0);
            var headerSize = 8;
            var type = ReadType(header, 4);

            if (size == 1)
            {
                if (read < 16)
                {
                    throw new TikTokPatchException("Le fichier MP4 est tronqué.");
                }

                size = checked((long)ReadUInt64BigEndian(header, 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = stream.Length - offset;
            }

            if (size < headerSize || offset + size > stream.Length)
            {
                throw new TikTokPatchException("La taille d'une boîte MP4 est invalide.");
            }

            boxes.Add(new Mp4Box
            {
                Type = type,
                Offset = offset,
                Size = size,
                HeaderSize = headerSize,
                ExtendsToEnd = ReadUInt32BigEndian(header, 0) == 0
            });

            if (boxes.Count > 10_000)
            {
                throw new TikTokPatchException("Le fichier MP4 contient trop de boîtes.");
            }

            offset += size;
        }

        return boxes;
    }

    /// <summary>Parses a moov payload already loaded in memory.</summary>
    public static List<Mp4Box> ParseRange(ReadOnlySpan<byte> data, int start, int end, int depth = 0)
    {
        if (depth > 16)
        {
            throw new TikTokPatchException("Le MP4 est trop imbriqué.");
        }

        var boxes = new List<Mp4Box>();

        while (start < end)
        {
            if (end - start < 8)
            {
                throw new TikTokPatchException("Une boîte MP4 est tronquée.");
            }

            long size = ReadUInt32BigEndian(data, start);
            var headerSize = 8;
            var type = ReadType(data, start + 4);

            if (size == 1)
            {
                if (end - start < 16)
                {
                    throw new TikTokPatchException("Une boîte MP4 est tronquée.");
                }

                size = checked((long)ReadUInt64BigEndian(data, start + 8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = end - start;
            }

            if (size < headerSize || start + size > end)
            {
                throw new TikTokPatchException("La taille d'une boîte MP4 est invalide.");
            }

            var box = new Mp4Box
            {
                Type = type,
                Offset = start,
                Size = size,
                HeaderSize = headerSize,
                ExtendsToEnd = ReadUInt32BigEndian(data, start) == 0,
                Children = Containers.Contains(type)
                    ? ParseRange(
                        data,
                        start + headerSize,
                        checked(start + (int)size),
                        depth + 1)
                    : []
            };

            boxes.Add(box);
            start += (int)size;

            if (boxes.Count > 10_000)
            {
                throw new TikTokPatchException("Le fichier MP4 contient trop de boîtes.");
            }
        }

        return boxes;
    }

    public static Mp4Box Child(Mp4Box parent, string type) =>
        parent.Children.FirstOrDefault(box => box.Type == type)
        ?? throw new TikTokPatchException($"La boîte MP4 « {type} » est absente.");

    public static Mp4Box? OptionalChild(Mp4Box parent, string type) =>
        parent.Children.FirstOrDefault(box => box.Type == type);

    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24) |
        ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) |
        data[offset + 3];

    public static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> data, int offset) =>
        ((ulong)ReadUInt32BigEndian(data, offset) << 32) |
        ReadUInt32BigEndian(data, offset + 4);

    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static string ReadType(ReadOnlySpan<byte> data, int offset) =>
        System.Text.Encoding.ASCII.GetString(data.Slice(offset, 4));
}
