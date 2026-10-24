// What a blog picture is, read from its own bytes (ADR-113, the addendum on putting a picture in): its kind by
// its magic numbers, never its file name, and its width and height from its own header (PNG's IHDR, JPEG's
// start-of-frame, WebP's VP8, VP8L or VP8X chunk). No image library: the three headers are a few fixed
// offsets each, and a test holds one file of each kind. Pure: bytes in, the facts or null out.
using System.Buffers.Binary;

namespace TheYard.Domain.Features.Blog;

// #region blog-image-headers
/// <summary>A picture's kind and size as its header states them.</summary>
/// <param name="Kind">webp, png or jpg.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
public sealed record BlogImageHeader(string Kind, int Width, int Height);

/// <summary>The limits a picture is held to, and the reader of its header.</summary>
public static class BlogImageHeaders
{
    /// <summary>The most bytes a picture may be: one megabyte. The post lane resizes before it uploads.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The widest a picture may be, in pixels.</summary>
    public const int MaxWidth = 2000;

    /// <summary>The picture's kind and size, or null when its bytes are not a PNG, a JPEG or a WebP with a header this reads.</summary>
    /// <param name="bytes">The picture.</param>
    public static BlogImageHeader? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(Png) && bytes[12..16].SequenceEqual("IHDR"u8))
        {
            return new("png", (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]));
        }
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return Jpeg(bytes);
        }
        if (bytes.Length >= 30 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP(bytes);
        }
        return null;
    }

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The first start-of-frame marker's height and width, walking the segments from the top.</summary>
    private static BlogImageHeader? Jpeg(ReadOnlySpan<byte> bytes)
    {
        int at = 2;
        while (at + 9 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }
            byte marker = bytes[at + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..]);
            // SOF0 to SOF15, leaving out DHT (C4), JPG (C8) and DAC (CC), which share the range.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                return new("jpg", BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..]));
            }
            at += 2 + length;
        }
        return null;
    }

    /// <summary>The first chunk's canvas: VP8 (lossy), VP8L (lossless) or VP8X (extended).</summary>
    private static BlogImageHeader? WebP(ReadOnlySpan<byte> bytes)
    {
        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8 "u8) && bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A)
        {
            return new("webp", BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) & 0x3FFF);
        }
        if (chunk.SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
        {
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes[21..]);
            return new("webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }
        if (chunk.SequenceEqual("VP8X"u8))
        {
            return new("webp", Int24(bytes[24..]) + 1, Int24(bytes[27..]) + 1);
        }
        return null;
    }

    private static int Int24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
}
// #endregion blog-image-headers
