namespace WheelWizard.Features.Archives;

public interface ISzsArchiveDecoder
{
    OperationResult<DecodedArchive> TryDecodeU8Archive(byte[] bytes);

    OperationResult<byte[]> DecompressYaz0IfNeeded(byte[] bytes);
}
