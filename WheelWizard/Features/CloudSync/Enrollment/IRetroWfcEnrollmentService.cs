namespace WheelWizard.CloudSync.Enrollment;

public interface IRetroWfcEnrollmentService
{
    Task<EnrollmentState> GetStateAsync();
    Task<ProbeResult> ProbeOnlineProfileAsync();
    Task MarkVerifiedAsync();
}
