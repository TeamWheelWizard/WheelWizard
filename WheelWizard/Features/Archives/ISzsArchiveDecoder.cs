namespace WheelWizard.Features.Archives;

// normally this would be in the Domain folder but its just 1 sealed record

public interface ISzsArchiveDecoder
{
    DecodedArchive? TryDecodeU8Archive(byte[] bytes);

    byte[] DecompressYaz0IfNeeded(byte[] bytes);
}
