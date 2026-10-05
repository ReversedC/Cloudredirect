using CloudRedirect.Services.Providers;

namespace CloudRedirect.Services;

/// <summary>
/// Resolves a <see cref="CloudConfig"/> to the matching
/// <see cref="IUiCloudProvider"/> implementation. Returns <c>null</c> for
/// the local-only / unrecognized cases so the caller can short-circuit to
/// "nothing to do" without a switch of its own.
/// </summary>
internal static class UiCloudProviderFactory
{
    public static IUiCloudProvider? TryResolve(CloudConfig? config, Action<string>? log)
    {
        if (config == null) return null;
        return new CliUiCloudProvider("gdrive", log);
    }
}
