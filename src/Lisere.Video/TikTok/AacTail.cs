namespace Lisere.Video.TikTok;

internal sealed record AacTailPlan(
    IReadOnlyList<long> SampleOffsets,
    IReadOnlyList<int> SampleSizes,
    byte[] ReplacementPrefix,
    int PceSampleIndex);

internal static class AacTail
{
    private static readonly int[] SampleRates =
    [
        96_000, 88_200, 64_000, 48_000, 44_100, 32_000,
        24_000, 22_050, 16_000, 12_000, 11_025, 8_000, 7_350
    ];

    /// <summary>
    /// Reads the first (and only supported) audio track and returns the four
    /// final sample locations plus the in-band PCE that replaces their opening
    /// bytes. The method intentionally refuses anything it cannot prove safe:
    /// a wrong offset would corrupt the user's file.
    /// </summary>
    public static AacTailPlan Read(
        byte[] moov,
        IReadOnlyList<Mp4Box> topLevel)
    {
        var root = Mp4Parser.ParseRange(moov, 0, moov.Length);
        var moovBox = root.FirstOrDefault(box => box.Type == "moov")
            ?? throw new TikTokPatchException("Le MP4 ne contient pas de moov.");
        var tracks = moovBox.Children.Where(box => box.Type == "trak").ToArray();
        var audioTracks = tracks.Where(track => IsAudioTrack(track, moov)).ToArray();
        var videoTracks = tracks.Where(track => IsVideoTrack(track, moov)).ToArray();

        if (audioTracks.Length != 1)
        {
            throw new TikTokPatchException(
                "La méthode TikTok exige exactement une piste audio AAC.");
        }

        if (videoTracks.Length == 0)
        {
            throw new TikTokPatchException(
                "La méthode TikTok exige une piste vidéo et une piste audio.");
        }

        var audio = audioTracks[0];
        var stbl = Mp4Parser.Child(
            Mp4Parser.Child(
                Mp4Parser.Child(audio, "mdia"),
                "minf"),
            "stbl");

        var (sampleSizes, sampleRateIndex) = ReadAudioDescription(stbl, moov);
        var chunkOffsets = ReadChunkOffsets(stbl, moov);
        var sampleToChunk = ReadSampleToChunk(stbl, moov);
        var offsets = ResolveSampleOffsets(
            sampleSizes,
            chunkOffsets,
            sampleToChunk,
            topLevel);

        if (offsets.Count < 4)
        {
            throw new TikTokPatchException(
                "La piste audio doit contenir au moins quatre échantillons.");
        }

        var tailOffsets = offsets.Skip(offsets.Count - 4).ToArray();
        var tailSizes = sampleSizes.Skip(sampleSizes.Count - 4).ToArray();
        var pce = BuildPce(sampleRateIndex);

        return new AacTailPlan(
            tailOffsets,
            tailSizes,
            pce,
            SelectPceSample(tailSizes, pce.Length));
    }

    /// <summary>
    /// FFmpeg clôt presque toujours sa piste AAC par une frame de quelques
    /// octets : le marqueur ne peut pas y tenir. Il est alors écrit dans la plus
    /// grande des quatre frames, les autres restant remplies de zéros, puisque le
    /// patch remplace de toute façon ces quatre frames. -1 garde le marqueur
    /// dans les quatre quand elles sont toutes assez grandes.
    /// </summary>
    internal static int SelectPceSample(IReadOnlyList<int> tailSizes, int pceLength)
    {
        var largest = 0;
        for (var index = 1; index < tailSizes.Count; index++)
        {
            if (tailSizes[index] > tailSizes[largest])
            {
                largest = index;
            }
        }

        if (tailSizes[largest] < pceLength)
        {
            throw new TikTokPatchException(
                "Les quatre derniers échantillons AAC sont trop petits pour le patch TikTok. " +
                "Exportez avec un débit audio plus élevé (AAC-LC 128 kb/s ou plus).");
        }

        return tailSizes.All(size => size >= pceLength) ? -1 : largest;
    }

    internal static byte[] BuildPce(int sampleRateIndex)
    {
        if (sampleRateIndex is < 0 or >= 13)
        {
            throw new TikTokPatchException("L'index de fréquence AAC est invalide.");
        }

        var bits = new List<bool>();

        void Add(int value, int count)
        {
            for (var bit = count - 1; bit >= 0; bit--)
            {
                bits.Add(((value >> bit) & 1) != 0);
            }
        }

        foreach (var (value, count) in new (int Value, int Count)[]
        {
            (5, 3), (0, 4), (1, 2), (sampleRateIndex, 4),
            (0, 4), (0, 4), (0, 4), (2, 2), (0, 3), (0, 4),
            (0, 1), (0, 1), (0, 1), (2, 4), (5, 4)
        })
        {
            Add(value, count);
        }

        PadToByte(bits);
        Add(0, 8);

        foreach (var front in new[] { 2, 5 })
        {
            foreach (var (value, count) in new (int Value, int Count)[]
            {
                (3, 3), (front, 4), (0, 8), (0, 11), (0, 3)
            })
            {
                Add(value, count);
            }
        }

        Add(7, 3);
        PadToByte(bits);

        var bytes = new byte[bits.Count / 8];
        for (var index = 0; index < bits.Count; index++)
        {
            if (bits[index])
            {
                bytes[index / 8] |= (byte)(1 << (7 - (index % 8)));
            }
        }

        return bytes;
    }

    private static void PadToByte(List<bool> bits)
    {
        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }
    }

    private static bool IsAudioTrack(Mp4Box track, byte[] moov) =>
        HandlerType(track, moov) == "soun";

    private static bool IsVideoTrack(Mp4Box track, byte[] moov) =>
        HandlerType(track, moov) == "vide";

    private static string HandlerType(Mp4Box track, byte[] moov)
    {
        var mdia = Mp4Parser.Child(track, "mdia");
        var hdlr = Mp4Parser.Child(mdia, "hdlr");
        if (hdlr.BodyOffset + 12 > hdlr.End)
        {
            return string.Empty;
        }

        // The handler type follows version/flags and a 4-byte pre_defined field.
        return System.Text.Encoding.ASCII.GetString(
            moov,
            checked((int)hdlr.BodyOffset + 8),
            4);
    }

    private static (List<int> Sizes, int SampleRateIndex) ReadAudioDescription(
        Mp4Box stbl,
        byte[] moov)
    {
        var stsd = Mp4Parser.Child(stbl, "stsd");
        if (stsd.BodyOffset + 8 > stsd.End ||
            Mp4Parser.ReadUInt32BigEndian(moov, (int)stsd.BodyOffset + 4) != 1)
        {
            throw new TikTokPatchException(
                "La description audio AAC doit contenir une seule entrée.");
        }

        var entry = Mp4Parser.ParseRange(
            moov,
            checked((int)stsd.BodyOffset + 8),
            checked((int)stsd.End))[0];
        if (entry.Type != "mp4a" ||
            entry.BodyOffset + 28 > entry.End)
        {
            throw new TikTokPatchException(
                "La piste audio n'est pas un AAC non chiffré compatible.");
        }

        var body = checked((int)entry.BodyOffset);
        if (moov[body + 8] != 0 || moov[body + 9] != 0)
        {
            throw new TikTokPatchException(
                "La version de l'échantillon AAC n'est pas prise en charge.");
        }

        var channels = Mp4Parser.ReadUInt16BigEndian(moov, body + 16);
        if (channels is < 1 or > 2)
        {
            throw new TikTokPatchException(
                "La méthode TikTok exige un audio AAC mono ou stéréo.");
        }

        var sampleRate = Mp4Parser.ReadUInt32BigEndian(moov, body + 24) >> 16;
        var sampleRateIndex = Array.IndexOf(SampleRates, (int)sampleRate);
        if (sampleRateIndex < 0)
        {
            throw new TikTokPatchException(
                "La fréquence d'échantillonnage AAC n'est pas prise en charge.");
        }

        var children = Mp4Parser.ParseRange(
            moov,
            body + 28,
            checked((int)entry.End));
        var esds = children.FirstOrDefault(box => box.Type == "esds")
            ?? throw new TikTokPatchException("La configuration AAC (esds) est absente.");

        ValidateAacConfig(moov, esds, sampleRateIndex);

        var stsz = Mp4Parser.Child(stbl, "stsz");
        var count = checked((int)Mp4Parser.ReadUInt32BigEndian(moov, (int)stsz.BodyOffset + 8));
        var fixedSize = checked((int)Mp4Parser.ReadUInt32BigEndian(moov, (int)stsz.BodyOffset + 4));
        if (count <= 0 || count > 5_000_000 ||
            stsz.BodyOffset + 12 + (fixedSize == 0 ? count * 4L : 0) != stsz.End)
        {
            throw new TikTokPatchException("La table de tailles audio est invalide.");
        }

        var sizes = new List<int>(count);
        for (var index = 0; index < count; index++)
        {
            var size = fixedSize != 0
                ? fixedSize
                : checked((int)Mp4Parser.ReadUInt32BigEndian(
                    moov,
                    checked((int)stsz.BodyOffset + 12 + (index * 4))));
            if (size <= 0)
            {
                throw new TikTokPatchException("Un échantillon audio a une taille nulle.");
            }

            sizes.Add(size);
        }

        return (sizes, sampleRateIndex);
    }

    private static void ValidateAacConfig(
        byte[] moov,
        Mp4Box esds,
        int expectedSampleRateIndex)
    {
        var body = checked((int)esds.BodyOffset);
        if (body + 4 > esds.End)
        {
            throw new TikTokPatchException("La configuration AAC est tronquée.");
        }

        var es = ReadDescriptor(moov, body + 4, checked((int)esds.End));
        if (es.Tag != 3 || es.Start + 3 > es.End || moov[es.Start + 2] != 0)
        {
            throw new TikTokPatchException("Le descripteur AAC n'est pas pris en charge.");
        }

        byte[]? audioSpecificConfig = null;
        for (var at = es.Start + 3; at < es.End;)
        {
            var descriptor = ReadDescriptor(moov, at, es.End);
            if (descriptor.Tag == 4)
            {
                if (descriptor.End - descriptor.Start < 13 ||
                    moov[descriptor.Start] != 0x40)
                {
                    throw new TikTokPatchException("L'objet audio AAC n'est pas pris en charge.");
                }

                for (var childAt = descriptor.Start + 13; childAt < descriptor.End;)
                {
                    var child = ReadDescriptor(moov, childAt, descriptor.End);
                    if (child.Tag == 5)
                    {
                        audioSpecificConfig = moov[child.Start..child.End];
                    }

                    childAt = child.End;
                }
            }

            at = descriptor.End;
        }

        if (audioSpecificConfig is not { Length: >= 2 } asc ||
            (asc[0] >> 3) != 2 ||
            (((asc[0] & 7) << 1) | (asc[1] >> 7)) != expectedSampleRateIndex ||
            ((asc[1] >> 3) & 0x0F) is < 1 or > 2 ||
            (asc[1] & 7) != 0)
        {
            throw new TikTokPatchException(
                "La méthode TikTok exige un audio AAC-LC mono ou stéréo.");
        }
    }

    private static (byte Tag, int Start, int End) ReadDescriptor(
        byte[] data,
        int offset,
        int limit)
    {
        if (offset >= limit)
        {
            throw new TikTokPatchException("Un descripteur AAC est tronqué.");
        }

        var tag = data[offset++];
        var length = 0;

        for (var index = 0; index < 4; index++)
        {
            if (offset >= limit)
            {
                throw new TikTokPatchException("Un descripteur AAC est tronqué.");
            }

            var value = data[offset++];
            length = (length * 128) + (value & 127);
            if ((value & 128) == 0)
            {
                if (offset + length > limit)
                {
                    throw new TikTokPatchException("Un descripteur AAC est tronqué.");
                }

                return (tag, offset, offset + length);
            }
        }

        throw new TikTokPatchException("Un descripteur AAC est invalide.");
    }

    private static List<long> ReadChunkOffsets(Mp4Box stbl, byte[] moov)
    {
        var stco = Mp4Parser.OptionalChild(stbl, "stco");
        var co64 = Mp4Parser.OptionalChild(stbl, "co64");
        if (stco is not null && co64 is not null)
        {
            throw new TikTokPatchException(
                "Deux tables d'offsets audio sont présentes ; le patch est refusé.");
        }

        var table = stco ?? co64
            ?? throw new TikTokPatchException("La table d'offsets audio est absente.");
        var width = table.Type == "stco" ? 4 : 8;
        var count = checked((int)Mp4Parser.ReadUInt32BigEndian(moov, (int)table.BodyOffset + 4));
        if (count <= 0 || table.BodyOffset + 8 + (count * (long)width) != table.End)
        {
            throw new TikTokPatchException("La table d'offsets audio est invalide.");
        }

        var offsets = new List<long>(count);
        for (var index = 0; index < count; index++)
        {
            var at = checked((int)table.BodyOffset + 8 + (index * width));
            offsets.Add(width == 4
                ? Mp4Parser.ReadUInt32BigEndian(moov, at)
                : checked((long)Mp4Parser.ReadUInt64BigEndian(moov, at)));
        }

        return offsets;
    }

    private static List<(int FirstChunk, int SamplesPerChunk)> ReadSampleToChunk(
        Mp4Box stbl,
        byte[] moov)
    {
        var table = Mp4Parser.Child(stbl, "stsc");
        var count = checked((int)Mp4Parser.ReadUInt32BigEndian(moov, (int)table.BodyOffset + 4));
        if (count <= 0 || table.BodyOffset + 8 + (count * 12L) != table.End)
        {
            throw new TikTokPatchException("La table des chunks audio est invalide.");
        }

        var entries = new List<(int FirstChunk, int SamplesPerChunk)>(count);
        for (var index = 0; index < count; index++)
        {
            var at = checked((int)table.BodyOffset + 8 + (index * 12));
            var firstChunk = checked((int)Mp4Parser.ReadUInt32BigEndian(moov, at));
            var samplesPerChunk = checked((int)Mp4Parser.ReadUInt32BigEndian(moov, at + 4));
            var description = Mp4Parser.ReadUInt32BigEndian(moov, at + 8);
            if (firstChunk <= 0 || samplesPerChunk <= 0 || description != 1)
            {
                throw new TikTokPatchException("La table des chunks audio n'est pas prise en charge.");
            }

            if (firstChunk != 1 && entries.Count == 0)
            {
                throw new TikTokPatchException(
                    "La table des chunks audio ne commence pas au premier chunk.");
            }

            if (entries.Count > 0 && firstChunk <= entries[^1].FirstChunk)
            {
                throw new TikTokPatchException(
                    "La table des chunks audio n'est pas triée.");
            }

            entries.Add((firstChunk, samplesPerChunk));
        }

        return entries;
    }

    private static List<long> ResolveSampleOffsets(
        IReadOnlyList<int> sampleSizes,
        IReadOnlyList<long> chunkOffsets,
        IReadOnlyList<(int FirstChunk, int SamplesPerChunk)> sampleToChunk,
        IReadOnlyList<Mp4Box> topLevel)
    {
        var mediaRanges = topLevel
            .Where(box => box.Type == "mdat")
            .Select(box => (box.BodyOffset, box.End))
            .ToArray();
        if (mediaRanges.Length == 0)
        {
            throw new TikTokPatchException("Le MP4 ne contient pas de données média.");
        }

        var offsets = new List<long>(sampleSizes.Count);
        var sample = 0;
        var entry = 0;

        for (var chunk = 1; chunk <= chunkOffsets.Count; chunk++)
        {
            while (entry + 1 < sampleToChunk.Count &&
                sampleToChunk[entry + 1].FirstChunk <= chunk)
            {
                entry++;
            }

            var samplesPerChunk = sampleToChunk[entry].SamplesPerChunk;
            var offset = chunkOffsets[chunk - 1];

            for (var index = 0; index < samplesPerChunk; index++)
            {
                if (sample >= sampleSizes.Count)
                {
                    throw new TikTokPatchException("La table audio dépasse le nombre d'échantillons.");
                }

                offsets.Add(offset);
                offset += sampleSizes[sample];
                sample++;
            }
        }

        if (sample != sampleSizes.Count)
        {
            throw new TikTokPatchException("Tous les échantillons audio ne sont pas couverts.");
        }

        for (var index = 0; index < offsets.Count; index++)
        {
            var end = offsets[index] + sampleSizes[index];
            if (!mediaRanges.Any(range =>
                offsets[index] >= range.BodyOffset && end <= range.End))
            {
                throw new TikTokPatchException("Un échantillon audio sort de la zone média.");
            }
        }

        return offsets;
    }
}
