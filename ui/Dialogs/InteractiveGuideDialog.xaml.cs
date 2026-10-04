using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CloudRedirect.Pages;
using CloudRedirect.Resources;
using CloudRedirect.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace CloudRedirect.Dialogs;

public partial class InteractiveGuideDialog : FluentWindow
{
    public class GuideFeatureStep
    {
        public string Id { get; set; } = "";
        public Wpf.Ui.Controls.SymbolRegular Symbol { get; set; } = Wpf.Ui.Controls.SymbolRegular.Apps24;
        public System.Windows.Media.Brush IconColor { get; set; } = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Badge { get; set; } = "FEATURE";
        public string Description { get; set; } = "";
        public List<string> Highlights { get; set; } = new();
        public string HowItWorks { get; set; } = "";
        public string ProTip { get; set; } = "";
        public Type? TargetPageType { get; set; }
    }

    private List<GuideFeatureStep> _steps = new();
    private bool _isInitializing = true;

    public InteractiveGuideDialog(string? initialFeatureId = null)
    {
        InitializeComponent();

        PopulateLanguageSelector();
        BuildStepsForCurrentLanguage();

        _isInitializing = false;

        // Select initial step
        int initialIndex = 0;
        if (!string.IsNullOrEmpty(initialFeatureId))
        {
            var match = _steps.FindIndex(s => string.Equals(s.Id, initialFeatureId, StringComparison.OrdinalIgnoreCase));
            if (match >= 0) initialIndex = match;
        }

        FeatureTabsListBox.SelectedIndex = initialIndex;

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            if (Height > workArea.Height * 0.92)
            {
                Height = Math.Max(460, workArea.Height * 0.92);
            }
            if (Width > workArea.Width * 0.94)
            {
                Width = Math.Max(640, workArea.Width * 0.94);
            }
        };
    }

    private void PopulateLanguageSelector()
    {
        var currentPref = LanguageService.ReadLanguagePreference();
        GuideLanguageComboBox.Items.Clear();

        int selectedIndex = 0;
        var languages = LanguageService.SupportedLanguages;
        for (int i = 0; i < languages.Length; i++)
        {
            var lang = languages[i];
            var itemText = lang.Code == "system"
                ? S.Get(lang.ResourceKey)
                : lang.DisplayName;

            var cbi = new ComboBoxItem
            {
                Content = itemText,
                Tag = lang,
                FontSize = 12.5,
                Padding = new Thickness(8, 6, 8, 6)
            };
            GuideLanguageComboBox.Items.Add(cbi);

            if (string.Equals(lang.Code, currentPref, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
            }
        }

        GuideLanguageComboBox.SelectedIndex = selectedIndex;
    }

    private void GuideLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (GuideLanguageComboBox.SelectedItem is ComboBoxItem { Tag: LanguageService.LanguageItem item })
        {
            LanguageService.ApplyLanguage(item.Code, save: true);
            var prevIndex = FeatureTabsListBox.SelectedIndex;
            BuildStepsForCurrentLanguage();
            FeatureTabsListBox.SelectedIndex = Math.Clamp(prevIndex, 0, _steps.Count - 1);
        }
    }

    private void BuildStepsForCurrentLanguage()
    {
        var lang = LanguageService.ReadLanguagePreference();
        if (lang == "system")
        {
            var twoLetter = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            lang = twoLetter switch
            {
                "ko" => "ko",
                "zh" => "zh-CN",
                "es" => "es",
                "pt" => "pt-BR",
                "ms" => "ms",
                _ => "en"
            };
        }

        _steps = lang switch
        {
            "ko" => GetKoreanSteps(),
            "zh-CN" => GetChineseSteps(),
            "es" => GetSpanishSteps(),
            "pt-BR" => GetPortugueseSteps(),
            "ms" => GetMalaySteps(),
            _ => GetEnglishSteps()
        };

        FeatureTabsListBox.ItemsSource = null;
        FeatureTabsListBox.ItemsSource = _steps;

        // Update dialog static strings from centralized multilingual resources (Strings.*.resx)
        AppTitleBar.Title = S.Get("InteractiveGuide_TitleBar");
        PrevBtn.Content = S.Get("InteractiveGuide_Prev");
        NextBtn.Content = S.Get("InteractiveGuide_Next");
        JumpToFeatureBtn.Content = S.Get("InteractiveGuide_Jump");
        KeyHighlightsTitle.Text = S.Get("InteractiveGuide_Highlights");
        HowItWorksHeader.Text = S.Get("InteractiveGuide_HowItWorks");
        ProTipHeader.Text = S.Get("InteractiveGuide_ProTip");
        GuideFooterText.Text = S.Get("InteractiveGuide_Footer");
        if (CloseGuideBtn != null)
        {
            CloseGuideBtn.Content = S.Get("InteractiveGuide_Close");
        }
    }

    private List<GuideFeatureStep> GetEnglishSteps()
    {
        return new List<GuideFeatureStep>
        {
            new()
            {
                Id = "universal",
                Symbol = SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Universal Cloud Saves",
                Subtitle = "Zero-risk Safe Mode save protection",
                Badge = "SAFE MODE",
                Description = "Universal Cloud Saves automatically monitors game save directories directly on your file system, keeping backups, generating version snapshots, and uploading saves directly to Google Drive or local cloud storage.",
                Highlights = new()
                {
                    "100% Anti-Cheat Safe: Runs completely out-of-process without injecting DLLs into game binaries.",
                    "Cloud Synchronization: Seamlessly uploads saves to Google Drive (CloudRedirect/UniversalCloudSaves/) or your designated local cloud drive.",
                    "Official Steam Artworks: Automatically fetches official Steam banners and posters for every detected game.",
                    "Automatic Save Monitoring: Detects when you play and finish a game, syncing your progress with zero manual hassle."
                },
                HowItWorks = "CloudRedirect detects game save paths across AppData, Saved Games, Steam userdata, and Documents. When save files are modified and the game exits, it bundles the delta and uploads it directly to Google Drive using the secure Google Drive API.",
                ProTip = "Click the [📁 Drive] button on any game card to immediately open and view your synced save folder in Google Drive in your browser!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "scan",
                Symbol = SymbolRegular.Search24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Steam Library Auto-Scan",
                Subtitle = "Intelligent game & save detection",
                Badge = "AUTO-DETECT",
                Description = "Discovers all installed Steam titles across multiple library drives and accurately maps their genuine save directories across Windows without cluttering your library with system AppData junk.",
                Highlights = new()
                {
                    "Multi-Library Discovery: Scans all configured Steam library folders across SSDs and HDDs.",
                    "Genuine Game Verification: Validates titles against Steam ACF manifests, remote_unlock.json, and SUO catalogs.",
                    "Smart Directory Filtering: Eliminates irrelevant Windows AppData junk while detecting actual game save locations.",
                    "Automatic Non-Cloud Protection: Automatically enrolls and protects games that Valve does not back up to the cloud."
                },
                HowItWorks = "The scanner queries Steam's libraryfolders.vdf and parses appmanifest_*.acf files to cross-reference game install paths, executable names, and community save schemas.",
                ProTip = "Click [⚡ Auto-Detect Game Saves] on the Universal Saves page anytime you install new games for instantaneous enrollment.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Save Version History & Rollback",
                Subtitle = "Timestamped restore points",
                Badge = "VERSION CONTROL",
                Description = "Never lose game progress again due to corrupted save files, buggy game updates, or accidental overwrites. CloudRedirect keeps immutable, timestamped snapshots of your saves.",
                Highlights = new()
                {
                    "Automatic Snapshots: Creates an isolated snapshot every time a game exits or before synchronization.",
                    "1-Click Rollback: Restore any previous version of your save file with a single click.",
                    "File Explorer Browser: Open and inspect individual snapshot files directly in Windows Explorer.",
                    "Zero Cloud Overwrite Panic: If a cloud sync creates a conflict, your historical snapshots remain intact."
                },
                HowItWorks = "Snapshots are stored with full directory tree fidelity in your local CloudRedirect snapshot repository. Restoring a version safely swaps the active files while preserving a backup of the current state.",
                ProTip = "Click [🕒 History] on any game card in Universal Cloud Saves to view all available snapshots and rollback points.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gdrive",
                Symbol = SymbolRegular.CloudArrowUp24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
                Title = "Cloud Storage & Google Drive",
                Subtitle = "Direct personal cloud sync",
                Badge = "CLOUD SYNC",
                Description = "Connect CloudRedirect directly to your personal Google Drive account with zero intermediate servers, or configure a local NAS/cloud folder for high-speed offline syncing.",
                Highlights = new()
                {
                    "Direct Google Drive OAuth: Uses official Google Drive APIs; your save data travels directly between your PC and Google Drive.",
                    "Zero Middleman Servers: No third-party servers, telemetry, or upload size limits.",
                    "Local & NAS Folder Fallback: Supports external drives, local directories, or mapped network drives.",
                    "Cloud Browser Button: Jump straight to your Google Drive save folder with a single click."
                },
                HowItWorks = "CloudRedirect uses OAuth 2.0 PKCE to securely authorize with Google Drive and store encrypted refresh tokens in your Windows Data Protection DPAPI store.",
                ProTip = "Visit the Cloud Provider page to authenticate your Google Drive account or test connection latency.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Steam Cloud Redirection (Core)",
                Subtitle = "Native Steam API redirection",
                Badge = "CORE ENGINE",
                Description = "For games utilizing the official Steam Cloud API, CloudRedirect seamlessly redirects file read/write operations to your own private cloud storage instead of Valve's servers.",
                Highlights = new()
                {
                    "Custom Storage: Store unlimited save game data on Google Drive or a customized NAS/folder path.",
                    "Full Steam Compatibility: Games continue to utilize standard Steam Cloud functions transparently.",
                    "Stock Icon Integrity: Preserves authentic original Steam Cloud status indicators without intrusive styling modifications."
                },
                HowItWorks = "The native CloudRedirect DLL core intercepts Steam Cloud I/O calls and routes save data directly to your designated cloud provider path.",
                ProTip = "Check out the Cloud Provider page to configure your Google Drive account or set a custom local sync folder.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Steam Library & Game Posters",
                Subtitle = "Seamless game discovery & CDN art",
                Badge = "LIBRARY",
                Description = "Automatically discovers installed Steam games across all your Steam libraries and displays real-time playing status and official high-resolution posters.",
                Highlights = new()
                {
                    "Automatic Library Scanning: Detects multi-drive Steam library installations seamlessly.",
                    "Real-time Playing Indicator: Shows the active game poster and play duration right on your Dashboard.",
                    "Hashed CDN Poster Resolution: Always fetches the correct poster artwork, even for modern Steam games with hashed CDN assets."
                },
                HowItWorks = "Parses Valve VDF manifests to locate installed titles and resolves current store artwork from the Steam Storefront API.",
                ProTip = "Click on any game in the Apps list to view redirection details, open the game install folder, or trigger a cloud sync.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "suo",
                Symbol = SymbolRegular.Globe24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "SUO Remote Dashboard",
                Subtitle = "Integrated embedded web management",
                Badge = "REMOTE DASHBOARD",
                Description = "Access and manage your Steam Unlock ONENNABE (SUO) remote environment directly within CloudRedirect using an embedded WebView2 dashboard without opening external browsers.",
                Highlights = new()
                {
                    "Embedded Webview: Smoothly renders https://onennabe.duckdns.org/dashboard without external browser popups.",
                    "Presence Detection: Automatically detects whether the local SUO daemon is active, standby, or offline.",
                    "One-Click Activation: Run the official setup script (irm onennabe.duckdns.org | iex) directly with a single click.",
                    "Instant Refresh: Reload live game unlock catalogs and remote states effortlessly."
                },
                HowItWorks = "SUO Remote uses Microsoft WebView2 with an isolated user profile in %LOCALAPPDATA%\\CloudRedirect to securely communicate with the remote dashboard.",
                ProTip = "If SUO is active on your machine, a dedicated 'SUO Remote' button appears on your Dashboard and Settings pages for instant navigation.",
                TargetPageType = typeof(SuoRemotePage)
            },
            new()
            {
                Id = "stats",
                Symbol = SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Analytics & Performance Stats",
                Subtitle = "Real-time sync telemetry",
                Badge = "ANALYTICS",
                Description = "Track transfer speeds, uploaded payload volumes, sync history logs, and cache efficiency in real time.",
                Highlights = new()
                {
                    "Transfer Telemetry: Live graphs and read/write bandwidth indicators.",
                    "Sync Audit Logs: Complete history of every cloud sync and file modification event.",
                    "Storage Usage: Keep tabs on how much cloud storage your save game library consumes."
                },
                HowItWorks = "Records lightweight performance metrics during background upload tasks and presents them in authentic Steam-styled telemetry graphs.",
                ProTip = "Review the Stats page after playing to verify successful synchronization timestamps and data size.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Maintenance & Cleanup",
                Subtitle = "Keep your storage tidy & optimized",
                Badge = "UTILITY",
                Description = "Quickly clean up temporary cache files, prune old rollback snapshots, and optimize your local CloudRedirect repository.",
                Highlights = new()
                {
                    "One-Click Optimization: Clean up obsolete sync caches and orphaned temporary downloads.",
                    "Snapshot Pruning: Automatically purge snapshots older than your chosen threshold.",
                    "Safe Verification: Never deletes active game saves or verified cloud backups."
                },
                HowItWorks = "Scans staging directories and compares snapshot timestamps against your retention policy.",
                ProTip = "Run a cleanup once a month to reclaim disk space from old game versions you no longer play.",
                TargetPageType = typeof(CleanupPage)
            },
            new()
            {
                Id = "shortcuts",
                Symbol = SymbolRegular.Keyboard24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Global Hotkey & Instant Zoom",
                Subtitle = "Alt+Shift+C & dynamic UI scaling",
                Badge = "ACCESSIBILITY",
                Description = "Summon CloudRedirect in an instant without minimizing your game, and adapt the interface effortlessly for high-resolution 1440p and 4K displays.",
                Highlights = new()
                {
                    "Global Summon Hotkey: Press Alt+Shift+C from any running game to instantly bring CloudRedirect to the foreground.",
                    "Dynamic Zoom: Adjust scaling from 80% to 150% with Ctrl + Mouse Wheel or toolbar buttons.",
                    "Smart Auto-Fit: One click adjusts the window layout to completely fit without vertical scrolling.",
                    "High-DPI Vector Crispness: Icons and text remain tack-sharp regardless of zoom factor."
                },
                HowItWorks = "Registers a global Windows hook using RegisterHotKey and applies hardware-accelerated WPF layout scale transforms dynamically.",
                ProTip = "Click the zoom percentage text in the top titlebar anytime to toggle between Auto-Fit and default 100% scale.",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "anticheat",
                Symbol = SymbolRegular.Shield24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B)),
                Title = "Anti-Cheat Safety Architecture",
                Subtitle = "EAC, BattlEye & VAC compliance",
                Badge = "SECURITY",
                Description = "Universal Cloud Saves is engineered from the ground up to never trigger anti-cheat software such as Easy Anti-Cheat, BattlEye, Valve Anti-Cheat, or Vanguard.",
                Highlights = new()
                {
                    "100% Zero Injection: Never hooks game memory, modifies executables, or injects DLLs into game threads.",
                    "File-System Isolation: Runs strictly as an external Windows background service monitoring save files.",
                    "Multiplayer Safe: Play competitive titles (e.g. Apex Legends, Elden Ring, Helldivers) with total peace of mind.",
                    "Read/Write Safety: Respects file locks and waits until games flush buffers before creating snapshot backups."
                },
                HowItWorks = "By relying entirely on Windows ReadDirectoryChangesW notifications and process exit events, game binaries remain 100% unmodified.",
                ProTip = "Look for the green 'Anti-Cheat Safe 🛡️' filter badge on Universal Saves to see all games protected under Safe Mode.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gameboost",
                Symbol = SymbolRegular.Flash24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Game Boost Engine",
                Subtitle = "CPU, GPU & Memory Optimization",
                Badge = "PERFORMANCE",
                Description = "Game Boost maximizes hardware performance when a game launches by prioritizing CPU and I/O threads, switching Windows to High Performance power plan, auto-assigning dedicated GPUs for laptops, auto-freezing background applications, and periodically purging standby RAM.",
                Highlights = new()
                {
                    "High CPU & I/O Priority: Elevates game process execution priority so Windows allocates maximum CPU cycles and minimizes input lag.",
                    "Dedicated GPU Auto-Assignment: Automatically configures Windows DirectX preferences to run the game on the discrete high-performance graphics card (vital for gaming laptops).",
                    "Auto-Freeze Background Apps: Deprioritizes resource hogs (Chrome, Edge, Discord, extra launchers) to Idle priority and flushes their RAM on launch, restoring them on game exit.",
                    "Periodic Smart RAM Purge: Automatically purges leaked standby memory every 10 minutes during long gaming sessions whenever system RAM load exceeds 80%.",
                    "Animated Toast Overlay: Displays a sleek, Steam-styled notification overlay showing freed memory and active booster status on launch."
                },
                HowItWorks = "Interacts directly with Windows powrprof.dll to engage high-performance power schemes, Windows Registry (DirectX UserGpuPreferences) to lock the discrete GPU, and EmptyWorkingSet APIs to trim background memory.",
                ProTip = "Customize individual Game Boost toggles (Dedicated GPU, Background Auto-Freeze, Smart RAM Purge) under Settings to fit your setup!",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "gamespace",
                Symbol = SymbolRegular.Games24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Game Space Overlay",
                Subtitle = "Android-style in-game slide-out drawer (Ctrl+Space)",
                Badge = "IN-GAME OVERLAY",
                Description = "Game Space is an authentic Steam-styled, non-intrusive companion overlay inspired by mobile gaming docks. Press Ctrl+Space inside any game to slide out live system hardware telemetry, quick RAM purges, disaster insurance checkpoints, in-game scratchpad notes, and a draggable mini web browser.",
                Highlights = new()
                {
                    "Live Hardware Telemetry: Instant glance at CPU & GPU utilization, temperatures, and RAM consumption formatted with responsive auto-fit badges.",
                    "Disaster Insurance Checkpoints: Create named, timestamped save checkpoints before major boss fights or branching choices with one click.",
                    "Draggable Mini Web Browser: Look up game wikis, maps, and walkthroughs without minimizing your game or triggering full-screen alt-tab stutters.",
                    "Emergency Game Kill: Force-terminate frozen or unresponsive game processes instantly from the overlay.",
                    "Auto-Safe Protection: Automatically disabled for genuine owned games and anti-cheat titles to ensure 100% account safety."
                },
                HowItWorks = "Runs as a hardware-accelerated WPF window positioned flush against the left screen edge. Listens to a global low-level Windows keyboard hook for Ctrl+Space, smoothly animating open and closing.",
                ProTip = "Click 'Preview Game Space' in Settings to test and customize the overlay without needing to start a game first!",
                TargetPageType = typeof(SettingsPage)
            }
        };
    }

    private List<GuideFeatureStep> GetKoreanSteps()
    {
        return new List<GuideFeatureStep>
        {
            new()
            {
                Id = "universal",
                Symbol = SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "유니버설 클라우드 세이브",
                Subtitle = "안티치트 무위험 안전 모드 보호",
                Badge = "안전 모드",
                Description = "유니버설 클라우드 세이브는 게임 프로세스 외부에서 세이브 폴더를 직접 감시하여, 백업 생성, 타임스탬프 스냅샷 기록, Google Drive 및 로컬 저장소로의 자동 업로드를 수행합니다.",
                Highlights = new()
                {
                    "100% 안전 모드: 게임 바이너리에 DLL을 주입하지 않아 안티치트 제재 위험이 전혀 없습니다.",
                    "Google Drive 클라우드 업로드: 세이브 파일을 Google Drive (CloudRedirect/UniversalCloudSaves/) 또는 로컬 클라우드 폴더로 안전하게 자동 업로드합니다.",
                    "공식 Steam 포스터: 설치된 게임의 공식 고화질 포스터를 자동으로 불러옵니다.",
                    "자동 백그라운드 동기화: 게임 플레이 및 종료를 감지하여 세이브를 자동으로 클라우드에 백업합니다."
                },
                HowItWorks = "AppData, Saved Games, Steam userdata, Documents 등 다양한 경로의 세이브 파일을 감시하며, 게임 종료 시 변경된 파일을 패키징하여 Google Drive API를 통해 안전하게 업로드합니다.",
                ProTip = "게임 카드의 [📁 Drive] 버튼을 클릭하면 브라우저에서 동기화된 Google Drive 세이브 폴더가 즉시 열립니다!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "scan",
                Symbol = SymbolRegular.Search24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Steam 라이브러리 자동 스캔",
                Subtitle = "스마트 게임 및 세이브 경로 감지",
                Badge = "자동 감지",
                Description = "여러 드라이브에 설치된 Steam 게임을 자동으로 스캔하고, 쓸모없는 Windows 시스템 폴더를 제외한 실제 게임 세이브 폴더만을 정확하게 찾아냅니다.",
                Highlights = new()
                {
                    "다중 라이브러리 스캔: 모든 SSD 및 HDD 드라이브의 Steam 라이브러리를 검색합니다.",
                    "정품 및 언락 게임 감지: Steam ACF 매니페스트, remote_unlock.json, SUO 카탈로그와 대조 검증합니다.",
                    "스마트 디렉터리 필터링: 불필요한 AppData 정크 파일을 걸러내고 실제 세이브 경로만 식별합니다.",
                    "비클라우드 게임 자동 보호: Valve 클라우드가 지원되지 않는 게임을 자동으로 등록하여 보호합니다."
                },
                HowItWorks = "Steam의 libraryfolders.vdf를 분석하여 설치된 게임 목록을 추출하고 커뮤니티 데이터베이스와 매칭하여 정확한 세이브 경로를 산출합니다.",
                ProTip = "새 게임을 설치한 후 유니버설 세이브 페이지의 [⚡ 세이브 자동 감지]를 클릭하면 즉시 등록됩니다.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "세이브 버전 기록 및 롤백",
                Subtitle = "타임스탬프 복원 지점",
                Badge = "버전 관리",
                Description = "세이브 파일 손상, 게임 버그, 실수로 인한 덮어쓰기 등으로 인한 진행 상황 손실을 완벽히 방지합니다. 타임스탬프가 지정된 변경 불가능한 스냅샷을 보관합니다.",
                Highlights = new()
                {
                    "자동 스냅샷: 게임 종료 시 및 동기화 전마다 독립된 스냅샷을 생성합니다.",
                    "클릭 한 번으로 롤백: 이전 버전의 세이브를 클릭 한 번으로 안전하게 복원합니다.",
                    "파일 탐색기 열기: 스냅샷 파일을 Windows 탐색기에서 직접 확인하고 비교할 수 있습니다.",
                    "클라우드 덮어쓰기 걱정 제로: 동기화 충돌이 발생해도 로컬 기록 스냅샷은 안전하게 보존됩니다."
                },
                HowItWorks = "스냅샷은 로컬 리포지토리에 디렉터리 트리 그대로 안전하게 보관됩니다. 복원 시 현재 상태를 백업한 후 목표 버전으로 교체합니다.",
                ProTip = "유니버설 세이브에서 게임 카드의 [🕒 기록]을 클릭하여 복원 지점 목록을 확인하세요.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gdrive",
                Symbol = SymbolRegular.CloudArrowUp24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
                Title = "클라우드 저장소 및 Google Drive",
                Subtitle = "직접 개인 클라우드 동기화",
                Badge = "클라우드 동기화",
                Description = "제3자 서버를 거치지 않고 본인의 Google Drive 계정과 직접 통신하거나 로컬 NAS/클라우드 폴더를 구성하여 고속으로 동기화합니다.",
                Highlights = new()
                {
                    "공식 Google Drive OAuth: 공식 API를 사용하여 PC와 Google Drive 간에만 데이터가 전송됩니다.",
                    "중계 서버 제로: 외부 서버에 데이터가 저장되거나 텔레메트리가 전송되지 않습니다.",
                    "로컬 및 NAS 폴더 지원: 외장 드라이브, 네트워크 드라이브(NAS)를 자유롭게 설정할 수 있습니다.",
                    "원클릭 드라이브 열기: 동기화된 클라우드 폴더를 웹 브라우저에서 바로 확인할 수 있습니다."
                },
                HowItWorks = "OAuth 2.0 PKCE 인증을 거쳐 발급된 토큰을 Windows DPAPI를 통해 암호화하여 로컬에 안전하게 보관합니다.",
                ProTip = "클라우드 공급자 페이지에서 Google Drive 인증 상태를 확인하고 연결 속도를 테스트하세요.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Steam Cloud 리디렉션 (코어 엔진)",
                Subtitle = "네이티브 Steam API 리디렉션",
                Badge = "코어 엔진",
                Description = "공식 Steam Cloud API를 사용하는 게임의 파일 입출력을 Valve 서버 대신 사용자의 개인 클라우드 저장소로 직접 리디렉션합니다.",
                Highlights = new()
                {
                    "무제한 용량: Google Drive 또는 로컬 저장소에 용량 제한 없이 세이브를 저장합니다.",
                    "완벽한 호환성: 게임은 표준 Steam Cloud 함수를 그대로 투명하게 사용합니다.",
                    "순정 아이콘 유지: UI 개조 없이 Steam 순정 오리지널 클라우드 아이콘을 안전하게 유지합니다."
                },
                HowItWorks = "네이티브 C++ 코어가 Steam Cloud API 호출을 가로채 지정된 개인 저장소 경로로 세이브 데이터를 라우팅합니다.",
                ProTip = "클라우드 공급자 페이지에서 원하는 클라우드 제공자나 로컬 동기화 폴더를 지정할 수 있습니다.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Steam 라이브러리 및 고화질 포스터",
                Subtitle = "원활한 게임 검색 및 CDN 아트워크",
                Badge = "라이브러리",
                Description = "모든 드라이브에 분산 설치된 Steam 게임을 자동으로 집계하고, 현재 플레이 중인 게임과 고해상도 공식 포스터를 실시간으로 표시합니다.",
                Highlights = new()
                {
                    "다중 라이브러리 통합: 여러 드라이브에 걸쳐 설치된 타이틀을 한눈에 관리합니다.",
                    "실시간 플레이 감지: 현재 실행 중인 게임 포스터와 플레이 시간을 대시보드에 즉시 표시합니다.",
                    "해시 CDN 포스터 지원: 최신 Steam CDN 포맷의 고해상도 공식 아트를 빠짐없이 불러옵니다."
                },
                HowItWorks = "Valve VDF 매니페스트를 분석하여 설치된 타이틀을 식별하고 Steam 스토어프론트 API에서 공식 아트를 가져옵니다.",
                ProTip = "앱 목록에서 게임을 클릭하면 리디렉션 상태 확인, 설치 폴더 열기, 수동 동기화를 실행할 수 있습니다.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "suo",
                Symbol = SymbolRegular.Globe24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "SUO 원격 대시보드",
                Subtitle = "통합 임베디드 웹 관리",
                Badge = "원격 대시보드",
                Description = "별도의 웹 브라우저를 띄울 필요 없이 CloudRedirect 내부의 임베디드 WebView2를 통해 Steam Unlock ONENNABE (SUO) 원격 대시보드를 직접 관리합니다.",
                Highlights = new()
                {
                    "내장 웹뷰: 외부 브라우저 창 없이 https://onennabe.duckdns.org/dashboard 를 앱 내에서 바로 탐색합니다.",
                    "상태 실시간 감지: 로컬 SUO 데몬의 실행 여부와 온라인/오프라인 상태를 실시간 확인합니다.",
                    "원클릭 활성화: 설치 스크립트 (irm onennabe.duckdns.org | iex)를 앱 내부에서 바로 실행합니다.",
                    "즉각적인 동기화: 원격 게임 목록과 라이브러리를 손쉽게 갱신할 수 있습니다."
                },
                HowItWorks = "Microsoft WebView2 런타임을 활용하여 앱 내 격리된 프로필에서 대시보드를 안전하게 렌더링합니다.",
                ProTip = "SUO가 설치되어 있거나 활성화되면 대시보드 및 설정 페이지에 바로가기 버튼이 자동으로 나타납니다.",
                TargetPageType = typeof(SuoRemotePage)
            },
            new()
            {
                Id = "stats",
                Symbol = SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "동기화 분석 및 텔레메트리",
                Subtitle = "실시간 동기화 성능 통계",
                Badge = "통계 분석",
                Description = "전송 속도, 업로드된 데이터 용량, 동기화 감사 로그 및 캐시 효율성을 실시간 그래프로 모니터링합니다.",
                Highlights = new()
                {
                    "전송 텔레메트리: 실시간 읽기/쓰기 대역폭 표시 및 그래프.",
                    "동기화 감사 로그: 모든 클라우드 동기화와 파일 수정 기록을 투명하게 열람.",
                    "저장소 사용량 분석: 게임 세이브가 클라우드에서 차지하는 용량을 한눈에 확인."
                },
                HowItWorks = "백그라운드 업로드 작업 중 메트릭을 기록하고 Steam 스타일의 직관적인 그래프로 표현합니다.",
                ProTip = "게임 플레이 후 통계 페이지를 방문하여 성공적인 동기화 시간과 전송 크기를 점검하세요.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "유지보수 및 스토리지 정리",
                Subtitle = "저장 공간 최적화 및 스냅샷 정리",
                Badge = "유지보수",
                Description = "임시 캐시 파일을 정리하고 오래된 롤백 스냅샷을 선별 정리하여 드라이브 용량을 최적화합니다.",
                Highlights = new()
                {
                    "원클릭 최적화: 사용하지 않는 동기화 캐시와 고아 임시 파일을 한 번에 제거.",
                    "스냅샷 보관 기간 설정: 지정한 기간보다 오래된 스냅샷만 안전하게 정리.",
                    "무손실 검증: 현재 활성화된 세이브나 검증된 클라우드 파일은 절대 삭제되지 않습니다."
                },
                HowItWorks = "임시 폴더를 검사하고 스냅샷 생성 시각을 보관 정책과 비교하여 안전하게 정리합니다.",
                ProTip = "한 달에 한 번 정리 작업을 실행하여 더 이상 플레이하지 않는 게임의 세이브 용량을 확보하세요.",
                TargetPageType = typeof(CleanupPage)
            },
            new()
            {
                Id = "shortcuts",
                Symbol = SymbolRegular.Keyboard24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "글로벌 단축키 및 화면 확대/축소",
                Subtitle = "Alt+Shift+C 및 동적 UI 스케일링",
                Badge = "접근성",
                Description = "게임을 최소화하지 않고 즉시 앱을 호출하며, 1440p 및 4K 고해상도 모니터에서도 최적의 크기로 편리하게 사용합니다.",
                Highlights = new()
                {
                    "전역 호출 단축키: 게임 플레이 중에도 Alt+Shift+C를 누르면 즉시 앱이 화면 맨 앞으로 표시됩니다.",
                    "동적 UI 줌: Ctrl + 마우스 휠 또는 툴바 버튼으로 80%~150%까지 자유롭게 확대/축소.",
                    "자동 맞춤 (Auto-Fit): 세로 스크롤 없이 창 크기에 맞게 인터페이스가 자동으로 조절됩니다.",
                    "선명한 고DPI 벡터: 확대 배율에 관계없이 텍스트와 아이콘이 매우 선명하게 렌더링됩니다."
                },
                HowItWorks = "Windows의 RegisterHotKey API로 단축키를 등록하고 WPF의 하드웨어 가속 레이아웃 변환을 통해 크기를 동적으로 조절합니다.",
                ProTip = "상단 타이틀바의 배율 텍스트를 클릭하면 자동 맞춤과 100% 기본 배율 사이를 빠르게 전환할 수 있습니다.",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "anticheat",
                Symbol = SymbolRegular.Shield24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B)),
                Title = "무주입 안티치트 안전 아키텍처",
                Subtitle = "EAC, BattlEye 및 VAC 완벽 준수",
                Badge = "보안",
                Description = "유니버설 클라우드 세이브는 Easy Anti-Cheat, BattlEye, VAC, Vanguard 등의 안티치트 프로그램에 절대 감지되지 않도록 설계되었습니다.",
                Highlights = new()
                {
                    "100% 무주입 방식: 게임 메모리나 실행 파일을 절대 변조하거나 DLL을 주입하지 않습니다.",
                    "파일 시스템 기반 격리: 외부 Windows 백그라운드 서비스로서 세이브 파일 시스템만 모니터링합니다.",
                    "멀티플레이 안전: 에이펙스 레전드, 엘든 링, 헬다이버즈 등 온라인 대전 게임에서도 안심하고 사용 가능.",
                    "파일 잠금 대기: 게임이 세이브 저장을 완전히 마칠 때까지 안전하게 대기한 후 백업합니다."
                },
                HowItWorks = "Windows의 ReadDirectoryChangesW 알림과 프로세스 종료 이벤트만을 활용하여 게임 프로세스와 완전히 격리된 상태로 동작합니다.",
                ProTip = "유니버설 세이브 목록에서 'Anti-Cheat Safe 🛡️' 필터를 선택하면 안전 모드로 보호 중인 게임만 모아볼 수 있습니다.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gameboost",
                Symbol = SymbolRegular.Flash24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "게임 부스트 엔진",
                Subtitle = "CPU, GPU 및 메모리 성능 극대화",
                Badge = "성능 최적화",
                Description = "게임 실행 시 CPU 및 I/O 우선순위를 높이고, Windows 전원 옵션을 고성능으로 전환하며, 노트북 외장 GPU 자동 할당, 백그라운드 앱 자동 동결 및 장시간 게임 시 스마트 RAM 자동 정리를 수행합니다.",
                Highlights = new()
                {
                    "CPU 및 I/O 우선순위 상승: 게임 프로세스에 높은 실행 우선순위를 부여하여 프레임 드랍과 입력 지연(인풋랙)을 최소화합니다.",
                    "외장 고성능 GPU 자동 할당: 듀얼 GPU 노트북에서 게임이 내장 그래픽 대신 외장 고성능 그래픽 카드로 실행되도록 Windows DirectX 설정을 자동 구성합니다.",
                    "백그라운드 앱 자동 동결: 게임 시작 시 Chrome, Edge, Discord, 런처 등을 유휴(Idle) 상태로 낮추고 RAM을 비우며, 게임 종료 시 원래대로 복구합니다.",
                    "주기적 스마트 RAM 자동 정리: 장시간 플레이 시 시스템 RAM 사용량이 80%를 초과하면 대기 메모리를 자동으로 안전하게 정리합니다.",
                    "Steam 스타일 토스트 알림: 부스트 활성화 시 최적화된 메모리 용량과 상태를 세련된 오버레이로 즉시 표시합니다."
                },
                HowItWorks = "Windows powrprof.dll을 통한 전원 관리 전환, DirectX UserGpuPreferences 레지스트리를 통한 외장 GPU 잠금, EmptyWorkingSet API를 활용하여 시스템 자원을 게임에 집중시킵니다.",
                ProTip = "설정 페이지에서 외장 GPU 강제 할당, 백그라운드 앱 자동 동결, 주기적 RAM 정리 옵션을 개별적으로 켜거나 끌 수 있습니다!",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "gamespace",
                Symbol = SymbolRegular.Games24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "게임 스페이스 오버레이",
                Subtitle = "안드로이드 스타일 인게임 슬라이드 서랍 (Ctrl+Space)",
                Badge = "인게임 오버레이",
                Description = "게임 스페이스는 게이밍 스마트폰의 게임 독에서 영감을 얻은 세련된 Steam 스타일 인게임 오버레이입니다. 게임 중 Ctrl+Space를 누르면 게임을 최소화하지 않고도 실시간 시스템 모니터링, 즉각적인 RAM 정리, 수동 세이브 체크포인트, 메모장, 드래그 가능한 미니 웹 브라우저를 바로 불러올 수 있습니다.",
                Highlights = new()
                {
                    "실시간 하드웨어 텔레메트리: CPU, GPU 사용률, 온도 및 RAM 상태를 깔끔한 배지로 실시간 표시합니다.",
                    "재해 방지 세이브 체크포인트: 보스전이나 중요한 선택 직전에 한 번의 클릭으로 이름을 지정한 세이브 백업을 즉시 생성합니다.",
                    "이동 가능한 미니 웹 브라우저: Alt+Tab 전체화면 전환 없이 게임 내에서 공략, 지도, 위키를 바로 검색하고 크기를 조절할 수 있습니다.",
                    "비상 게임 종료: 게임이 멈추거나 튕겼을 때 오버레이에서 즉시 강제 종료할 수 있습니다.",
                    "안티치트 안전 가드: 정품 소유 게임 및 안티치트가 포함된 게임에서는 자동으로 비활성화되어 계정 제재 위험을 100% 차단합니다."
                },
                HowItWorks = "화면 좌측에 최상위 투명 레이어로 대기하다가 Ctrl+Space 단축키를 감지하면 부드러운 애니메이션과 함께 나타납니다.",
                ProTip = "설정에서 'Preview Game Space' 버튼을 누르면 게임을 켜지 않고도 오버레이를 미리 체험하고 설정을 조정할 수 있습니다!",
                TargetPageType = typeof(SettingsPage)
            }
        };
    }

    private List<GuideFeatureStep> GetChineseSteps()
    {
        return new List<GuideFeatureStep>
        {
            new()
            {
                Id = "universal",
                Symbol = SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "通用云存档 (安全模式)",
                Subtitle = "零风险文件级安全保护",
                Badge = "安全模式",
                Description = "通用云存档直接在文件系统级别监控游戏存档目录，自动创建备份、记录时间戳快照，并无缝上传到 Google Drive 或本地云存储。",
                Highlights = new()
                {
                    "100% 反作弊安全：完全在游戏进程外运行，不向游戏注入任何 DLL，无任何封号风险。",
                    "直接云端同步：安全同步至 Google Drive (CloudRedirect/UniversalCloudSaves/) 或自定义本地目录。",
                    "官方高清封面：自动匹配并获取已安装游戏的 Steam 官方高清宣传海报。",
                    "全自动后台备份：智能感知游戏运行与退出，游戏结束即刻自动同步进度。"
                },
                HowItWorks = "监控 AppData、Saved Games、Steam userdata 和 Documents 中的存档变动，在游戏退出后自动打包差异增量，使用安全加密通道直传云端。",
                ProTip = "点击游戏卡片上的 [📁 Drive] 按钮，可直接在浏览器中打开该游戏在 Google Drive 中的云存档文件夹！",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "scan",
                Symbol = SymbolRegular.Search24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Steam 游戏库自动扫描",
                Subtitle = "智能检测安装游戏与存档路径",
                Badge = "自动检测",
                Description = "跨多个硬盘驱动器发现已安装的 Steam 游戏，精准定位真实存档目录，剔除系统无关的 AppData 垃圾目录。",
                Highlights = new()
                {
                    "多盘库自动识别：自动检索所有配置的 Steam 库文件夹（SSD/HDD）。",
                    "正版与解锁游戏验证：对照 Steam ACF 清单、remote_unlock.json 及 SUO 目录精准比对。",
                    "智能目录过滤：彻底过滤无关的系统杂项文件夹，精准锁定真正的游戏存档。",
                    "非云端游戏自动保护：自动将未接入 Valve 官方云的游戏纳入通用存档保护。"
                },
                HowItWorks = "读取 Steam 的 libraryfolders.vdf，解析 appmanifest_*.acf 文件获取游戏安装路径，结合社区存档特征库完成精确匹配。",
                ProTip = "安装新游戏后，在通用云存档页面点击 [⚡ 自动扫描游戏存档] 即可一键完成纳管保护。",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "存档版本历史与一键回滚",
                Subtitle = "时间戳快照与版本恢复",
                Badge = "版本控制",
                Description = "再也不用担心游戏崩溃导致存档损坏、误操作覆盖或恶性更新。CloudRedirect 为每一次游戏备份保留不可篡改的时间戳快照。",
                Highlights = new()
                {
                    "自动快照生成：每次游戏退出或云同步前均自动建立独立快照。",
                    "一键回滚恢复：只需点击一次即可将存档还原到任意历史时刻。",
                    "在文件资源管理器中打开：直接在 Windows 资源管理器中检视快照文件内容。",
                    "云端覆盖无忧：即使云端同步出现意外冲突，本地历史快照依然完好无损。"
                },
                HowItWorks = "快照完整保留目录层级存储在本地仓库中。执行还原时，系统会先备份当前存档状态，再原子替换为目标版本。",
                ProTip = "点击通用存档卡片上的 [🕒 历史]，即可浏览该游戏的所有快照并随时一键回退。",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gdrive",
                Symbol = SymbolRegular.CloudArrowUp24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
                Title = "云存储与 Google Drive 同步",
                Subtitle = "直连个人私有云端",
                Badge = "云端同步",
                Description = "直接与个人的 Google Drive 账号建立端到端连接，无任何中转服务器；同时支持本地 NAS 或外部硬盘进行高速离线备份。",
                Highlights = new()
                {
                    "官方 OAuth 授权：直接调用 Google Drive API，数据仅在电脑与 Google 云端之间流转。",
                    "零中转服务器：无第三方服务器介入，不收集任何用户数据与存档内容。",
                    "支持本地与 NAS 映射：可自定义外接硬盘、局域网共享文件夹（NAS）作为同步端。",
                    "网页端一键直达：支持在浏览器中即时打开云存档目录查看详情。"
                },
                HowItWorks = "基于 OAuth 2.0 PKCE 协议安全授权，刷新令牌通过 Windows DPAPI 硬件加密保存在本机。",
                ProTip = "进入“云存储提供商”页面可随时绑定 Google 账号并测试网络连接延迟。",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Steam 云重定向 (核心引擎)",
                Subtitle = "原生 Steam API I/O 截取",
                Badge = "核心引擎",
                Description = "针对使用官方 Steam Cloud API 的游戏，将原本发送至 Valve 服务器的文件读写请求无缝重定向至您的私有存储空间。",
                Highlights = new()
                {
                    "无限云存储：在 Google Drive 或 NAS 驱动器上享受不受 Valve 配额限制的存档空间。",
                    "原生兼容：游戏透明调用标准 Steam Cloud 函数，运行完全无感知。",
                    "保留原版图标：完全保持 Steam 官方原生云状态图标，无任何样式破坏。"
                },
                HowItWorks = "通过底层 C++ 核心拦截 Steam Cloud I/O 调用，将存档数据直接读写至您指定的私有云路径。",
                ProTip = "在云存储提供商页面中可以自由配置 Google Drive 或本地私有保存路径。",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Steam 游戏库与高清海报",
                Subtitle = "全盘库自动发现与实时状态",
                Badge = "游戏库",
                Description = "自动聚合跨盘安装的 Steam 游戏，并在仪表盘上实时呈现当前游戏运行状态、游玩时长及官方高清封面艺术图。",
                Highlights = new()
                {
                    "多驱动器整合：无论游戏分散在哪个分区，均可一览无余。",
                    "实时运行指示：大厅即刻展示当前正在游玩的游戏海报及已游玩时间。",
                    "哈希 CDN 封面解析：完美解析最新 Steam CDN 哈希资源，海报永不失效。"
                },
                HowItWorks = "读取 VDF 清单解析游戏信息，并通过 Steam Storefront 接口动态获取对应的高分辨率宣传海报。",
                ProTip = "在应用列表中点击任意游戏，可查看重定向详情、打开游戏安装目录或触发单项同步。",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "suo",
                Symbol = SymbolRegular.Globe24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "SUO 远程仪表盘",
                Subtitle = "内嵌网页端远程管理",
                Badge = "远程控制",
                Description = "无需启动外部浏览器，直接在 CloudRedirect 内嵌的 WebView2 界面中查看并管理 Steam Unlock ONENNABE (SUO) 远程仪表盘。",
                Highlights = new()
                {
                    "内嵌 Web 视图：在软件内流畅浏览 https://onennabe.duckdns.org/dashboard ，无多余弹窗干扰。",
                    "服务状态探测：自动检测本地 SUO 守护进程是否处于在线或待机状态。",
                    "一键激活安装：内置激活通道，可直接执行安装命令 (irm onennabe.duckdns.org | iex)。",
                    "即时刷新：方便快速重载解锁目录和远程配置信息。"
                },
                HowItWorks = "采用 Microsoft WebView2 控件配合专属用户数据目录，安全渲染远程仪表盘并维持登录会话。",
                ProTip = "当本机检测到 SUO 服务后，主界面及设置中心将自动亮起“SUO 远程”入口按钮。",
                TargetPageType = typeof(SuoRemotePage)
            },
            new()
            {
                Id = "stats",
                Symbol = SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "同步遥测与实时数据统计",
                Subtitle = "实时带宽监控与传输审计",
                Badge = "数据统计",
                Description = "实时追踪上传/下载传输速率、流量体积、操作审计日志以及云端存档占用空间。",
                Highlights = new()
                {
                    "实时性能遥测：直观展示读写带宽指示器与活动图表。",
                    "完整审计日志：记录每一次云同步与本地文件更新的精确时间戳。",
                    "云存储占用分析：精确掌握每个游戏存档在云端所消耗的存储体积。"
                },
                HowItWorks = "在后台同步任务执行时采集各项性能指标，并以 Steam 质感图表实时反馈给用户。",
                ProTip = "游玩结束后可切换至统计页面，确认最新的云备份完成时间与同步字节数。",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "存储维护与快照清理",
                Subtitle = "清理临时缓存与过期快照",
                Badge = "维护优化",
                Description = "一键清理无用的传输缓存与临时下载碎片，并根据保留策略修剪过期的历史回滚快照。",
                Highlights = new()
                {
                    "一键快速优化：智能扫描并清除过期的暂存缓存与孤立下载碎片。",
                    "快照阈值修剪：按自定义时间规则自动清理过于久远的历史版本。",
                    "数据完整保障：绝不会删除正在生效的活跃存档或已验证的云端副本。"
                },
                HowItWorks = "扫描暂存目录并对比快照生成时间与保留时间窗口，安全回收无用磁盘空间。",
                ProTip = "建议每月运行一次存储维护，为不再常玩的游戏释放本地磁盘空间。",
                TargetPageType = typeof(CleanupPage)
            },
            new()
            {
                Id = "shortcuts",
                Symbol = SymbolRegular.Keyboard24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "全局快捷键与动态缩放",
                Subtitle = "Alt+Shift+C 唤出与高分屏适配",
                Badge = "辅助功能",
                Description = "在游戏中无需切出桌面即可即刻呼出控制面板，并专为 2K、4K 等高分辨率显示器提供自适应界面缩放。",
                Highlights = new()
                {
                    "全局呼出快捷键：在任何全屏游戏中按下 Alt+Shift+C，即可立即置顶呼出主窗口。",
                    "自由动态缩放：支持 Ctrl + 鼠标滚轮在 80% 到 150% 之间平滑无极缩放界面。",
                    "智能窗口自适应：一键 Auto-Fit，无需上下滚动即可完整显示当前页面所有内容。",
                    "高分屏矢量渲染：字体与图标均为矢量渲染，放大后依然极度锐利细腻。"
                },
                HowItWorks = "调用 Windows RegisterHotKey 接口监听全局热键，并通过 WPF 硬件加速矩阵实现平滑缩放。",
                ProTip = "随时点击标题栏右侧的缩放百分比文字，可快速在自适应布局与 100% 原始比例间切换。",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "anticheat",
                Symbol = SymbolRegular.Shield24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B)),
                Title = "零注入反作弊安全架构",
                Subtitle = "兼容 EAC、BattlEye 与 VAC",
                Badge = "安全合规",
                Description = "通用云存档采用彻底的“零进程注入”设计，从根源上杜绝引发 Easy Anti-Cheat、BattlEye、VAC 或 Vanguard 的警告。",
                Highlights = new()
                {
                    "100% 零内存注入：不挂钩游戏进程、不修改可执行文件、不向游戏注入任何代码。",
                    "文件系统纯外置运行：作为外部独立的 Windows 服务运行，只感知文件系统的读写动作。",
                    "联机多人竞技无忧：放心游玩《Apex 英雄》《艾尔登法环》《绝地潜兵 2》等竞技与联机游戏。",
                    "智能文件锁规避：等待游戏完全解除存档读写占用后再打包，杜绝存档截断损坏。"
                },
                HowItWorks = "完全基于 Windows 标准的 ReadDirectoryChangesW 文件变动通知及进程退出事件驱动，与游戏内存彻底隔离。",
                ProTip = "在通用存档界面勾选“Anti-Cheat Safe 🛡️”筛选标签，即可快速查看所有处于安全模式保护下的游戏。",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gameboost",
                Symbol = SymbolRegular.Flash24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "游戏加速引擎 (Game Boost)",
                Subtitle = "CPU、GPU 与内存全方位性能优化",
                Badge = "性能加速",
                Description = "当游戏启动时，自动提升 CPU 与 I/O 优先级，切换 Windows 高性能电源计划，为笔记本自动分配独立独显，自动冻结后台冗余进程，并在长时间游戏时周期性智能清理内存。",
                Highlights = new()
                {
                    "高 CPU 与 I/O 优先级：为游戏进程分配最高的系统调度权重，显著降低掉帧与操作输入延迟。",
                    "自动分配高性能独显：专为双显卡笔记本优化，自动在 Windows DirectX 中绑定高性能独立显卡，避免误用集显导致卡顿。",
                    "启动时自动冻结后台应用：游戏启动时将 Chrome、Edge、Discord、各类启动器降为低空闲优先级并释放内存，退出游戏后自动复原。",
                    "长游戏周期智能 RAM 清理：监测长时间游玩过程，当系统 RAM 占用超过 80% 时自动清理备用待机内存，杜绝微卡顿。",
                    "动态 Steam 风格弹出提示：启动游戏时弹出精致的游戏加速悬浮窗，展示释放的内存与就绪状态。"
                },
                HowItWorks = "通过调用 Windows powrprof.dll 切换极致电源模式，配置注册表 DirectX UserGpuPreferences 绑定独显，并调用 EmptyWorkingSet 释放后台无用工作集。",
                ProTip = "您可以在“设置”页面中分别开启或关闭独显分配、后台自动冻结以及周期性智能内存清理！",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "gamespace",
                Symbol = SymbolRegular.Games24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "游戏空间悬浮窗 (Game Space)",
                Subtitle = "安卓游戏手机风格侧滑栏 (Ctrl+Space)",
                Badge = "游戏内悬浮窗",
                Description = "Game Space 是灵感来源于电竞手机游戏助手的 Steam 质感侧滑栏。在游戏中按下 Ctrl+Space 即可瞬间滑出，无需最小化游戏即可查看硬件状态、一键清理内存、创建命名存档备份、使用游戏便签或开启微型网页浏览器。",
                Highlights = new()
                {
                    "实时硬件状态监控：以原生 Steam 徽章风格实时展示 CPU、GPU 占用率、温度及 RAM 消耗。",
                    "灾难备份存档点：在迎战强力 Boss 或做出关键剧情抉择前，一键生成带时间戳的命名存档快照。",
                    "可拖拽移动的微型浏览器：无需切出游戏（Alt+Tab），直接在悬浮窗内浏览攻略、地图与维基。",
                    "一键强退卡死游戏：游戏失去响应或卡死时，可在侧滑栏中一键结束游戏进程。",
                    "反作弊安全防护：对官方正版和带有反作弊模块的游戏自动禁用，确保 100% 账号安全。"
                },
                HowItWorks = "采用全局底层键盘钩子监听 Ctrl+Space，借助硬件加速的 WPF 置顶透明窗口实现无缝平滑滑出动画。",
                ProTip = "可在设置页面点击“Preview Game Space”预览悬浮窗并调整自适应布局，无需先启动游戏！",
                TargetPageType = typeof(SettingsPage)
            }
        };
    }

    private List<GuideFeatureStep> GetSpanishSteps()
    {
        return new List<GuideFeatureStep>
        {
            new()
            {
                Id = "universal",
                Symbol = SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Guardados en la Nube Universales",
                Subtitle = "Protección en Modo Seguro sin riesgos",
                Badge = "MODO SEGURO",
                Description = "Monitorea automáticamente los directorios de guardado directamente en tu sistema de archivos, manteniendo copias de seguridad, instantáneas versionadas y subiéndolas a Google Drive o almacenamiento local.",
                Highlights = new()
                {
                    "100% Seguro con Anti-Cheat: Funciona fuera del proceso sin inyectar DLLs en los ejecutables del juego.",
                    "Sincronización en la Nube: Sube partidas a Google Drive (CloudRedirect/UniversalCloudSaves/) o a tu almacenamiento local.",
                    "Pósteres Oficiales de Steam: Descarga carátulas oficiales en alta resolución para cada juego detectado.",
                    "Monitoreo Automático: Detecta cuándo juegas y sales del juego, sincronizando sin esfuerzo manual."
                },
                HowItWorks = "Detecta rutas de guardado en AppData, Saved Games, Steam userdata y Documentos. Al salir del juego, empaqueta los cambios y los sube mediante la API de Google Drive.",
                ProTip = "¡Haz clic en el botón [📁 Drive] de cualquier tarjeta de juego para abrir directamente la carpeta de guardado en Google Drive en tu navegador!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "scan",
                Symbol = SymbolRegular.Search24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Escaneo Automático de Biblioteca Steam",
                Subtitle = "Detección inteligente de juegos y guardados",
                Badge = "AUTO-DETECCIÓN",
                Description = "Encuentra todos los juegos de Steam instalados en varias unidades y localiza con precisión sus carpetas de guardado auténticas sin basura de AppData del sistema.",
                Highlights = new()
                {
                    "Descubrimiento Multi-Unidad: Analiza todas las bibliotecas de Steam en SSDs y HDDs.",
                    "Verificación de Juegos Genuinos: Comprueba manifiestos ACF, remote_unlock.json y catálogos SUO.",
                    "Filtrado Inteligente de Carpetas: Elimina carpetas irrelevantes de Windows y conserva solo guardados reales.",
                    "Protección de Juegos sin Nube: Registra y protege automáticamente juegos que Valve no respalda."
                },
                HowItWorks = "Lee libraryfolders.vdf y appmanifest_*.acf de Steam para obtener rutas y nombres, asociándolos con la base de datos de guardados comunitarios.",
                ProTip = "Haz clic en [⚡ Auto-Detectar Guardados] en la página de Guardados Universales para registrar nuevos juegos al instante.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Historial de Versiones y Reversión",
                Subtitle = "Puntos de restauración con marca de tiempo",
                Badge = "CONTROL DE VERSIONES",
                Description = "No vuelvas a perder progreso por partidas corruptas, fallos de juegos o sobrescrituras accidentales. CloudRedirect conserva instantáneas inmutables con fecha y hora.",
                Highlights = new()
                {
                    "Instantáneas Automáticas: Genera una instantánea aislada cada vez que el juego finaliza o antes de sincronizar.",
                    "Reversión en 1 Clic: Restaura cualquier versión previa de tu partida con un solo clic.",
                    "Explorador de Archivos: Abre e inspecciona los archivos de cada punto de restauración en Windows Explorer.",
                    "Sin Miedo a Sobrescrituras: Si ocurre un conflicto en la nube, tus instantáneas locales permanecen intactas."
                },
                HowItWorks = "Las instantáneas se almacenan con la estructura de carpetas original. Al restaurar, se respalda el estado actual antes de aplicar la versión elegida.",
                ProTip = "Haz clic en [🕒 Historial] en cualquier tarjeta de juego para ver los puntos de reversión disponibles.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gdrive",
                Symbol = SymbolRegular.CloudArrowUp24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
                Title = "Almacenamiento en Nube y Google Drive",
                Subtitle = "Sincronización directa a tu nube personal",
                Badge = "SYNC EN LA NUBE",
                Description = "Conecta CloudRedirect directamente a tu cuenta de Google Drive sin servidores intermedios, o configura una carpeta local/NAS para sincronización rápida sin conexión.",
                Highlights = new()
                {
                    "OAuth Directo de Google Drive: Utiliza APIs oficiales de Google; tus datos van directo de tu PC a Google Drive.",
                    "Cero Servidores Intermediarios: Sin recolección de datos, servidores de terceros ni límites artificiales.",
                    "Soporte Local y NAS: Admite discos duros externos y carpetas compartidas de red.",
                    "Acceso Web Rápido: Abre la carpeta en la nube en tu navegador con un solo clic."
                },
                HowItWorks = "Utiliza el protocolo seguro OAuth 2.0 PKCE y almacena las credenciales cifradas con Windows DPAPI en tu equipo.",
                ProTip = "Visita la página de Proveedor de Nube para conectar tu cuenta de Google Drive o probar la velocidad de conexión.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Redirección de Steam Cloud (Núcleo)",
                Subtitle = "Redirección nativa de la API de Steam",
                Badge = "MOTOR NATIVO",
                Description = "Para títulos que utilizan la API oficial de Steam Cloud, CloudRedirect redirige las lecturas y escrituras hacia tu almacenamiento privado en lugar de los servidores de Valve.",
                Highlights = new()
                {
                    "Almacenamiento Ilimitado: Guarda partidas sin restricciones de cuota en Google Drive o tu almacenamiento local.",
                    "Compatibilidad Total con Steam: Los juegos usan las funciones de Steam Cloud de forma completamente transparente.",
                    "Iconos Originales Intactos: Mantiene los iconos oficiales de Steam Cloud en la biblioteca sin alteraciones cosméticas."
                },
                HowItWorks = "El núcleo DLL intercepta las llamadas de E/S de Steam Cloud y canaliza los datos hacia tu proveedor de almacenamiento configurado.",
                ProTip = "Configura tu cuenta de Google Drive o tu carpeta de sincronización local en la página de Proveedor de Nube.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Biblioteca de Steam y Pósteres Oficiales",
                Subtitle = "Descubrimiento de juegos y arte CDN",
                Badge = "BIBLIOTECA",
                Description = "Descubre automáticamente los juegos de Steam instalados en todas tus unidades y muestra en tiempo real el juego activo con carátulas oficiales en alta resolución.",
                Highlights = new()
                {
                    "Integración Multi-Unidad: Administra tus juegos repartidos en distintos discos en un solo panel.",
                    "Indicador en Tiempo Real: Muestra el juego en ejecución y la duración de la sesión en el Dashboard.",
                    "Resolución CDN con Hash: Descarga siempre el arte oficial correcto, incluso para los títulos más modernos."
                },
                HowItWorks = "Analiza manifiestos VDF de Steam para identificar juegos y obtiene el arte oficial desde la API de la tienda Steam.",
                ProTip = "Haz clic en cualquier juego de la lista para ver detalles de redirección, abrir la carpeta de instalación o forzar una sincronización.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "suo",
                Symbol = SymbolRegular.Globe24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Panel Remoto de SUO",
                Subtitle = "Gestión web integrada sin navegador externo",
                Badge = "PANEL REMOTO",
                Description = "Administra tu entorno remoto de Steam Unlock ONENNABE (SUO) directamente dentro de CloudRedirect mediante una vista WebView2 integrada.",
                Highlights = new()
                {
                    "Vista Web Embebida: Carga https://onennabe.duckdns.org/dashboard con fluidez sin abrir navegadores externos.",
                    "Detección de Estado: Informa en tiempo real si el demonio SUO local está activo, en espera o desconectado.",
                    "Activación en 1 Clic: Ejecuta el script oficial de instalación (irm onennabe.duckdns.org | iex) directamente.",
                    "Recarga Rápida: Actualiza instantáneamente el catálogo de juegos y las configuraciones remotas."
                },
                HowItWorks = "Utiliza el motor Microsoft WebView2 con un perfil aislado en %LOCALAPPDATA%\\CloudRedirect para renderizar con total seguridad.",
                ProTip = "Cuando SUO esté activo en tu equipo, aparecerá automáticamente un botón 'SUO Remote' en el Dashboard y en Configuración.",
                TargetPageType = typeof(SuoRemotePage)
            },
            new()
            {
                Id = "stats",
                Symbol = SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Telemetría y Estadísticas de Sincronización",
                Subtitle = "Métricas de rendimiento en tiempo real",
                Badge = "ESTADÍSTICAS",
                Description = "Monitorea la velocidad de transferencia, volumen de datos subidos, registros de auditoría y espacio consumido en la nube en tiempo real.",
                Highlights = new()
                {
                    "Telemetría en Vivo: Gráficos dinámicos e indicadores de ancho de banda de lectura/escritura.",
                    "Registros de Auditoría: Historial detallado con fecha y hora de cada sincronización realizada.",
                    "Uso de Almacenamiento: Conoce con exactitud el espacio que ocupa cada juego en tu nube."
                },
                HowItWorks = "Registra métricas ligeras durante las transferencias en segundo plano y las plasma en gráficos con diseño Steam.",
                ProTip = "Revisa la página de Estadísticas tras jugar para confirmar la sincronización exitosa y el tamaño de datos transferido.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Mantenimiento y Limpieza",
                Subtitle = "Optimiza y libera espacio de almacenamiento",
                Badge = "UTILIDAD",
                Description = "Limpia rápidamente archivos de caché temporales, purga instantáneas antiguas y optimiza tu repositorio local de CloudRedirect.",
                Highlights = new()
                {
                    "Optimización con un Clic: Elimina cachés obsoletas y fragmentos temporales huérfanos.",
                    "Purga Inteligente de Instantáneas: Elimina automáticamente versiones que superen tu tiempo de retención.",
                    "Seguridad Garantizada: Nunca borra guardados activos ni copias de seguridad verificadas en la nube."
                },
                HowItWorks = "Inspecciona carpetas temporales y compara marcas de tiempo de las instantáneas con tu política de retención.",
                ProTip = "Ejecuta una limpieza mensualmente para recuperar espacio en disco de juegos a los que ya no juegas.",
                TargetPageType = typeof(CleanupPage)
            },
            new()
            {
                Id = "shortcuts",
                Symbol = SymbolRegular.Keyboard24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Atajo Global y Zoom Instantáneo",
                Subtitle = "Alt+Shift+C y escalado dinámico de interfaz",
                Badge = "ACCESIBILIDAD",
                Description = "Abre CloudRedirect al instante sin minimizar tu juego y ajusta la interfaz perfectamente para monitores de alta resolución 1440p y 4K.",
                Highlights = new()
                {
                    "Atajo Global para Invocar: Presiona Alt+Shift+C en cualquier juego para traer CloudRedirect al primer plano.",
                    "Zoom Dinámico: Escala la interfaz entre 80% y 150% con Ctrl + Rueda del ratón o con la barra superior.",
                    "Auto-Ajuste Inteligente: Un clic ajusta la ventana para que todo encaje sin barra de desplazamiento vertical.",
                    "Nitidez Vectorial de Alta Densidad: Iconos y tipografía perfectamente nítidos en cualquier nivel de aumento."
                },
                HowItWorks = "Registra un atajo en Windows mediante RegisterHotKey y aplica transformaciones de diseño WPF aceleradas por hardware.",
                ProTip = "Haz clic en el porcentaje de zoom de la barra superior para alternar entre Auto-Ajuste y la escala predeterminada al 100%.",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "anticheat",
                Symbol = SymbolRegular.Shield24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B)),
                Title = "Seguridad Anti-Cheat Sin Inyección",
                Subtitle = "100% compatible con EAC, BattlEye y VAC",
                Badge = "SEGURIDAD",
                Description = "Los Guardados Universales han sido diseñados desde cero para no interferir jamás con sistemas anti-trampas como Easy Anti-Cheat, BattlEye, VAC o Vanguard.",
                Highlights = new()
                {
                    "Cero Inyección: No toca la memoria del juego, no altera ejecutables ni inyecta DLLs en los hilos del proceso.",
                    "Aislamiento a Nivel de Archivo: Funciona estrictamente como un servicio externo de Windows monitoreando archivos.",
                    "Apto para Multijugador: Juega a Apex Legends, Elden Ring o Helldivers con total tranquilidad.",
                    "Respeto de Bloqueos de Archivo: Espera a que el juego libere el archivo antes de generar la copia de seguridad."
                },
                HowItWorks = "Utiliza las notificaciones de ReadDirectoryChangesW de Windows y eventos de finalización de procesos sin tocar la memoria del juego.",
                ProTip = "Selecciona el filtro 'Anti-Cheat Safe 🛡️' en Guardados Universales para ver todos los juegos bajo protección en Modo Seguro.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gameboost",
                Symbol = SymbolRegular.Flash24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Motor Game Boost",
                Subtitle = "Optimización de CPU, GPU y memoria",
                Badge = "RENDIMIENTO",
                Description = "Game Boost maximiza el rendimiento al iniciar un juego: eleva la prioridad de CPU e I/O, activa el plan de energía de alto rendimiento de Windows, asigna la GPU dedicada en portátiles, congela apps en segundo plano y purga memoria RAM periódicamente.",
                Highlights = new()
                {
                    "Prioridad alta de CPU e I/O: Otorga al juego máxima prioridad en el programador de Windows, reduciendo el retardo de entrada.",
                    "Asignación automática de GPU dedicada: Configura las preferencias de DirectX en Windows para forzar la gráfica dedicada (esencial en portátiles).",
                    "Congelación automática en segundo plano: Reduce navegadores (Chrome, Edge), Discord y lanzadores a prioridad Idle y vacía su RAM al jugar, restaurándolos al salir.",
                    "Autopurga inteligente periódica de RAM: En sesiones largas de juego, si el uso de RAM supera el 80%, limpia la memoria en espera de forma segura.",
                    "Notificación animada estilo Steam: Muestra un aviso elegante al arrancar el juego indicando los megabytes de RAM optimizados."
                },
                HowItWorks = "Utiliza powrprof.dll para planes de energía, el registro DirectX UserGpuPreferences para forzar la GPU dedicada y EmptyWorkingSet para liberar memoria.",
                ProTip = "¡Puedes activar o desactivar cada ajuste de Game Boost (GPU dedicada, congelación de apps, purga de RAM) en la página de Ajustes!",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "gamespace",
                Symbol = SymbolRegular.Games24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Superposición Game Space",
                Subtitle = "Cajón deslizante estilo Android en el juego (Ctrl+Espacio)",
                Badge = "SUPERPOSICIÓN",
                Description = "Game Space es una barra lateral complementaria no intrusiva inspirada en los móviles gaming con la estética de Steam. Pulsa Ctrl+Espacio en cualquier juego para desplegar telemetría en tiempo real, liberar RAM al instante, crear puntos de guardado nombrados, usar un bloc de notas y abrir un mininavegador web arrastrable.",
                Highlights = new()
                {
                    "Telemetría de hardware en vivo: Visualiza uso de CPU, GPU, temperaturas y consumo de RAM con indicadores adaptables.",
                    "Puntos de guardado de emergencia: Crea copias de seguridad de tus partidas antes de un combate difícil con un solo clic.",
                    "Mininavegador web redimensionable y arrastrable: Consulta guías, mapas y wikis sin minimizar el juego ni sufrir tirones de pantalla completa.",
                    "Cierre forzado de emergencia: Cierra juegos congelados o bloqueados de inmediato desde el panel lateral.",
                    "Protección antitrampas automática: Se desactiva automáticamente en juegos legítimos o con sistemas antitrampas para máxima seguridad."
                },
                HowItWorks = "Se ejecuta como una ventana flotante acelerada por GPU fijada al borde izquierdo, detectando la combinación global Ctrl+Espacio.",
                ProTip = "¡Haz clic en 'Preview Game Space' en Ajustes para probar y personalizar el panel antes de entrar al juego!",
                TargetPageType = typeof(SettingsPage)
            }
        };
    }

    private List<GuideFeatureStep> GetPortugueseSteps()
    {
        return new List<GuideFeatureStep>
        {
            new()
            {
                Id = "universal",
                Symbol = SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Salvamentos Universais na Nuvem",
                Subtitle = "Proteção em Modo Seguro com risco zero",
                Badge = "MODO SEGURO",
                Description = "Monitora pastas de salvamento diretamente no sistema de arquivos, criando cópias de segurança, instantâneos versionados e enviando tudo para o Google Drive ou armazenamento local.",
                Highlights = new()
                {
                    "100% Seguro contra Anti-Cheat: Funciona fora do processo sem injetar DLLs nos executáveis dos jogos.",
                    "Sincronização na Nuvem: Salva no Google Drive (CloudRedirect/UniversalCloudSaves/) ou em disco local.",
                    "Pôsteres Oficiais da Steam: Baixa artes e banners oficiais em alta resolução para cada jogo detectado.",
                    "Monitoramento Automático: Detecta quando você joga e quando fecha o jogo, sincronizando sem esforço manual."
                },
                HowItWorks = "Detecta caminhos em AppData, Saved Games, Steam userdata e Documentos. Ao fechar o jogo, envia as alterações diretamente para o Google Drive via API segura.",
                ProTip = "Clique no botão [📁 Drive] em qualquer cartão de jogo para abrir diretamente a pasta de salvamentos no Google Drive no seu navegador!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "scan",
                Symbol = SymbolRegular.Search24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Escaneamento Automático da Biblioteca Steam",
                Subtitle = "Detecção inteligente de jogos e saves",
                Badge = "AUTO-DETECÇÃO",
                Description = "Localiza todos os jogos instalados em múltiplos discos e mapeia com precisão suas pastas reais de salvamento sem poluir com dados irrelevantes do sistema.",
                Highlights = new()
                {
                    "Detecção em Vários Discos: Varre todas as bibliotecas da Steam configuradas em SSDs e HDDs.",
                    "Validação de Jogos: Confirma títulos através de manifestos ACF da Steam, remote_unlock.json e SUO.",
                    "Filtro Inteligente: Remove pastas inúteis do AppData do Windows e foca somente nos arquivos de save reais.",
                    "Proteção de Jogos sem Nuvem: Adiciona e protege automaticamente jogos que não possuem nuvem na Steam."
                },
                HowItWorks = "Lê libraryfolders.vdf e appmanifest_*.acf para obter caminhos e executáveis, cruzando dados com a base comunitária de jogos.",
                ProTip = "Clique em [⚡ Auto-Detectar Saves] na página de Salvamentos Universais para registrar novos jogos instantaneamente.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Histórico de Versões e Restauração",
                Subtitle = "Pontos de restauração com data e hora",
                Badge = "CONTROLE DE VERSÃO",
                Description = "Nunca mais perca progresso por arquivos corrompidos, bugs do jogo ou sobrescritas acidentais. O CloudRedirect guarda instantâneos imutáveis com data e hora.",
                Highlights = new()
                {
                    "Instantâneos Automáticos: Cria um ponto de restauração isolado a cada fechamento de jogo ou antes de sincronizar.",
                    "Restauração em 1 Clique: Reverta para qualquer versão anterior com apenas um clique.",
                    "Abrir no Explorador: Abra e inspecione os arquivos de cada versão diretamente no Windows Explorer.",
                    "Sem Pânico de Sobrescrita: Se houver conflito na nuvem, suas versões locais históricas permanecem protegidas."
                },
                HowItWorks = "Os instantâneos são mantidos com a árvore de diretórios intacta. Na restauração, o estado atual é arquivado antes de aplicar a versão desejada.",
                ProTip = "Clique em [🕒 Histórico] em qualquer cartão de jogo para visualizar todos os pontos de restauração disponíveis.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gdrive",
                Symbol = SymbolRegular.CloudArrowUp24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
                Title = "Armazenamento em Nuvem e Google Drive",
                Subtitle = "Sincronização direta na sua nuvem pessoal",
                Badge = "SYNC NA NUVEM",
                Description = "Conecte o CloudRedirect diretamente à sua conta pessoal do Google Drive sem servidores intermediários, ou utilize pastas locais/NAS para alta velocidade offline.",
                Highlights = new()
                {
                    "OAuth Oficial do Google Drive: Dados transferidos diretamente entre o seu PC e os servidores do Google.",
                    "Zero Servidores Intermediários: Sem interceptação de terceiros, telemetria invasiva ou limites arbitrários.",
                    "Suporte a Pastas Locais e NAS: Configure discos externos ou pastas compartilhadas em rede com facilidade.",
                    "Acesso Imediato pelo Navegador: Abra a pasta de salvamentos na nuvem com um único clique."
                },
                HowItWorks = "Utiliza o protocolo OAuth 2.0 PKCE e armazena os tokens de atualização protegidos pelo Windows DPAPI no computador.",
                ProTip = "Visite a página do Provedor de Nuvem para autenticar sua conta Google ou testar a latência de conexão.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Redirecionamento Steam Cloud (Núcleo)",
                Subtitle = "Redirecionamento nativo da API Steam",
                Badge = "MOTOR NATIVO",
                Description = "Para jogos que usam a API oficial da Steam Cloud, o CloudRedirect redireciona as operações de leitura e gravação diretamente para o seu armazenamento privado.",
                Highlights = new()
                {
                    "Armazenamento sem Limites: Guarde seus dados no Google Drive ou NAS sem se preocupar com cotas da Valve.",
                    "Total Compatibilidade: Os jogos continuam executando as funções padrão da Steam Cloud de forma transparente.",
                    "Ícones Originais Mantidos: Preserva o visual autêntico do cliente Steam sem injeções visuais indesejadas."
                },
                HowItWorks = "O núcleo nativo C++ intercepta chamadas de E/S da Steam Cloud e roteia os dados para a sua nuvem pessoal.",
                ProTip = "Defina seu provedor de nuvem ou pasta local preferida na página do Provedor de Nuvem.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Biblioteca Steam e Pôsteres Oficiais",
                Subtitle = "Descoberta de jogos e artes em alta resolução",
                Badge = "BIBLIOTECA",
                Description = "Agrupa automaticamente todos os jogos instalados nos seus discos e exibe em tempo real o jogo em execução com pôsteres oficiais em alta resolução.",
                Highlights = new()
                {
                    "Visão Consolidada: Gerencie títulos espalhados por diferentes unidades em uma única interface.",
                    "Jogo Ativo em Tempo Real: Mostra o pôster e tempo de jogo da sessão ativa no Dashboard.",
                    "Artes CDN com Hash: Baixa pôsteres oficiais sempre atualizados, mesmo para jogos recentes."
                },
                HowItWorks = "Analisa arquivos VDF da Steam para identificar jogos e consome a API da loja Steam para buscar artes oficiais.",
                ProTip = "Clique em qualquer jogo na lista para abrir a pasta de instalação, verificar status ou iniciar uma sincronização.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "suo",
                Symbol = SymbolRegular.Globe24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Painel Remoto SUO",
                Subtitle = "Gerenciamento web integrado sem navegadores externos",
                Badge = "PAINEL REMOTO",
                Description = "Acesse o painel do Steam Unlock ONENNABE (SUO) diretamente no CloudRedirect através de uma janela WebView2 embutida, sem abrir navegadores externos.",
                Highlights = new()
                {
                    "WebView Embutido: Navegue em https://onennabe.duckdns.org/dashboard confortavelmente dentro do aplicativo.",
                    "Detecção de Status: Detecta se o serviço SUO está ativo, em espera ou desconectado na máquina.",
                    "Ativação Rápida: Execute o comando de instalação (irm onennabe.duckdns.org | iex) com apenas um clique.",
                    "Atualização Fácil: Recarregue o catálogo e as opções remotas instantaneamente."
                },
                HowItWorks = "Utiliza o Microsoft WebView2 com perfil isolado em %LOCALAPPDATA%\\CloudRedirect para renderizar com total segurança.",
                ProTip = "Quando o SUO for detectado, um botão 'SUO Remote' aparecerá automaticamente no Dashboard e em Configurações.",
                TargetPageType = typeof(SuoRemotePage)
            },
            new()
            {
                Id = "stats",
                Symbol = SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Telemetria e Estatísticas de Sincronização",
                Subtitle = "Métricas de desempenho em tempo real",
                Badge = "ESTATÍSTICAS",
                Description = "Acompanhe velocidade de envio, volume de dados transferidos, histórico de sincronizações e uso de espaço na nuvem em tempo real.",
                Highlights = new()
                {
                    "Telemetria ao Vivo: Gráficos de largura de banda e atividade de rede em tempo real.",
                    "Auditoria de Sincronização: Histórico detalhado de alterações e envios de arquivos.",
                    "Uso de Armazenamento: Saiba exatamente quanto espaço cada jogo está ocupando na nuvem."
                },
                HowItWorks = "Registra métricas durante as tarefas em segundo plano e apresenta gráficos limpos no estilo Steam.",
                ProTip = "Verifique a página de Estatísticas após jogar para conferir o horário e tamanho do último backup.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Manutenção e Limpeza",
                Subtitle = "Otimize e libere espaço de armazenamento",
                Badge = "UTILITÁRIO",
                Description = "Limpe arquivos temporários de cache, remova versões antigas de saves e mantenha o repositório local do CloudRedirect otimizado.",
                Highlights = new()
                {
                    "Otimização em 1 Clique: Apaga dados temporários obsoletos e fragmentos de download órfãos.",
                    "Limpeza por Tempo: Remove automaticamente instantâneos que excederam o período configurado.",
                    "Segurança Total: Nunca exclui saves ativos nem cópias de segurança verificadas na nuvem."
                },
                HowItWorks = "Analisa diretórios temporários e compara a data dos instantâneos com a política de retenção.",
                ProTip = "Execute a limpeza uma vez por mês para recuperar espaço em disco de jogos concluídos.",
                TargetPageType = typeof(CleanupPage)
            },
            new()
            {
                Id = "shortcuts",
                Symbol = SymbolRegular.Keyboard24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Atalho Global e Zoom Dinâmico",
                Subtitle = "Alt+Shift+C e redimensionamento suave da UI",
                Badge = "ACESSIBILIDADE",
                Description = "Abra o CloudRedirect a qualquer momento sem minimizar seus jogos e adapte a interface para monitores 1440p e 4K com extrema nitidez.",
                Highlights = new()
                {
                    "Atalho de Chamada Global: Pressione Alt+Shift+C durante qualquer jogo para trazer o aplicativo para a frente.",
                    "Zoom Dinâmico: Ajuste a escala de 80% a 150% usando Ctrl + Roda do mouse ou os botões de ajuste.",
                    "Ajuste Automático (Auto-Fit): Um clique ajusta a janela para caber perfeitamente sem barras de rolagem verticais.",
                    "Nitidez Vetorial em Alta Resolução: Textos e ícones permanecem perfeitamente nítidos em qualquer escala."
                },
                HowItWorks = "Registra um atalho global no Windows via RegisterHotKey e aplica matrizes de transformação de layout aceleradas por GPU.",
                ProTip = "Clique no texto de porcentagem de zoom na barra superior para alternar rapidamente entre Auto-Fit e o tamanho padrão de 100%.",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "anticheat",
                Symbol = SymbolRegular.Shield24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B)),
                Title = "Segurança Anti-Cheat Sem Injeção",
                Subtitle = "100% compatível com EAC, BattlEye e VAC",
                Badge = "SEGURANÇA",
                Description = "Os Salvamentos Universais foram arquitetados para nunca disparar alarmes em sistemas como Easy Anti-Cheat, BattlEye, VAC ou Vanguard.",
                Highlights = new()
                {
                    "Zero Injeção de Código: Não mexe na memória do jogo, não altera arquivos binários e não injeta DLLs.",
                    "Isolamento no Sistema de Arquivos: Opera como um serviço externo do Windows apenas monitorando alterações em pastas.",
                    "Seguro para Jogos Online: Jogue Apex Legends, Elden Ring, Helldivers e outros títulos competitivos com tranquilidade.",
                    "Tratamento de Arquivos em Uso: Aguarda o jogo finalizar a gravação antes de criar o pacote de backup."
                },
                HowItWorks = "Baseia-se unicamente nas notificações ReadDirectoryChangesW do Windows e no evento de encerramento do processo do jogo.",
                ProTip = "Selecione o filtro 'Anti-Cheat Safe 🛡️' na página de Salvamentos Universais para listar apenas os jogos protegidos no Modo Seguro.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gameboost",
                Symbol = SymbolRegular.Flash24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Motor Game Boost",
                Subtitle = "Otimização de CPU, GPU e memória",
                Badge = "DESEMPENHO",
                Description = "O Game Boost maximiza o desempenho ao iniciar um jogo: eleva a prioridade de CPU e E/S, ativa o plano de energia de alto desempenho do Windows, atribui a GPU dedicada em notebooks, congela aplicativos em segundo plano e realiza purga periódica de RAM.",
                Highlights = new()
                {
                    "Prioridade alta de CPU e E/S: Garante ciclos máximos de processamento para o jogo, reduzindo a latência de comandos.",
                    "Atribuição de GPU dedicada: Configura as preferências do DirectX no Windows para rodar o jogo na placa de vídeo dedicada (ideal para notebooks com GPU dupla).",
                    "Congelamento automático de apps: Reduz navegadores (Chrome, Edge), Discord e inicializadores para prioridade Idle e libera a RAM enquanto você joga, restaurando ao sair.",
                    "Purga inteligente periódica de RAM: Durante sessões longas, limpa automaticamente a memória em espera se o consumo total ultrapassar 80%.",
                    "Notificação animada estilo Steam: Exibe um aviso moderno ao iniciar o jogo mostrando a quantidade de memória liberada."
                },
                HowItWorks = "Interage com a biblioteca powrprof.dll para alternar planos de energia, grava no registro DirectX UserGpuPreferences para fixar a GPU dedicada e usa a API EmptyWorkingSet para limpar a RAM.",
                ProTip = "Personalize as opções do Game Boost (GPU dedicada, congelamento automático, purga de RAM) na tela de Configurações!",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "gamespace",
                Symbol = SymbolRegular.Games24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Sobreposição Game Space",
                Subtitle = "Gaveta deslizante no jogo estilo Android (Ctrl+Espaço)",
                Badge = "HUD NO JOGO",
                Description = "O Game Space é uma sobreposição não invasiva no estilo Steam inspirada nos menus de jogos de smartphones. Pressione Ctrl+Espaço durante qualquer jogo para acessar telemetria em tempo real, liberar RAM com um toque, criar pontos de restauração salvos, fazer anotações e navegar na web com uma mini janela arrastável.",
                Highlights = new()
                {
                    "Telemetria de hardware ao vivo: Monitore uso de CPU, GPU, temperaturas e consumo de RAM com visualização adaptável.",
                    "Pontos de salvamento personalizados: Crie pontos de restauração com nome e horário antes de momentos decisivos no jogo.",
                    "Mini navegador web arrastável: Consulte tutoriais, mapas e detonados sem minimizar o jogo nem causar travamentos de tela cheia.",
                    "Encerramento forçado de emergência: Feche jogos travados ou sem resposta diretamente pela sobreposição.",
                    "Proteção antitrapaça automática: É desativado automaticamente em jogos originais ou com anti-cheat para garantir total segurança da sua conta."
                },
                HowItWorks = "Funciona como uma janela acelerada por hardware fixada na borda esquerda que responde ao atalho global Ctrl+Espaço com animação suave.",
                ProTip = "Clique em 'Preview Game Space' nas Configurações para testar e ajustar o painel antes de começar a jogar!",
                TargetPageType = typeof(SettingsPage)
            }
        };
    }

    private List<GuideFeatureStep> GetMalaySteps()
    {
        return new List<GuideFeatureStep>
        {
            new()
            {
                Id = "universal",
                Symbol = SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Simpanan Awan Universal (Mod Selamat)",
                Subtitle = "Perlindungan simpanan selamat tanpa risiko",
                Badge = "MOD SELAMAT",
                Description = "Simpanan Awan Universal memantau direktori fail simpanan permainan terus pada sistem fail anda, mencipta sandaran, merakam tangkapan versi, dan memuat naik terus ke Google Drive atau storan tempatan.",
                Highlights = new()
                {
                    "100% Selamat Anti-Cheat: Beroperasi di luar proses permainan tanpa menyuntik sebarang DLL ke dalam fail permainan.",
                    "Penyegerakan Awan: Memuat naik fail simpanan ke Google Drive (CloudRedirect/UniversalCloudSaves/) atau pemacu tempatan.",
                    "Poster Rasmi Steam: Memuat turun poster dan sepanduk rasmi Steam berkualiti tinggi secara automatik.",
                    "Pemantauan Auto: Mengesan masa anda bermain dan keluar dari permainan, menyegerakkan kemajuan secara lancar."
                },
                HowItWorks = "Mengesan laluan simpanan dalam AppData, Saved Games, Steam userdata dan Documents. Apabila permainan ditutup, ia mengemas kini fail dan memuat naik ke Google Drive melalui API selamat.",
                ProTip = "Klik butang [📁 Drive] pada kad permainan untuk membuka folder simpanan di Google Drive terus pada pelayar anda!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "scan",
                Symbol = SymbolRegular.Search24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Imbasan Auto Pustaka Steam",
                Subtitle = "Pengesanan pintar permainan dan fail simpanan",
                Badge = "AUTO-KESAN",
                Description = "Mengesan semua permainan Steam yang dipasang merentasi pelbagai pemacu dan mencari folder simpanan sebenar tanpa mengumpulkan fail sampah sistem AppData.",
                Highlights = new()
                {
                    "Imbasan Berbilang Pemacu: Menyemak semua pustaka Steam pada SSD dan HDD yang dipasang.",
                    "Pengesahan Permainan Sebenar: Memadankan fail appmanifest ACF, remote_unlock.json dan katalog SUO.",
                    "Penapisan Pintar: Membuang folder sistem yang tidak berkaitan dan hanya mengekalkan direktori simpanan sebenar.",
                    "Perlindungan Permainan Tanpa Awan: Memasukkan permainan yang tiada sokongan awan Valve ke dalam perlindungan secara automatik."
                },
                HowItWorks = "Membaca fail libraryfolders.vdf dan appmanifest_*.acf Steam untuk mengenal pasti laluan pemasangan dan memadankannya dengan pangkalan data simpanan komuniti.",
                ProTip = "Klik [⚡ Imbas Auto Simpanan] pada halaman Simpanan Universal bila-bila masa selepas memasang permainan baharu.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Sejarah Versi Simpanan & Kembalikan",
                Subtitle = "Titik pemulihan dengan cap masa",
                Badge = "KAWALAN VERSI",
                Description = "Jangan risau tentang fail simpanan rosak, pepijat permainan, atau fail tertindih tanpa sengaja. CloudRedirect menyimpan tangkapan versi selamat dengan cap masa.",
                Highlights = new()
                {
                    "Tangkapan Automatik: Mencipta titik pemulihan terpencil setiap kali keluar dari permainan atau sebelum penyegerakan.",
                    "Kembalikan 1 Klik: Pulihkan mana-mana versi simpanan terdahulu dengan hanya satu klik.",
                    "Penyemak Fail Windows: Buka dan semak fail tangkapan terus melalui Windows Explorer.",
                    "Tiada Kerisauan Penindihan: Jika berlaku konflik di awan, rekod versi tempatan anda sentiasa terpelihara."
                },
                HowItWorks = "Tangkapan versi disimpan mengikut struktur folder asal dalam repositori tempatan. Pemulihan akan menyandarkan keadaan semasa sebelum menggantikannya.",
                ProTip = "Klik butang [🕒 Sejarah] pada kad permainan untuk melihat semua titik pemulihan yang tersedia.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gdrive",
                Symbol = SymbolRegular.CloudArrowUp24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)),
                Title = "Storan Awan & Google Drive",
                Subtitle = "Penyegerakan terus ke awan peribadi anda",
                Badge = "SEGERAK AWAN",
                Description = "Sambungkan CloudRedirect terus ke akaun Google Drive peribadi anda tanpa pelayan perantara, atau tetapkan folder tempatan/NAS untuk penyegerakan luar talian yang pantas.",
                Highlights = new()
                {
                    "OAuth Rasmi Google Drive: Menggunakan API rasmi Google; fail dihantar terus antara PC anda dan Google Drive.",
                    "Tiada Pelayan Pihak Ketiga: Tiada pemintasan data peribadi, telemetri luaran, atau had saiz muat naik.",
                    "Sokongan Folder Tempatan & NAS: Menyokong pemacu luaran atau folder rangkaian kongsi.",
                    "Akses Pantas Pelayar: Buka folder awan dalam pelayar web dengan sekali klik."
                },
                HowItWorks = "Menggunakan protokol selamat OAuth 2.0 PKCE dan menyimpan token penyegar yang disulitkan dengan Windows DPAPI.",
                ProTip = "Lawati halaman Pembekal Awan untuk mengesahkan akaun Google Drive atau menguji kelajuan sambungan.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Pelepasan Steam Cloud (Enjin Teras)",
                Subtitle = "Pelepasan natif API Steam",
                Badge = "ENJIN TERAS",
                Description = "Bagi permainan yang menggunakan API rasmi Steam Cloud, CloudRedirect menghalakan bacaan dan tulisan fail terus ke storan peribadi anda dan bukan ke pelayan Valve.",
                Highlights = new()
                {
                    "Storan Tanpa Had: Simpan data permainan tanpa batasan kuota di Google Drive atau pemacu tempatan.",
                    "Keserasian Penuh: Permainan terus menggunakan fungsi biasa Steam Cloud secara telus.",
                    "Kekalkan Ikon Asal: Mengekalkan ikon awan asal Steam pada pustaka tanpa sebarang modifikasi visual yang mengganggu."
                },
                HowItWorks = "Teras DLL memintas panggilan I/O Steam Cloud dan menyalurkan data simpanan terus ke storan awan pilihan anda.",
                ProTip = "Buka halaman Pembekal Awan untuk menetapkan Google Drive atau folder tempatan.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Pustaka Steam & Poster Rasmi",
                Subtitle = "Penemuan permainan menyeluruh & seni CDN",
                Badge = "PUSTAKA",
                Description = "Mengumpulkan permainan Steam yang dipasang merentasi semua pemacu dan memaparkan status masa nyata serta poster rasmi resolusi tinggi.",
                Highlights = new()
                {
                    "Pengurusan Berpusat: Mengurus permainan yang dipasang pada pelbagai pemacu dalam satu tempat.",
                    "Status Main Masa Nyata: Memaparkan poster permainan dan tempoh masa bermain pada Papan Pemuka.",
                    "Resolusi Seni CDN Hash: Sentiasa memaparkan poster rasmi yang tepat walaupun untuk tajuk terkini."
                },
                HowItWorks = "Membaca fail VDF Steam untuk menyenaraikan tajuk dan mengambil karya seni rasmi melalui API Steam Storefront.",
                ProTip = "Klik pada mana-mana permainan dalam senarai untuk melihat butiran pelepasan atau membuka folder pemasangan.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "suo",
                Symbol = SymbolRegular.Globe24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Papan Pemuka Jauh SUO",
                Subtitle = "Pengurusan web terbina tanpa pelayar luaran",
                Badge = "PAPAN PEMUKA",
                Description = "Urus persekitaran jauh Steam Unlock ONENNABE (SUO) anda terus di dalam CloudRedirect menggunakan paparan web WebView2 terbina tanpa membuka pelayar luaran.",
                Highlights = new()
                {
                    "Paparan Web Terbina: Akses https://onennabe.duckdns.org/dashboard dengan lancar tanpa gangguan tetingkap luar.",
                    "Pengesanan Status: Mengesan sama ada daemon SUO tempatan sedang aktif, bersedia, atau luar talian.",
                    "Pengaktifan 1 Klik: Jalankan skrip pemasangan rasmi (irm onennabe.duckdns.org | iex) terus dari aplikasi.",
                    "Muat Semula Pantas: Segarkan katalog permainan dan konfigurasi jarak jauh secara serta-merta."
                },
                HowItWorks = "Menggunakan enjin Microsoft WebView2 dengan profil terasing dalam %LOCALAPPDATA%\\CloudRedirect untuk memaparkan papan pemuka secara selamat.",
                ProTip = "Apabila SUO dikesan pada komputer anda, butang 'SUO Remote' akan dipaparkan secara automatik pada Papan Pemuka dan Tetapan.",
                TargetPageType = typeof(SuoRemotePage)
            },
            new()
            {
                Id = "stats",
                Symbol = SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Analisis & Telemetri Segerak",
                Subtitle = "Statistik prestasi penyegerakan masa nyata",
                Badge = "ANALISIS",
                Description = "Pantau kelajuan pemindahan, jumlah saiz data yang dimuat naik, log audit penyegerakan dan penggunaan storan secara masa nyata.",
                Highlights = new()
                {
                    "Telemetri Langsung: Graf jalur lebar baca/tulis masa nyata yang responsif.",
                    "Log Audit Lengkap: Rekod terperinci bagi setiap peristiwa penyegerakan dan pengubahsuaian fail.",
                    "Penggunaan Storan: Ketahui jumlah ruang storan awan yang digunakan oleh setiap permainan."
                },
                HowItWorks = "Merekod metrik prestasi semasa tugasan muat naik latar belakang dan memaparkannya dalam graf bertema Steam.",
                ProTip = "Semak halaman Statistik selepas selesai bermain untuk mengesahkan masa dan saiz sandaran awan anda.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Penyelenggaraan & Pembersihan",
                Subtitle = "Optimumkan dan kemas ruang storan anda",
                Badge = "UTILITI",
                Description = "Kemas fail cache sementara, padamkan tangkapan versi lama yang tidak diperlukan, dan optimumkan repositori tempatan CloudRedirect anda.",
                Highlights = new()
                {
                    "Pengoptimuman 1 Klik: Padam fail cache lama dan serpihan muat turun yang terbiar.",
                    "Pembersihan Mengikut Tempoh: Padamkan tangkapan versi yang melebihi tempoh simpanan secara automatik.",
                    "Jaminan Keselamatan: Tidak akan memadamkan fail simpanan aktif atau sandaran awan yang disahkan."
                },
                HowItWorks = "Mengimbas direktori sementara dan membandingkan tarikh tangkapan versi dengan polisi pengekalan yang ditetapkan.",
                ProTip = "Lakukan pembersihan sebulan sekali untuk menjimatkan ruang pemacu daripada permainan yang tidak lagi dimainkan.",
                TargetPageType = typeof(CleanupPage)
            },
            new()
            {
                Id = "shortcuts",
                Symbol = SymbolRegular.Keyboard24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Pintas Global & Penskalaan Zoom",
                Subtitle = "Alt+Shift+C & pelarasan saiz UI dinamik",
                Badge = "KEBOLEHAKSESAN",
                Description = "Buka CloudRedirect dengan serta-merta tanpa perlu mengecilkan permainan anda, serta laraskan saiz antara muka untuk monitor 1440p dan 4K.",
                Highlights = new()
                {
                    "Pintas Panggilan Global: Tekan Alt+Shift+C semasa bermain permainan untuk membawa CloudRedirect ke hadapan.",
                    "Skala Zoom Dinamik: Laraskan saiz dari 80% hingga 150% menggunakan Ctrl + Roda tetikus atau butang bar alat.",
                    "Muat Automatik (Auto-Fit): Satu klik melaraskan tetingkap supaya muat sepenuhnya tanpa bar tatal menegak.",
                    "Kejelasan Vektor DPI Tinggi: Ikon dan teks kekal tajam tanpa kabur pada sebarang tahap pembesaran."
                },
                HowItWorks = "Mendaftar pintasan global melalui Windows RegisterHotKey dan menggunakan transformasi susun atur WPF dipercepatkan GPU.",
                ProTip = "Klik teks peratusan zoom pada bar tajuk untuk bertukar pantas antara mod Auto-Fit dan saiz asal 100%.",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "anticheat",
                Symbol = SymbolRegular.Shield24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xA3, 0x2B)),
                Title = "Keselamatan Anti-Cheat Tanpa Suntikan",
                Subtitle = "100% mematuhi EAC, BattlEye dan VAC",
                Badge = "KESELAMATAN",
                Description = "Simpanan Awan Universal dibina khusus untuk tidak mencetuskan sebarang amaran daripada perisian anti-cheat seperti Easy Anti-Cheat, BattlEye, VAC atau Vanguard.",
                Highlights = new()
                {
                    "100% Bebas Suntikan: Tidak mengubah memori permainan, tidak mengubah fail binari, dan tidak menyuntik DLL.",
                    "Pengasingan Sistem Fail: Berfungsi sepenuhnya sebagai servis luaran Windows yang hanya memantau fail simpanan.",
                    "Selamat untuk Permainan Berbilang Pemain: Nikmati permainan seperti Apex Legends, Elden Ring dan Helldivers dengan tenang.",
                    "Pengendalian Fail Selamat: Menunggu permainan selesai menulis fail sepenuhnya sebelum mengambil salinan sandaran."
                },
                HowItWorks = "Bergantung sepenuhnya pada pemberitahuan ReadDirectoryChangesW Windows dan peristiwa penamatan proses permainan.",
                ProTip = "Pilih penapis 'Anti-Cheat Safe 🛡️' pada halaman Simpanan Universal untuk melihat semua permainan yang dilindungi di bawah Mod Selamat.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "gameboost",
                Symbol = SymbolRegular.Flash24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                Title = "Enjin Game Boost",
                Subtitle = "Pengoptimuman CPU, GPU & Memori",
                Badge = "PRESTASI",
                Description = "Game Boost memaksimumkan prestasi perkakasan semasa pelancaran permainan: meningkatkan keutamaan CPU dan I/O, menukar pelan kuasa Windows ke Prestasi Tinggi, menetapkan GPU khusus untuk komputer riba, membekukan aplikasi latar belakang secara automatik dan membersihkan RAM secara berkala.",
                Highlights = new()
                {
                    "Keutamaan Tinggi CPU & I/O: Memberikan keutamaan pemprosesan maksimum kepada permainan bagi mengurangkan kelengahan input.",
                    "Penetapan GPU Khusus Automatik: Mengkonfigurasi keutamaan Windows DirectX untuk memastikan permainan berjalan pada kad grafik khusus berprestasi tinggi (penting untuk komputer riba).",
                    "Pembekuan Automatik Aplikasi Latar Belakang: Menurunkan keutamaan pelayar (Chrome, Edge), Discord dan pelancar lain ke Idle serta mengosongkan RAM semasa bermain, memulihkannya semula selepas keluar.",
                    "Pembersihan RAM Pintar Berkala: Memantau penggunaan memori semasa sesi permainan yang panjang; membersihkan memori siap sedia secara automatik jika beban RAM melebihi 80%.",
                    "Pemberitahuan Animasi Gaya Steam: Memaparkan pemberitahuan elegan semasa permainan dilancarkan yang menunjukkan jumlah RAM yang dioptimumkan."
                },
                HowItWorks = "Menggunakan powrprof.dll untuk mod kuasa tinggi, pendaftaran Windows DirectX UserGpuPreferences untuk mengunci GPU khusus, dan API EmptyWorkingSet untuk mengosongkan memori latar belakang.",
                ProTip = "Anda boleh menyesuaikan pilihan Game Boost (GPU Khusus, Pembekuan Aplikasi, Pembersihan RAM) dalam halaman Tetapan!",
                TargetPageType = typeof(SettingsPage)
            },
            new()
            {
                Id = "gamespace",
                Symbol = SymbolRegular.Games24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Hamparan Game Space",
                Subtitle = "Laci luncur dalam permainan gaya Android (Ctrl+Space)",
                Badge = "HAMPARAN PERMAINAN",
                Description = "Game Space ialah hamparan teman bergaya Steam yang diilhamkan daripada dermaga telefon pintar permainan. Tekan Ctrl+Space dalam mana-mana permainan untuk meluncurkan telemetri perkakasan secara langsung, pembersihan RAM segera, penciptaan titik semak simpanan tersuai, pad nota dan pelayar web mini yang boleh dialihkan.",
                Highlights = new()
                {
                    "Telemetri Perkakasan Langsung: Lihat penggunaan CPU & GPU, suhu, dan penggunaan RAM dengan lencana paparan pintar.",
                    "Titik Semak Simpanan Bencana: Cipta titik semak simpanan bernama dan bertarikh dengan satu klik sebelum pertempuran bos utama.",
                    "Pelayar Web Mini Boleh Dialih: Cari panduan permainan, peta dan wiki tanpa perlu meminimumkan permainan atau mengalami gangguan skrin penuh.",
                    "Penamatan Kecemasan Permainan: Tamatkan proses permainan yang beku atau tidak bertindak balas serta-merta dari hamparan.",
                    "Perlindungan Keselamatan Anti-Cheat: Dinyahaktifkan secara automatik pada permainan tulen dan permainan dengan sistem anti-cheat bagi menjamin keselamatan akaun 100%."
                },
                HowItWorks = "Beroperasi sebagai tetingkap WPF yang dipasang di tepi kiri skrin dan mendengar pintasan papan kekunci global Ctrl+Space untuk meluncur masuk dengan lancar.",
                ProTip = "Klik 'Preview Game Space' dalam Tetapan untuk menguji dan menyesuaikan hamparan sebelum anda mula bermain!",
                TargetPageType = typeof(SettingsPage)
            }
        };
    }

    private void FeatureTabsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FeatureTabsListBox.SelectedItem is not GuideFeatureStep step) return;

        FeatureBadgeText.Text = step.Badge;
        FeatureHeaderTitle.Text = step.Title;
        FeatureDescriptionText.Text = step.Description;
        HowItWorksBody.Text = step.HowItWorks;
        ProTipBody.Text = step.ProTip;

        HighlightsPanel.Children.Clear();
        foreach (var h in step.Highlights)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new SymbolIcon
            {
                Symbol = SymbolRegular.CheckmarkCircle24,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Margin = new Thickness(0, 2, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var tb = new TextBlock
            {
                Text = h,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17
            };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            HighlightsPanel.Children.Add(grid);
        }

        int idx = FeatureTabsListBox.SelectedIndex;
        PrevBtn.IsEnabled = idx > 0;
        NextBtn.IsEnabled = idx < _steps.Count - 1;
        JumpToFeatureBtn.Visibility = step.TargetPageType != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PrevBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FeatureTabsListBox.SelectedIndex > 0)
        {
            FeatureTabsListBox.SelectedIndex--;
        }
    }

    private void NextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (FeatureTabsListBox.SelectedIndex < _steps.Count - 1)
        {
            FeatureTabsListBox.SelectedIndex++;
        }
    }

    private void JumpToFeature_Click(object sender, RoutedEventArgs e)
    {
        if (FeatureTabsListBox.SelectedItem is GuideFeatureStep step && step.TargetPageType != null)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateTo(step.TargetPageType);
            }
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
