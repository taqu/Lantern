using System.Text;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Detects character encoding from byte content using BOM detection,
/// strict UTF-8 validation, and heuristic legacy encoding detection.
/// </summary>
public sealed class EncodingDetector
{
    static EncodingDetector()
    {
        // Register code-page encodings (Shift_JIS, EUC-JP, etc.) for .NET
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public sealed record DetectionResult(
        Encoding Encoding,
        string EncodingName,
        int BomLength,
        bool IsReliable);

    /// <summary>
    /// Detect encoding from byte content.
    /// Precedence: BOM → strict UTF-8 → Shift_JIS → EUC-JP → fail.
    /// </summary>
    public DetectionResult Detect(byte[] bytes, string? hintEncoding = null)
    {
        // Explicit hint from caller
        if (hintEncoding != null)
        {
            try
            {
                var enc = Encoding.GetEncoding(hintEncoding);
                return new DetectionResult(enc, hintEncoding, 0, IsReliable: true);
            }
            catch { /* invalid hint - fall through */ }
        }

        // BOM detection
        DetectionResult? bom = DetectBom(bytes);
        if (bom != null) return bom;

        // Strict UTF-8 validation
        if (IsStrictUtf8(bytes))
            return new DetectionResult(new UTF8Encoding(false, true), "utf-8", 0, IsReliable: true);

        // Heuristic: Shift_JIS (Windows-31J, CP932)
        if (TryDecode(bytes, 932, out _))
        {
            // Score: count plausible multi-byte Shift_JIS sequences
            int shiftjisScore = ScoreShiftJis(bytes);
            int eucjpScore = ScoreEucJp(bytes);

            if (shiftjisScore >= eucjpScore && shiftjisScore > 0)
            {
                var enc = Encoding.GetEncoding(932);
                return new DetectionResult(enc, "shift_jis", 0, IsReliable: shiftjisScore > 5);
            }
        }

        // Heuristic: EUC-JP
        if (ScoreEucJp(bytes) > 0)
        {
            try
            {
                var enc = Encoding.GetEncoding("euc-jp");
                return new DetectionResult(enc, "euc-jp", 0, IsReliable: false);
            }
            catch { /* EUC-JP not available */ }
        }

        // Unknown — caller must decide how to handle
        return new DetectionResult(new UTF8Encoding(false, false), "utf-8-fallback", 0, IsReliable: false);
    }

    public string Decode(byte[] bytes, DetectionResult detection)
    {
        int start = detection.BomLength;
        int length = bytes.Length - start;
        if (length <= 0) return string.Empty;
        return detection.Encoding.GetString(bytes, start, length);
    }

    private static DetectionResult? DetectBom(byte[] bytes)
    {
        if (bytes.Length >= 4)
        {
            // UTF-32 BE: 00 00 FE FF
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return new DetectionResult(Encoding.GetEncoding("utf-32BE"), "utf-32-be", 4, true);
            // UTF-32 LE: FF FE 00 00
            if (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                return new DetectionResult(Encoding.UTF32, "utf-32-le", 4, true);
        }
        if (bytes.Length >= 3)
        {
            // UTF-8 BOM: EF BB BF
            if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new DetectionResult(new UTF8Encoding(true), "utf-8-bom", 3, true);
        }
        if (bytes.Length >= 2)
        {
            // UTF-16 BE: FE FF
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new DetectionResult(Encoding.BigEndianUnicode, "utf-16-be", 2, true);
            // UTF-16 LE: FF FE (and not UTF-32 LE)
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new DetectionResult(Encoding.Unicode, "utf-16-le", 2, true);
        }
        return null;
    }

    private static bool IsStrictUtf8(byte[] bytes)
    {
        try
        {
            Decoder decoder = new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder();
            char[] chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length, flush: true)];
            decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: true);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecode(byte[] bytes, int codePage, out string result)
    {
        try
        {
            var enc = Encoding.GetEncoding(codePage,
                new EncoderExceptionFallback(),
                new DecoderExceptionFallback());
            result = enc.GetString(bytes);
            return true;
        }
        catch
        {
            result = string.Empty;
            return false;
        }
    }

    // Heuristic: count valid Shift_JIS 2-byte sequences
    private static int ScoreShiftJis(byte[] bytes)
    {
        int score = 0;
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            byte b = bytes[i];
            // Shift_JIS lead byte ranges: 0x81-0x9F, 0xE0-0xFC
            if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
            {
                byte trail = bytes[i + 1];
                // Shift_JIS trail byte ranges: 0x40-0x7E, 0x80-0xFC
                if ((trail >= 0x40 && trail <= 0x7E) || (trail >= 0x80 && trail <= 0xFC))
                {
                    score++;
                    i++; // skip trail byte
                }
            }
        }
        return score;
    }

    // Heuristic: count valid EUC-JP 2-byte sequences
    private static int ScoreEucJp(byte[] bytes)
    {
        int score = 0;
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            byte b = bytes[i];
            // EUC-JP lead byte: 0xA1-0xFE
            if (b >= 0xA1 && b <= 0xFE)
            {
                byte trail = bytes[i + 1];
                if (trail >= 0xA1 && trail <= 0xFE)
                {
                    score++;
                    i++;
                }
            }
            // Half-width kana: 0x8E + 0xA1-0xDF
            else if (b == 0x8E && i + 1 < bytes.Length)
            {
                byte trail = bytes[i + 1];
                if (trail >= 0xA1 && trail <= 0xDF)
                {
                    score++;
                    i++;
                }
            }
        }
        return score;
    }
}
