namespace Lantern.Core.Extraction;

public enum ExtractionFailureCategory
{
    UnsupportedFormat,
    InvalidDocument,
    EncryptedDocument,
    FileTooLarge,
    ExtractionTimeout,
    ExtractionFailed,
    NoExtractableText,
    ResourceLimitExceeded
}
