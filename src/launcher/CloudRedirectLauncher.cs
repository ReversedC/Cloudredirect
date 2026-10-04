using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("CloudRedirect")]
[assembly: AssemblyDescription("CloudRedirect Steam Cloud Synchronization & Save Redirection Companion")]
[assembly: AssemblyProduct("CloudRedirect")]
[assembly: AssemblyVersion("2.9.91.0")]
[assembly: AssemblyFileVersion("2.9.91.0")]

namespace CloudRedirectLauncher
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
        private const int ASFW_ANY = -1;

        private const string ShowWindowEventName = @"Local\CloudRedirect_ShowMainWindow_Event_99214";
        private const string GlobalShowWindowEventName = @"Global\CloudRedirect_ShowMainWindow_Event_99214";

        private static bool TrySignalRunningInstance(string[] args)
        {
            try
            {
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string a = args[i];
                        if (!string.IsNullOrEmpty(a) && a.StartsWith("cloudredirect://", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudRedirect");
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                                File.WriteAllText(Path.Combine(dir, "ipc_command.txt"), a);
                            }
                            catch { }
                            break;
                        }
                    }
                }

                EventWaitHandle ewh = null;
                if (!EventWaitHandle.TryOpenExisting(GlobalShowWindowEventName, out ewh))
                {
                    EventWaitHandle.TryOpenExisting(ShowWindowEventName, out ewh);
                }

                if (ewh != null)
                {
                    ewh.Set();
                    ewh.Close();
                    try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string[] _savedArgs;

        [STAThread]
        private static void Main(string[] args)
        {
            _savedArgs = args;
            try { SetProcessDPIAware(); } catch { }

            // Store the path of this outer executable so the application and in-app updater know where it is located
            try
            {
                Environment.SetEnvironmentVariable("CLOUDREDIRECT_LAUNCHER_PATH", Application.ExecutablePath, EnvironmentVariableTarget.Process);
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int updateFromPid = 0;
            bool forceSetup = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (string.IsNullOrEmpty(a)) continue;

                    if (string.Equals(a, "--update-from-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int.TryParse(args[i + 1], out updateFromPid);
                    }
                    else if (a.StartsWith("--update-from-pid=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(a.Substring("--update-from-pid=".Length), out updateFromPid);
                    }
                    else if (string.Equals(a, "--test-setup", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(a, "-setup", StringComparison.OrdinalIgnoreCase))
                    {
                        forceSetup = true;
                    }
                }
            }

            // 1. If this is an Auto-Update launch, wait for the old PID to completely exit
            if (updateFromPid > 0)
            {
                try
                {
                    Process oldProc = Process.GetProcessById(updateFromPid);
                    if (!oldProc.WaitForExit(4000))
                    {
                        try { oldProc.Kill(); oldProc.WaitForExit(1000); } catch { }
                    }
                }
                catch { }
                Thread.Sleep(100);
            }
            else
            {
                // 2. Ultra-Fast Single-Instance Check (<5ms):
                // If CloudRedirect is already running, signal it to restore/show its window and exit immediately!
                if (TrySignalRunningInstance(args))
                {
                    return;
                }
            }

            // 1. If .NET 8 Desktop Runtime is already installed on this PC and not forced:
            // Extract the embedded payload if needed and launch immediately!
            if (!forceSetup && RuntimeChecker.IsDotNet8DesktopInstalled())
            {
                if (LaunchMainApp(args))
                {
                    return;
                }
                // Do NOT fall through to setup form if runtime is already installed;
                // LaunchMainApp has already displayed the specific error message to the user.
                return;
            }

            // 2. If .NET 8 Desktop Runtime is NOT installed (or --test-setup is used):
            // Show the Steam-styled dark Setup window with custom progress bar to download & install runtime
            Application.Run(new LauncherForm(args, forceSetup));
        }

        public static string GetTargetAppPath()
        {
            string currentExe = Application.ExecutablePath;

            // 1. Standard location: %LocalAppData%\CloudRedirect\app\CloudRedirect.Core.exe
            //    This is the canonical install path and should always be preferred.
            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloudRedirect", "app");
            string appTarget = Path.Combine(appDir, "CloudRedirect.Core.exe");

            // If the running executable IS the appTarget, avoid self-reference
            if (string.Equals(Path.GetFullPath(appTarget), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(appDir, "CloudRedirect.App.exe");
            }

            // Prefer %LocalAppData% path (even if it doesn't exist yet — EnsurePayloadExtracted will create it)
            return appTarget;
        }

        private static bool CompareStreamAndFileHash(Stream stream, string filePath)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    stream.Position = 0;
                    byte[] streamHash = md5.ComputeHash(stream);
                    stream.Position = 0;

                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        byte[] fileHash = md5.ComputeHash(fs);
                        if (streamHash.Length != fileHash.Length) return false;
                        for (int i = 0; i < streamHash.Length; i++)
                        {
                            if (streamHash[i] != fileHash[i]) return false;
                        }
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool EnsurePayloadExtracted()
        {
            try
            {
                string targetExe = GetTargetAppPath();
                string currentExe = Application.ExecutablePath;

                // Safety guard: target cannot be ourselves
                if (string.Equals(Path.GetFullPath(targetExe), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("MainAppPayload"))
                {
                    if (stream == null)
                    {
                        return File.Exists(targetExe);
                    }

                    if (File.Exists(targetExe))
                    {
                        var fi = new FileInfo(targetExe);
                        // If file size and MD5 hash match embedded stream, target is already perfectly up to date!
                        if (fi.Length == stream.Length && CompareStreamAndFileHash(stream, targetExe))
                        {
                            return true;
                        }

                        // Different version or size: terminate lingering processes to allow overwrite
                        try
                        {
                            foreach (var p in Process.GetProcessesByName("CloudRedirect.Core"))
                            {
                                try { p.Kill(); p.WaitForExit(3000); } catch { }
                            }
                        }
                        catch { }
                    }

                    string dir = Path.GetDirectoryName(targetExe);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string tempTarget = targetExe + ".tmp";
                    stream.Position = 0;
                    using (var fs = new FileStream(tempTarget, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fs.Write(buffer, 0, read);
                        }
                    }

                    bool extracted = false;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            if (File.Exists(targetExe))
                            {
                                File.Delete(targetExe);
                            }
                            if (File.Exists(tempTarget))
                            {
                                File.Move(tempTarget, targetExe);
                            }
                            extracted = true;
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(300);
                        }
                    }

                    if (!extracted)
                    {
                        Debug.WriteLine("Extraction error: Could not overwrite targetExe after 10 attempts.");
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Extraction error: " + ex.Message);
                return File.Exists(GetTargetAppPath());
            }
        }

        public static bool LaunchMainApp(string[] args)
        {
            try
            {
                // If any lingering CloudRedirect.Core is completely hung/not responding, terminate it first
                try
                {
                    foreach (var p in Process.GetProcessesByName("CloudRedirect.Core"))
                    {
                        if (!p.Responding)
                        {
                            try { p.Kill(); p.WaitForExit(1000); } catch { }
                        }
                    }
                }
                catch { }

                if (!EnsurePayloadExtracted())
                {
                    MessageBox.Show("Could not extract application payload. Please close any running CloudRedirect processes and try again.", "CloudRedirect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                string targetExe = GetTargetAppPath();
                string currentExe = Application.ExecutablePath;

                if (string.Equals(Path.GetFullPath(targetExe), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Configuration error: Launcher cannot launch itself recursively.", "CloudRedirect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                if (!File.Exists(targetExe))
                {
                    MessageBox.Show("Application executable not found:\n" + targetExe, "CloudRedirect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // Clean and deduplicate arguments to prevent runaway command-line growth
                var cleanArgs = new System.Collections.Generic.List<string>();
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string a = args[i];
                        if (string.IsNullOrEmpty(a)) continue;

                        if (string.Equals(a, "--launcher", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            {
                                i++; // skip launcher path argument
                            }
                            continue;
                        }
                        if (a.StartsWith("--launcher=", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (string.Equals(a, "--test-setup", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a, "-setup", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        cleanArgs.Add(a);
                    }
                }

                cleanArgs.Add("--launcher");
                cleanArgs.Add("\"" + currentExe + "\"");
                string arguments = string.Join(" ", cleanArgs.ToArray());

                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(targetExe),
                    UseShellExecute = true
                };

                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not launch CloudRedirect:\n" + ex.Message, "CloudRedirect", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }

    internal static class RuntimeChecker
    {
        public static bool IsDotNet8DesktopInstalled()
        {
            try
            {
                // 1. Check Registry x64
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"))
                {
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            if (name.StartsWith("8.", StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                // 2. Check %ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string dotnetPath = Path.Combine(pf, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(dotnetPath))
                {
                    foreach (var dir in Directory.GetDirectories(dotnetPath))
                    {
                        string folderName = Path.GetFileName(dir);
                        if (folderName.StartsWith("8.", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool IsVCRedistInstalled()
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("Installed");
                        if (val is int && ((int)val) == 1) return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    public class SteamProgressBar : Control
    {
        private int _value = 0;
        private int _maximum = 100;

        public SteamProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 18;
            BackColor = Color.FromArgb(16, 24, 34);
        }

        public int Value
        {
            get { return _value; }
            set
            {
                _value = Math.Max(0, Math.Min(_maximum, value));
                Invalidate();
            }
        }

        public int Maximum
        {
            get { return _maximum; }
            set
            {
                _maximum = Math.Max(1, value);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var brush = new SolidBrush(BackColor))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            if (_value > 0)
            {
                float pct = (float)_value / _maximum;
                int fillWidth = (int)(ClientRectangle.Width * pct);
                if (fillWidth > 0)
                {
                    var fillRect = new Rectangle(0, 0, fillWidth, Height);
                    using (var fillBrush = new LinearGradientBrush(fillRect, Color.FromArgb(32, 85, 128), Color.FromArgb(102, 192, 244), LinearGradientMode.Horizontal))
                    {
                        g.FillRectangle(fillBrush, fillRect);
                    }
                }
            }

            using (var pen = new Pen(Color.FromArgb(42, 71, 94), 1))
            {
                g.DrawRectangle(pen, 0, 0, ClientRectangle.Width - 1, Height - 1);
            }
        }
    }

    public class LauncherForm : Form
    {
        private string[] _args;
        private bool _forceSetup;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _statusLabel;
        private Label _detailsLabel;
        private SteamProgressBar _progressBar;
        private Button _actionButton;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public LauncherForm(string[] args, bool forceSetup = false)
        {
            _args = args;
            _forceSetup = forceSetup;
            InitializeUi();
            Shown += (s, e) => StartSetupWorkflow();
        }

        private void InitializeUi()
        {
            Text = "CloudRedirect Setup";
            ClientSize = new Size(520, 240);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(23, 26, 33);
            ForeColor = Color.FromArgb(198, 212, 223);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(27, 40, 56),
                Padding = new Padding(20, 12, 20, 12)
            };

            _titleLabel = new Label
            {
                Text = "CloudRedirect Setup",
                Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(102, 192, 244),
                AutoSize = true,
                Location = new Point(18, 12)
            };

            _subtitleLabel = new Label
            {
                Text = "Automated Component & Runtime Installer for fresh Windows PCs",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(143, 152, 160),
                AutoSize = true,
                Location = new Point(20, 36)
            };

            headerPanel.Controls.Add(_titleLabel);
            headerPanel.Controls.Add(_subtitleLabel);
            Controls.Add(headerPanel);

            _statusLabel = new Label
            {
                Text = "Checking system requirements...",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Location = new Point(24, 85),
                Size = new Size(470, 22)
            };
            Controls.Add(_statusLabel);

            _progressBar = new SteamProgressBar
            {
                Location = new Point(24, 114),
                Size = new Size(470, 20),
                Value = 0,
                Maximum = 100
            };
            Controls.Add(_progressBar);

            _detailsLabel = new Label
            {
                Text = "Initializing...",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(143, 152, 160),
                Location = new Point(24, 140),
                Size = new Size(470, 20)
            };
            Controls.Add(_detailsLabel);

            _actionButton = new Button
            {
                Text = "Cancel",
                Size = new Size(95, 32),
                Location = new Point(399, 185),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 71, 94),
                ForeColor = Color.FromArgb(198, 212, 223),
                Cursor = Cursors.Hand
            };
            _actionButton.FlatAppearance.BorderColor = Color.FromArgb(102, 192, 244);
            _actionButton.FlatAppearance.BorderSize = 1;
            _actionButton.Click += (s, e) =>
            {
                _cts.Cancel();
                Close();
            };
            Controls.Add(_actionButton);
        }

        private async void StartSetupWorkflow()
        {
            try
            {
                // 1. Check & Install .NET 8 Desktop Runtime (x64)
                if (_forceSetup || !RuntimeChecker.IsDotNet8DesktopInstalled())
                {
                    UpdateStatus("Downloading .NET 8 Desktop Runtime (x64)...", "Downloading official Microsoft component...");
                    string dotnetUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";
                    string destDotNet = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-8.0-win-x64.exe");

                    await DownloadFileWithProgress(dotnetUrl, destDotNet);

                    UpdateStatus("Installing .NET 8 Desktop Runtime...", "Running silent installation. Please wait a moment...");
                    _progressBar.Value = 100;

                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = destDotNet,
                            Arguments = "/install /quiet /norestart",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        var proc = Process.Start(psi);
                        proc.WaitForExit();
                        try { File.Delete(destDotNet); } catch { }
                    });
                }

                // 2. Check & Install Visual C++ Redistributable (x64)
                if (!RuntimeChecker.IsVCRedistInstalled())
                {
                    UpdateStatus("Downloading Visual C++ Redistributable (x64)...", "Downloading official Microsoft component...");
                    _progressBar.Value = 0;
                    string vcUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
                    string destVc = Path.Combine(Path.GetTempPath(), "vc_redist.x64.exe");

                    await DownloadFileWithProgress(vcUrl, destVc);

                    UpdateStatus("Installing Visual C++ Redistributable...", "Running silent installation...");
                    _progressBar.Value = 100;

                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = destVc,
                            Arguments = "/install /quiet /norestart",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        var proc = Process.Start(psi);
                        proc.WaitForExit();
                        try { File.Delete(destVc); } catch { }
                    });
                }

                // 3. Launch application
                UpdateStatus("Setup Complete!", "Launching CloudRedirect...");
                _progressBar.Value = 100;
                await System.Threading.Tasks.Task.Delay(500);

                if (!Program.LaunchMainApp(_args))
                {
                    UpdateStatus("Launch Error", "Could not start CloudRedirect after setup.");
                    _actionButton.Text = "Close";
                    return;
                }
                Close();
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex)
            {
                UpdateStatus("Setup Error", ex.Message);
                _actionButton.Text = "Close";
            }
        }

        private void UpdateStatus(string status, string details)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(status, details)));
                return;
            }
            _statusLabel.Text = status;
            _detailsLabel.Text = details;
        }

        private async System.Threading.Tasks.Task DownloadFileWithProgress(string url, string destination)
        {
            using (var client = new WebClient())
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                DateTime lastUpdate = DateTime.MinValue;

                client.DownloadProgressChanged += (s, e) =>
                {
                    if ((DateTime.Now - lastUpdate).TotalMilliseconds > 80 || e.ProgressPercentage == 100)
                    {
                        lastUpdate = DateTime.Now;
                        Invoke(new Action(() =>
                        {
                            _progressBar.Value = e.ProgressPercentage;
                            double mbReceived = e.BytesReceived / 1048576.0;
                            double mbTotal = e.TotalBytesToReceive / 1048576.0;
                            _detailsLabel.Text = mbTotal > 0
                                ? string.Format("{0:F1} MB / {1:F1} MB ({2}%)", mbReceived, mbTotal, e.ProgressPercentage)
                                : string.Format("{0:F1} MB downloaded", mbReceived);
                        }));
                    }
                };

                _cts.Token.Register(() => client.CancelAsync());
                await client.DownloadFileTaskAsync(new Uri(url), destination);
            }
        }
    }
}
