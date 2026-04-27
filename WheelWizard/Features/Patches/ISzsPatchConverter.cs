namespace WheelWizard.Features.Patches;

public interface ISzsPatchConverter
{
    PatchConversionAnalysis AnalyzeAgainstBaseline(BaselineEntry baseline, string moddedName, byte[] moddedBytes);

    int EstimateDifference(BaselineEntry baseline, byte[] moddedBytes);
}
