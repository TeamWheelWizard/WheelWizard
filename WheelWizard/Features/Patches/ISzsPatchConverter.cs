namespace WheelWizard.Features.Patches;

public interface ISzsPatchConverter
{
    OperationResult<PatchConversionAnalysis> AnalyzeAgainstBaseline(BaselineEntry baseline, string moddedName, byte[] moddedBytes);

    int EstimateDifference(BaselineEntry baseline, byte[] moddedBytes);
}
