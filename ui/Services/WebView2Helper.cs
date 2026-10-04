using System;
using System.IO;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace CloudRedirect.Services;

/// <summary>
/// Ensures WebView2Loader.dll is deployed and registered for single-file published .NET applications.
/// In single-file publish mode, native C++ DLLs are not auto-located by Windows LoadLibrary.
/// </summary>
public static class WebView2Helper
{
    private static bool _isLoaderConfigured;
    private static readonly object _lock = new();

    public static void EnsureLoaderConfigured()
    {
        if (_isLoaderConfigured) return;
        lock (_lock)
        {
            if (_isLoaderConfigured) return;

            try
            {
                // Primary directory: %LOCALAPPDATA%\CloudRedirect\app
                string appDataAppDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CloudRedirect", "app");
                Directory.CreateDirectory(appDataAppDir);

                string appDataDll = Path.Combine(appDataAppDir, "WebView2Loader.dll");

                // Check if we have embedded WebView2Loader.dll to deploy/verify
                var asm = typeof(WebView2Helper).Assembly;
                using (var stream = asm.GetManifestResourceStream("WebView2Loader.dll"))
                {
                    if (stream != null)
                    {
                        bool needExtract = !File.Exists(appDataDll) || new FileInfo(appDataDll).Length != stream.Length;
                        if (needExtract)
                        {
                            string tempDll = appDataDll + ".tmp";
                            using (var fs = new FileStream(tempDll, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                stream.CopyTo(fs);
                            }
                            if (File.Exists(appDataDll)) File.Delete(appDataDll);
                            File.Move(tempDll, appDataDll);
                            App.LogStartup($"Extracted embedded WebView2Loader.dll to {appDataDll}");
                        }
                    }
                }

                // Determine folder containing WebView2Loader.dll
                string? validFolder = null;

                if (File.Exists(appDataDll))
                {
                    validFolder = appDataAppDir;
                }
                else
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (File.Exists(Path.Combine(baseDir, "WebView2Loader.dll")))
                    {
                        validFolder = baseDir;
                    }
                    else if (File.Exists(Path.Combine(baseDir, "runtimes", "win-x64", "native", "WebView2Loader.dll")))
                    {
                        validFolder = Path.Combine(baseDir, "runtimes", "win-x64", "native");
                    }
                }

                if (!string.IsNullOrEmpty(validFolder))
                {
                    CoreWebView2Environment.SetLoaderDllFolderPath(validFolder);
                    App.LogStartup($"WebView2Loader path configured: {validFolder}");
                }
                else
                {
                    App.LogStartup("Warning: WebView2Loader.dll could not be found in any expected location.");
                }
            }
            catch (Exception ex)
            {
                App.LogStartup($"WebView2Loader configuration exception: {ex.Message}");
            }
            finally
            {
                _isLoaderConfigured = true;
            }
        }
    }
}
