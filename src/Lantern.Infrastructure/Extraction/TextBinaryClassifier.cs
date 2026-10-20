namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Lightweight heuristic classifier to distinguish text files from binary files.
/// Samples up to 8 KB of file content to avoid reading large files into memory.
/// </summary>
public static class TextBinaryClassifier
{
    private const int SampleSize = 8192;

    public enum Classification { Text, Binary, Unknown }

    public static Classification Classify(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ClassifyStream(fs);
        }
        catch
        {
            return Classification.Unknown;
        }
    }

    public static Classification ClassifyBytes(ReadOnlySpan<byte> sample)
    {
        if (sample.Length == 0) return Classification.Text;

        // UTF-32 LE BOM: 00 00 FE FF — do not confuse null bytes with binary
        if (sample.Length >= 4 && sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
            return Classification.Text;
        if (sample.Length >= 4 && sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0xFE && sample[3] == 0xFF)
            return Classification.Text;

        // UTF-16 LE BOM: FF FE — null bytes in pairs are expected
        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
            return Classification.Text;

        // UTF-16 BE BOM: FE FF — null bytes in pairs are expected
        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
            return Classification.Text;

        // Count null bytes and non-printable characters
        int nullCount = 0;
        int nonPrintableCount = 0;
        int len = sample.Length;

        for (int i = 0; i < len; i++)
        {
            byte b = sample[i];
            if (b == 0x00)
            {
                nullCount++;
            }
            else if (b < 0x09 || (b > 0x0D && b < 0x20) || b == 0x7F)
            {
                // Non-printable: below tab, above CR but below space, DEL
                nonPrintableCount++;
            }
        }

        // Any null byte in non-UTF-16 content → likely binary
        if (nullCount > 0) return Classification.Binary;

        // More than 30% non-printable characters → likely binary
        if ((double)nonPrintableCount / len > 0.30) return Classification.Binary;

        return Classification.Text;
    }

    public static Classification ClassifyStream(Stream stream)
    {
        byte[] buffer = new byte[SampleSize];
        int read = stream.Read(buffer, 0, buffer.Length);
        return ClassifyBytes(buffer.AsSpan(0, read));
    }
}
