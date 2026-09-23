namespace WheelWizard.CloudSync.Providers;

public sealed class CloudProviderResolver(IEnumerable<ICloudProvider> providers) : ICloudProviderResolver
{
    public ICloudProvider Resolve(CloudProviderType providerType) =>
        providers.FirstOrDefault(provider => provider.ProviderType == providerType)
        ?? throw new NotSupportedException($"Cloud provider '{providerType}' is not available.");
}
