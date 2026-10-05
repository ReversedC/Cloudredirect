using System;
using System.IO;
using System.Threading.Tasks;

namespace CloudRedirect.Services;

/// <summary>
/// Automatically detects compatible Steam unlock tools (e.g. OpenSteamTool, HubcapTools)
/// and configures CloudRedirect without requiring manual user setup.
/// </summary>
public static class AutoSetupService
{
    public sealed record SetupResult(
        bool Success,
        string? DetectedTool,
        string Message);

    public static async Task<SetupResult> RunAutoSetupAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var steamPath = SteamDetector.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                {
                    return new SetupResult(false, null, "Steam path not found.");
                }

                // Check compatible or blocked tools
                var detection = ThirdPartyDetector.Detect(steamPath);
                if (detection.Status == ThirdPartyDetector.DetectionStatus.Blocked)
                {
                    return new SetupResult(
                        false,
                        detection.DetectedTool,
                        "Incompatible tool detected (StealIdra / LumaCore). Deployment blocked.");
                }

                // 1. Deploy or update cloud_redirect.dll
                var dllPath = Path.Combine(steamPath, "cloud_redirect.dll");
                if (EmbeddedDll.IsAvailable())
                {
                    var isCurrent = EmbeddedDll.IsDeployedCurrent(dllPath);
                    if (!File.Exists(dllPath) || isCurrent == false)
                    {
                        var deployError = EmbeddedDll.DeployTo(dllPath);
                        if (deployError != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoSetup] DLL deploy warning: {deployError}");
                        }
                    }
                }

                // 2. Configure compatible tools automatically
                if (detection.DetectedTool == "OpenSteamTool")
                {
                    try
                    {
                        OpenSteamToolIntegration.EnsureCloudEnabled(steamPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoSetup] OST config warning: {ex.Message}");
                    }
                }

                // 3. Ensure default config exists
                EnsureDefaultConfig(steamPath);

                // 4. Ensure mode is saved as cloud_redirect / thirdparty
                ModeService.PersistMode("cloud_redirect", cloudRedirectEnabled: true);
                ModeService.SaveClientType("thirdparty");

                var toolName = detection.DetectedTool ?? "Steam";
                return new SetupResult(true, toolName, $"Auto-setup completed for {toolName}.");
            }
            catch (Exception ex)
            {
                return new SetupResult(false, null, ex.Message);
            }
        });
    }

    private static void EnsureDefaultConfig(string steamPath)
    {
        try
        {
            var configDir = SteamDetector.GetConfigDir();
            Directory.CreateDirectory(configDir);

            var configPath = SteamDetector.GetConfigFilePath();
            var gdriveTokenPath = Path.Combine(configDir, "gdrive_tokens.json");

            if (!File.Exists(configPath))
            {
                ConfigHelper.SaveConfig(configPath,
                    new[] { "provider", "token_path", "auto_update_dll", "sync_luas", "sync_achievements", "sync_playtime" },
                    writer =>
                    {
                        writer.WriteString("provider", "gdrive");
                        writer.WriteString("token_path", gdriveTokenPath);
                        writer.WriteBoolean("auto_update_dll", true);
                        writer.WriteBoolean("sync_luas", true);
                        writer.WriteBoolean("sync_achievements", true);
                        writer.WriteBoolean("sync_playtime", true);
                    });
            }
            else
            {
                var config = SteamDetector.ReadConfig();
                if (config != null && config.Provider != "gdrive")
                {
                    ConfigHelper.SaveConfig(configPath,
                        new[] { "provider", "token_path" },
                        writer =>
                        {
                            writer.WriteString("provider", "gdrive");
                            writer.WriteString("token_path", string.IsNullOrEmpty(config.TokenPath) ? gdriveTokenPath : config.TokenPath);
                        });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoSetup] Default config warning: {ex.Message}");
        }
    }
}
