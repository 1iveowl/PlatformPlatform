namespace SharedKernel.Validation;

public sealed record InspectedImage(string ContentType, string FileExtension, int Width, int Height);

// Validates the container structure of JPEG, PNG, GIF and WebP files without decoding pixel data. The client file
// name and declared content type are untrusted, so the served content type and extension come from this result. A file
// is rejected when it is empty, truncated, carries bytes after the image trailer, uses a format outside the allowed
// set, or declares dimensions above the bounds, so an active-content document cannot pass by declaring an image type.
public static class ImageContentInspector
{
    public const string InvalidImageMessage = "Image must be a valid JPEG, PNG, GIF, or WebP file.";

    public const int MaximumDimension = 8192;

    public const long MaximumPixelCount = 40_000_000;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] Crc32Table = CreateCrc32Table();

    public static async Task<InspectedImage?> InspectAsync(Stream stream, long maximumLength, CancellationToken cancellationToken)
    {
        if (!stream.CanSeek || stream.Length == 0 || stream.Length > maximumLength)
        {
            return null;
        }

        var bytes = new byte[stream.Length];
        stream.Position = 0;
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        stream.Position = 0;

        return Inspect(bytes);
    }

    public static InspectedImage? Inspect(ReadOnlySpan<byte> bytes)
    {
        var image = InspectPng(bytes) ?? InspectJpeg(bytes) ?? InspectGif(bytes) ?? InspectWebP(bytes);
        return image is not null && HasAllowedDimensions(image.Width, image.Height) ? image : null;
    }

    private static bool HasAllowedDimensions(long width, long height)
    {
        return width is > 0 and <= MaximumDimension && height is > 0 and <= MaximumDimension && width * height <= MaximumPixelCount;
    }

    private static InspectedImage? InspectPng(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith(PngSignature))
        {
            return null;
        }

        var offset = PngSignature.Length;
        var width = 0;
        var height = 0;
        var hasImageData = false;
        var isFirstChunk = true;

        while (offset + 12 <= bytes.Length)
        {
            var length = ReadUInt32BigEndian(bytes, offset);
            if (length > int.MaxValue || offset + 12 + length > bytes.Length)
            {
                return null;
            }

            var chunkType = bytes.Slice(offset + 4, 4);
            var data = bytes.Slice(offset + 8, (int)length);
            var expectedCrc = ReadUInt32BigEndian(bytes, offset + 8 + (int)length);
            if (!IsAsciiLetters(chunkType) || ComputeCrc32(bytes.Slice(offset + 4, 4 + (int)length)) != expectedCrc)
            {
                return null;
            }

            var isHeader = chunkType.SequenceEqual("IHDR"u8);
            if (isFirstChunk != isHeader)
            {
                return null;
            }

            offset += 12 + (int)length;
            isFirstChunk = false;

            if (isHeader)
            {
                if (length != 13 || !IsValidPngHeader(data))
                {
                    return null;
                }

                width = (int)Math.Min(ReadUInt32BigEndian(data, 0), int.MaxValue);
                height = (int)Math.Min(ReadUInt32BigEndian(data, 4), int.MaxValue);
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                hasImageData = true;
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                return length == 0 && hasImageData && offset == bytes.Length ? new InspectedImage("image/png", "png", width, height) : null;
            }
            else if (!chunkType.SequenceEqual("PLTE"u8) && char.IsUpper((char)chunkType[0]))
            {
                // Unknown critical chunks make the image undecodable by definition
                return null;
            }
        }

        return null;
    }

    private static bool IsValidPngHeader(ReadOnlySpan<byte> header)
    {
        var bitDepth = header[8];
        var colorType = header[9];
        var isValidBitDepth = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => bitDepth is 8 or 16,
            _ => false
        };
        return isValidBitDepth && header[10] == 0 && header[11] == 0 && header[12] is 0 or 1;
    }

    private static InspectedImage? InspectJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        var offset = 2;
        var width = 0;
        var height = 0;
        var hasFrame = false;
        var hasScan = false;

        while (offset + 1 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                return null;
            }

            while (offset + 1 < bytes.Length && bytes[offset + 1] == 0xFF)
            {
                offset++;
            }

            if (offset + 1 >= bytes.Length)
            {
                return null;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xD9)
            {
                return hasFrame && hasScan && offset + 2 == bytes.Length ? new InspectedImage("image/jpeg", "jpg", width, height) : null;
            }

            if (marker is 0x00 or 0x01 or 0xD8 or >= 0xD0 and <= 0xD7 || offset + 4 > bytes.Length)
            {
                return null;
            }

            var segmentLength = (bytes[offset + 2] << 8) | bytes[offset + 3];
            var segmentEnd = offset + 2 + segmentLength;
            if (segmentLength < 2 || segmentEnd > bytes.Length)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (hasFrame || segmentLength < 8)
                {
                    return null;
                }

                height = (bytes[offset + 5] << 8) | bytes[offset + 6];
                width = (bytes[offset + 7] << 8) | bytes[offset + 8];
                hasFrame = true;
            }

            offset = segmentEnd;

            if (marker != 0xDA)
            {
                continue;
            }

            if (!hasFrame)
            {
                return null;
            }

            hasScan = true;
            offset = FindMarkerAfterEntropyCodedData(bytes, offset);
            if (offset < 0)
            {
                return null;
            }
        }

        return null;
    }

    private static int FindMarkerAfterEntropyCodedData(ReadOnlySpan<byte> bytes, int offset)
    {
        while (offset + 1 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var next = bytes[offset + 1];
            if (next is 0x00 or >= 0xD0 and <= 0xD7)
            {
                offset += 2;
                continue;
            }

            if (next == 0xFF)
            {
                offset++;
                continue;
            }

            return offset;
        }

        return -1;
    }

    private static InspectedImage? InspectGif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 14 || !(bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)))
        {
            return null;
        }

        var width = bytes[6] | (bytes[7] << 8);
        var height = bytes[8] | (bytes[9] << 8);
        var offset = 13 + ColorTableLength(bytes[10]);
        var imageCount = 0;

        while (offset < bytes.Length)
        {
            switch (bytes[offset])
            {
                case 0x3B:
                    return imageCount > 0 && offset + 1 == bytes.Length ? new InspectedImage("image/gif", "gif", width, height) : null;
                case 0x21:
                    offset = offset + 2 <= bytes.Length ? SkipGifSubBlocks(bytes, offset + 2) : -1;
                    break;
                case 0x2C:
                    if (offset + 10 > bytes.Length)
                    {
                        return null;
                    }

                    var minimumCodeSizeOffset = offset + 10 + ColorTableLength(bytes[offset + 9]);
                    if (minimumCodeSizeOffset >= bytes.Length || bytes[minimumCodeSizeOffset] is 0 or > 11)
                    {
                        return null;
                    }

                    offset = SkipGifSubBlocks(bytes, minimumCodeSizeOffset + 1);
                    imageCount++;
                    break;
                default:
                    return null;
            }

            if (offset < 0)
            {
                return null;
            }
        }

        return null;
    }

    private static int ColorTableLength(byte packedFields)
    {
        return (packedFields & 0x80) == 0 ? 0 : 3 * (1 << ((packedFields & 0x07) + 1));
    }

    private static int SkipGifSubBlocks(ReadOnlySpan<byte> bytes, int offset)
    {
        while (offset < bytes.Length)
        {
            var blockSize = bytes[offset];
            offset += 1 + blockSize;
            if (blockSize == 0)
            {
                return offset;
            }
        }

        return -1;
    }

    private static InspectedImage? InspectWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 || !bytes.StartsWith("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8) || ReadUInt32LittleEndian(bytes, 4) + 8L != bytes.Length)
        {
            return null;
        }

        var offset = 12;
        var chunkIndex = 0;
        var width = 0;
        var height = 0;
        var isExtendedFormat = false;
        var hasImageData = false;

        while (offset < bytes.Length)
        {
            if (offset + 8 > bytes.Length)
            {
                return null;
            }

            var chunkType = bytes.Slice(offset, 4);
            var chunkSize = ReadUInt32LittleEndian(bytes, offset + 4);
            var paddedEnd = offset + 8L + chunkSize + (chunkSize & 1);
            if (paddedEnd > bytes.Length)
            {
                return null;
            }

            var payload = bytes.Slice(offset + 8, (int)chunkSize);
            offset = (int)paddedEnd;

            if (chunkIndex++ == 0)
            {
                if (chunkType.SequenceEqual("VP8X"u8) && payload.Length >= 10)
                {
                    isExtendedFormat = true;
                    width = ReadUInt24LittleEndian(payload, 4) + 1;
                    height = ReadUInt24LittleEndian(payload, 7) + 1;
                    continue;
                }

                if (!TryReadWebPBitstreamDimensions(chunkType, payload, out width, out height))
                {
                    return null;
                }

                hasImageData = true;
                continue;
            }

            if (!isExtendedFormat)
            {
                return null;
            }

            if (chunkType.SequenceEqual("VP8 "u8) || chunkType.SequenceEqual("VP8L"u8))
            {
                if (hasImageData || !TryReadWebPBitstreamDimensions(chunkType, payload, out _, out _))
                {
                    return null;
                }

                hasImageData = true;
            }
            else if (chunkType.SequenceEqual("ANMF"u8))
            {
                hasImageData = true;
            }
        }

        return hasImageData ? new InspectedImage("image/webp", "webp", width, height) : null;
    }

    private static bool TryReadWebPBitstreamDimensions(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> payload, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (chunkType.SequenceEqual("VP8 "u8))
        {
            // A lossy key frame: frame tag bit 0 clear, the start code, then 14-bit width and height
            if (payload.Length < 10 || (payload[0] & 0x01) != 0 || payload[3] != 0x9D || payload[4] != 0x01 || payload[5] != 0x2A)
            {
                return false;
            }

            width = (payload[6] | (payload[7] << 8)) & 0x3FFF;
            height = (payload[8] | (payload[9] << 8)) & 0x3FFF;
            return true;
        }

        if (chunkType.SequenceEqual("VP8L"u8))
        {
            // A lossless stream: signature, 14-bit width minus one, 14-bit height minus one, alpha bit, 3-bit version zero
            if (payload.Length < 5 || payload[0] != 0x2F)
            {
                return false;
            }

            var bits = (uint)(payload[1] | (payload[2] << 8) | (payload[3] << 16) | (payload[4] << 24));
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return (bits >> 29) == 0;
        }

        return false;
    }

    private static bool IsAsciiLetters(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (!char.IsAsciiLetter((char)value))
            {
                return false;
            }
        }

        return true;
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return bytes[offset] | ((uint)bytes[offset + 1] << 8) | ((uint)bytes[offset + 2] << 16) | ((uint)bytes[offset + 3] << 24);
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc = Crc32Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
