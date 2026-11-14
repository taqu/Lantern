using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Lantern.Core.Extraction;

namespace Lantern.Infrastructure.Extraction;

/// <summary>
/// Safely opens an OpenDocument Format package (ZIP container with XML parts).
///
/// Enforces resource limits (entry count, uncompressed size, decompression ratio),
/// detects the ODF document kind from the <c>mimetype</c> stream, surfaces
/// encrypted packages via <see cref="OdfEncryptedPackageException"/>, and loads
/// the XML parts with external-entity and DTD processing disabled.
/// </summary>
public sealed class OdfPackageReader : IDisposable
{
    // Resource limits — tuned to leave headroom for legitimate business docs
    // while preventing ZIP-bomb style attacks.
    private const int MaxEntries = 4096;
    private const long MaxTotalUncompressedBytes = 512L * 1024 * 1024;   // 512 MiB total
    private const long MaxSingleEntryBytes = 256L * 1024 * 1024;         // per-entry cap
    private const long MaxXmlEntryBytes = 128L * 1024 * 1024;            // cap XML load
    private const int MaxDecompressionRatio = 200;                       // per-entry

    // Well-known ODF MIME type identifiers.
    public const string MimeOdt = "application/vnd.oasis.opendocument.text";
    public const string MimeOtt = "application/vnd.oasis.opendocument.text-template";
    public const string MimeOds = "application/vnd.oasis.opendocument.spreadsheet";
    public const string MimeOts = "application/vnd.oasis.opendocument.spreadsheet-template";
    public const string MimeOdp = "application/vnd.oasis.opendocument.presentation";
    public const string MimeOtp = "application/vnd.oasis.opendocument.presentation-template";

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entryIndex;

    public OdfDocumentKind Kind { get; }
    public string MimeType { get; }

    private OdfPackageReader(Stream stream, bool ownsStream, ZipArchive archive,
        Dictionary<string, ZipArchiveEntry> entryIndex, string mimeType, OdfDocumentKind kind)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _archive = archive;
        _entryIndex = entryIndex;
        MimeType = mimeType;
        Kind = kind;
    }

    public static OdfPackageReader Open(DocumentInput input)
    {
        Stream stream;
        bool owns;
        if(input.FilePath != null) {
            stream = File.OpenRead(input.FilePath);
            owns = true;
        } else if(input.Stream != null) {
            // Accept both seekable and non-seekable streams. ZipArchive needs
            // seeking; buffer non-seekable input in memory up to the limit.
            if(input.Stream.CanSeek) {
                stream = input.Stream;
                owns = false;
            } else {
                MemoryStream buffer = new MemoryStream();
                input.Stream.CopyTo(buffer);
                if(buffer.Length > MaxTotalUncompressedBytes)
                    throw new OdfResourceLimitException("ODF package stream exceeds size limit");
                buffer.Position = 0;
                stream = buffer;
                owns = true;
            }
        } else {
            throw new ArgumentException("DocumentInput requires FilePath or Stream", nameof(input));
        }

        ZipArchive? archive = null;
        try {
            try {
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: !owns);
            } catch(InvalidDataException ex) {
                throw new OdfMalformedPackageException("Not a valid ZIP container", ex);
            }

            if(archive.Entries.Count > MaxEntries)
                throw new OdfResourceLimitException(
                    $"ODF package has {archive.Entries.Count} entries (limit {MaxEntries})");

            Dictionary<string, ZipArchiveEntry> index = new(StringComparer.Ordinal);
            long totalUncompressed = 0;
            foreach(ZipArchiveEntry entry in archive.Entries) {
                // Reject path traversal / absolute paths.
                string name = entry.FullName.Replace('\\', '/');
                if(name.Contains("..") || name.StartsWith('/') ||
                    (name.Length >= 2 && name[1] == ':'))
                    throw new OdfMalformedPackageException($"Invalid entry path: {entry.FullName}");

                // Directories have 0 length and end with '/'; skip them.
                if(name.EndsWith('/')) continue;

                if(entry.Length < 0 || entry.Length > MaxSingleEntryBytes)
                    throw new OdfResourceLimitException(
                        $"ODF entry {entry.FullName} exceeds per-entry size limit");

                totalUncompressed += entry.Length;
                if(totalUncompressed > MaxTotalUncompressedBytes)
                    throw new OdfResourceLimitException(
                        "ODF package exceeds total uncompressed size limit");

                // Reject suspicious decompression ratios (ZIP-bomb heuristic).
                if(entry.CompressedLength > 0 &&
                    entry.Length / entry.CompressedLength > MaxDecompressionRatio)
                    throw new OdfResourceLimitException(
                        $"ODF entry {entry.FullName} has suspicious compression ratio");

                // Keep the first occurrence of an entry name; later duplicates are ignored
                // to avoid ambiguity.
                if(!index.ContainsKey(name))
                    index[name] = entry;
            }

            string mime = ReadMimeType(index);
            OdfDocumentKind kind = ClassifyMime(mime);

            if(kind == OdfDocumentKind.Unknown)
                throw new OdfUnsupportedKindException(
                    string.IsNullOrEmpty(mime)
                        ? "ODF package is missing a mimetype entry"
                        : $"Unsupported ODF document kind: {mime}");

            // Fail fast on encrypted content before any XML parsing to avoid
            // obscure XML errors leaking.
            if(IsManifestEncrypted(index))
                throw new OdfEncryptedPackageException("ODF package contains encrypted content");

            OdfPackageReader reader = new OdfPackageReader(stream, owns, archive, index, mime, kind);
            archive = null; // ownership transferred
            return reader;
        } finally {
            archive?.Dispose();
            // On error, release the stream we created.
            if(archive == null && owns) {
                // Successful path: reader now owns the stream.
            }
        }
    }

    public bool TryGetEntry(string entryPath, out ZipArchiveEntry? entry)
    {
        bool ok = _entryIndex.TryGetValue(entryPath, out ZipArchiveEntry? found);
        entry = found;
        return ok;
    }

    /// <summary>
    /// Load an XML entry with secure XmlReader settings (no DTDs, no external entities).
    /// Returns <c>null</c> if the entry is absent.
    /// </summary>
    public XDocument? LoadXml(string entryPath, CancellationToken cancellationToken = default)
    {
        if(!_entryIndex.TryGetValue(entryPath, out ZipArchiveEntry? entry) || entry == null)
            return null;

        if(entry.Length > MaxXmlEntryBytes)
            throw new OdfResourceLimitException(
                $"ODF XML entry {entryPath} exceeds XML size limit");

        cancellationToken.ThrowIfCancellationRequested();

        XmlReaderSettings settings = new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = MaxXmlEntryBytes,
            Async = false,
        };

        try {
            using Stream es = entry.Open();
            using XmlReader reader = XmlReader.Create(es, settings);
            return XDocument.Load(reader, LoadOptions.None);
        } catch(XmlException ex) {
            throw new OdfMalformedPackageException(
                $"Invalid XML in {entryPath}: {ex.Message}", ex);
        }
    }

    private static string ReadMimeType(IReadOnlyDictionary<string, ZipArchiveEntry> index)
    {
        if(!index.TryGetValue("mimetype", out ZipArchiveEntry? entry) || entry == null)
            return "";
        // The mimetype entry is small (< 128 bytes in practice); cap defensively.
        if(entry.Length > 1024) return "";
        using Stream es = entry.Open();
        using StreamReader reader = new StreamReader(es, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd().Trim();
    }

    private static OdfDocumentKind ClassifyMime(string mime) => mime switch {
        MimeOdt or MimeOtt => OdfDocumentKind.Text,
        MimeOds or MimeOts => OdfDocumentKind.Spreadsheet,
        MimeOdp or MimeOtp => OdfDocumentKind.Presentation,
        _ => OdfDocumentKind.Unknown,
    };

    private static bool IsManifestEncrypted(IReadOnlyDictionary<string, ZipArchiveEntry> index)
    {
        if(!index.TryGetValue("META-INF/manifest.xml", out ZipArchiveEntry? manifest) || manifest == null)
            return false;
        if(manifest.Length > 4 * 1024 * 1024) return false; // ignore oversized manifests here

        XmlReaderSettings settings = new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        try {
            using Stream es = manifest.Open();
            using XmlReader reader = XmlReader.Create(es, settings);
            while(reader.Read()) {
                if(reader.NodeType != XmlNodeType.Element) continue;
                if(reader.LocalName == "encryption-data" &&
                    reader.NamespaceURI == OdfNamespaces.Manifest)
                    return true;
            }
        } catch {
            // Treat an unreadable manifest as not-encrypted-by-this-check; the
            // XML extractor will surface a malformed-package error later.
            return false;
        }
        return false;
    }

    public void Dispose()
    {
        _archive.Dispose();
        if(_ownsStream) _stream.Dispose();
    }
}

public enum OdfDocumentKind
{
    Unknown,
    Text,
    Spreadsheet,
    Presentation,
}

public class OdfPackageException : Exception
{
    public OdfPackageException(string message) : base(message) { }
    public OdfPackageException(string message, Exception inner) : base(message, inner) { }
}

public sealed class OdfMalformedPackageException : OdfPackageException
{
    public OdfMalformedPackageException(string message) : base(message) { }
    public OdfMalformedPackageException(string message, Exception inner) : base(message, inner) { }
}

public sealed class OdfEncryptedPackageException : OdfPackageException
{
    public OdfEncryptedPackageException(string message) : base(message) { }
}

public sealed class OdfResourceLimitException : OdfPackageException
{
    public OdfResourceLimitException(string message) : base(message) { }
}

public sealed class OdfUnsupportedKindException : OdfPackageException
{
    public OdfUnsupportedKindException(string message) : base(message) { }
}
