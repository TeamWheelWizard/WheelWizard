namespace WheelWizard.Features.Archives;

public sealed record DecodedArchive(IReadOnlyDictionary<string, byte[]> Files);
