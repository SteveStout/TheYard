namespace JobHunt.Server.Infrastructure.Shared.Share;

/// <summary>CRC-32 as a zip's headers carry it (IEEE 802.3, reflected, polynomial 0xEDB88320), read off a stream.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    /// <summary>The stream's length and CRC-32, read to its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="ct">Stops the read.</param>
    public static async Task<(long Length, uint Crc)> OfAsync(Stream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[81920];
        uint crc = 0xFFFFFFFF;
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            length += read;
            for (int i = 0; i < read; i++)
            {
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
        }
        return (length, crc ^ 0xFFFFFFFF);
    }

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
