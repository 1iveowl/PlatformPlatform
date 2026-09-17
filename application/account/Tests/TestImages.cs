using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace Account.Tests;

// Builds small, structurally valid images and damaged variants for the upload tests, without binary fixtures.
public static class TestImages
{
    public static byte[] Gif { get; } = Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");

    public static byte[] WebP { get; } = Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==");

    public static byte[] Png(int? totalLength = null, uint width = 1, uint height = 1)
    {
        var header = new byte[13];
        WriteUInt32BigEndian(header, 0, width);
        WriteUInt32BigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 6;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            zlib.Write([0, 255, 0, 0, 255]);
        }

        var chunks = PngChunk("IHDR", header).Concat(PngChunk("IDAT", compressed.ToArray())).ToArray();
        var end = PngChunk("IEND", []);
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var unpaddedLength = signature.Length + chunks.Length + end.Length;

        if (totalLength is null)
        {
            return [.. signature, .. chunks, .. end];
        }

        var keyword = "Comment\0"u8.ToArray();
        var padding = Enumerable.Repeat((byte)'a', totalLength.Value - unpaddedLength - 12 - keyword.Length).ToArray();
        return [.. signature, .. chunks, .. PngChunk("tEXt", [.. keyword, .. padding]), .. end];
    }

    // A 1x1 grayscale baseline JPEG: one DC and one AC Huffman code of length one encode a zero coefficient block.
    public static byte[] Jpeg()
    {
        byte[] quantizationTable = [0xFF, 0xDB, 0x00, 0x43, 0x00, .. Enumerable.Repeat((byte)1, 64)];
        byte[] frame = [0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00];
        byte[] huffmanCounts = [0x01, .. new byte[15]];
        byte[] dcTable = [0xFF, 0xC4, 0x00, 0x14, 0x00, .. huffmanCounts, 0x00];
        byte[] acTable = [0xFF, 0xC4, 0x00, 0x14, 0x10, .. huffmanCounts, 0x00];
        byte[] scan = [0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x3F];
        return [0xFF, 0xD8, .. quantizationTable, .. frame, .. dcTable, .. acTable, .. scan, 0xFF, 0xD9];
    }

    public static byte[] Truncated(byte[] image)
    {
        return image[..^1];
    }

    public static byte[] WithTrailingBytes(byte[] image, string trailing)
    {
        return [.. image, .. Encoding.UTF8.GetBytes(trailing)];
    }

    public static MultipartFormDataContent CreateForm(byte[] content, string contentType, string fileName)
    {
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var formData = new MultipartFormDataContent();
        formData.Add(fileContent, "file", fileName);
        return formData;
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        WriteUInt32BigEndian(chunk, 0, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        WriteUInt32BigEndian(chunk, 8 + data.Length, Crc32(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
