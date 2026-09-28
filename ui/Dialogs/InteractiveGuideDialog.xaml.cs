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
        public Brush IconColor { get; set; } = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4));
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Universal Cloud Saves",
                Subtitle = "Zero-risk Safe Mode protection",
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
                Id = "history",
                Symbol = Wpf.Ui.Controls.SymbolRegular.History24,
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
                Id = "redirection",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Steam Cloud Redirection (Core)",
                Subtitle = "Native Steam API redirection",
                Badge = "CORE ENGINE",
                Description = "For games utilizing the official Steam Cloud API, CloudRedirect seamlessly redirects file read/write operations to your own private cloud storage instead of Valve's servers.",
                Highlights = new()
                {
                    "Custom Storage: Store unlimited save game data on Google Drive or a customized NAS/folder path.",
                    "Full Steam Compatibility: Games continue to utilize standard Steam Cloud functions transparently.",
                    "No Third-Party Server: Your data travels directly between your PC and Google Drive with zero intermediary telemetry."
                },
                HowItWorks = "The native CloudRedirect DLL core intercepts Steam Cloud I/O calls and routes save data directly to your designated cloud provider path.",
                ProTip = "Check out the Cloud Provider page to configure your Google Drive account or set a custom local sync folder.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Steam Library & Game Posters",
                Subtitle = "Seamless game discovery",
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
                Id = "stats",
                Symbol = Wpf.Ui.Controls.SymbolRegular.DataBarHorizontal24,
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Maintenance & Cleanup",
                Subtitle = "Keep your storage tidy",
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "유니버설 클라우드 세이브",
                Subtitle = "안티치트 무위험 안전 모드 보호",
                Badge = "안전 모드",
                Description = "유니버설 클라우드 세이브는 게임 프로세스 외부에서 세이브 폴더를 직접 감시하여, 백업 생성, 타임스탬프 스냅샷 기록, Google Drive 및 로컬 저장소로의 자동 업로드를 수행합니다.",
                Highlights = new()
                {
                    "100% 안전한 안전 모드: 게임 바이너리에 DLL을 주입하지 않아 안티치트 제재 위험이 전혀 없습니다.",
                    "Google Drive 클라우드 업로드: 세이브 파일을 Google Drive (CloudRedirect/UniversalCloudSaves/) 또는 로컬 클라우드 폴더로 안전하게 자동 업로드합니다.",
                    "공식 Steam 포스터: 설치된 게임의 공식 고화질 포스터를 자동으로 불러옵니다.",
                    "자동 백그라운드 동기화: 게임 플레이 및 종료를 감지하여 세이브를 자동으로 클라우드에 백업합니다."
                },
                HowItWorks = "AppData, Saved Games, Documents 등 게임의 세이브 경로를 자동으로 파악하고, 게임 종료 시 변경 사항을 Google Drive API를 통해 안전하게 클라우드로 전송합니다.",
                ProTip = "게임 카드에서 [📁 드라이브] 버튼을 클릭하면 브라우저에서 Google Drive에 저장된 세이브 폴더가 즉시 열립니다!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = Wpf.Ui.Controls.SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "세이브 버전 기록 및 롤백",
                Subtitle = "타임스탬프 기반 복원 지점",
                Badge = "버전 관리",
                Description = "세이브 파일 손상, 버그성 게임 패치, 실수로 인한 덮어쓰기 걱정이 없습니다. CloudRedirect는 각 플레이 시점의 불변 스냅샷을 보관합니다.",
                Highlights = new()
                {
                    "자동 스냅샷: 게임 종료 시 또는 클라우드 동기화 직전에 독립적인 스냅샷이 생성됩니다.",
                    "원클릭 롤백: 이전 버전의 세이브를 단 한 번의 클릭으로 완벽 복원할 수 있습니다.",
                    "스냅샷 파일 탐색: Windows 파일 탐색기에서 스냅샷 파일을 직접 열어 확인할 수 있습니다."
                },
                HowItWorks = "스냅샷은 로컬 디렉터리에 타임스탬프별로 온전히 보관되며, 복원 시 현재 파일을 백업한 후 이전 상태로 안전하게 교체합니다.",
                ProTip = "유니버설 클라우드 세이브 페이지에서 [🕒 히스토리]를 클릭하여 복원 지점을 확인하세요.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Steam 클라우드 리디렉션 (코어)",
                Subtitle = "네이티브 Steam API 리디렉션",
                Badge = "코어 엔진",
                Description = "Steam Cloud API를 사용하는 게임의 세이브 데이터를 Valve 서버 대신 개인 Google Drive나 지정 폴더로 투명하게 리디렉션합니다.",
                Highlights = new()
                {
                    "맞춤형 클라우드: Google Drive 또는 로컬 NAS에 무제한 세이브 데이터를 저장합니다.",
                    "완벽한 호환성: 게임은 기본 Steam Cloud 기능을 그대로 사용합니다.",
                    "중간 서버 없음: 사용자의 PC와 Google Drive 간에만 직접 데이터가 전송됩니다."
                },
                HowItWorks = "CloudRedirect 코어 엔진이 Steam Cloud I/O 호출을 가로채어 개인 클라우드 저장소로 라우팅합니다.",
                ProTip = "클라우드 제공자 페이지에서 Google Drive 연동 또는 로컬 동기화 경로를 설정하세요.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Steam 라이브러리 및 게임 포스터",
                Subtitle = "자동 게임 감지",
                Badge = "라이브러리",
                Description = "모든 Steam 라이브러리 드라이브에서 설치된 게임을 자동으로 찾고, 실시간 플레이 상태와 고해상도 포스터를 표시합니다.",
                Highlights = new()
                {
                    "멀티 드라이브 검색: 여러 드라이브에 분산된 Steam 라이브러리를 자동 감지합니다.",
                    "실시간 플레이 감지: 현재 플레이 중인 게임 포스터와 플레이 시간을 대시보드에 즉시 표시합니다."
                },
                HowItWorks = "Valve VDF 매니페스트를 분석하여 설치된 타이틀을 식별하고 최신 포스터 CDN URL을 확인합니다.",
                ProTip = "게임 목록에서 게임을 클릭하면 상세 리디렉션 정보와 설치 폴더를 열 수 있습니다.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "stats",
                Symbol = Wpf.Ui.Controls.SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "통계 및 성능 분석",
                Subtitle = "실시간 전송 텔레메트리",
                Badge = "통계",
                Description = "전송 속도, 업로드 데이터 용량, 동기화 이벤트 기록을 실시간으로 확인하세요.",
                Highlights = new()
                {
                    "실시간 속도 그래프: 읽기/쓰기 대역폭을 시각적으로 모니터링합니다.",
                    "동기화 감사 로그: 모든 클라우드 동기화 내역을 완벽하게 기록합니다."
                },
                HowItWorks = "백그라운드 업로드 작업 중 메트릭을 기록하여 전용 그래프로 표시합니다.",
                ProTip = "게임 종료 후 통계 페이지에서 동기화가 정상 완료되었는지 확인할 수 있습니다.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "유지보수 및 정리",
                Subtitle = "저장 공간 최적화",
                Badge = "유틸리티",
                Description = "임시 캐시 파일을 정리하고 오래된 스냅샷을 정리하여 디스크 공간을 확보합니다.",
                Highlights = new()
                {
                    "원클릭 최적화: 불필요한 임시 파일과 캐시를 즉시 청소합니다.",
                    "안전한 정리: 활성 세이브 파일과 클라우드 백업은 안전하게 보존됩니다."
                },
                HowItWorks = "스테이징 디렉터리를 스캔하여 보존 기준을 초과한 오래된 스냅샷을 안전하게 정리합니다.",
                ProTip = "한 달에 한 번 정리를 실행하면 디스크 공간을 효율적으로 관리할 수 있습니다.",
                TargetPageType = typeof(CleanupPage)
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "通用云存档 (安全模式)",
                Subtitle = "零封禁风险的安全模式保护",
                Badge = "安全模式",
                Description = "通用云存档在游戏进程外部直接监控存档目录，提供实时自动备份、历史版本快照并自动上传至 Google 云端硬盘或本地云盘。",
                Highlights = new()
                {
                    "100% 反作弊安全: 完全在进程外运行，不注入任何 DLL，无任何封号风险。",
                    "云端同步: 自动将存档上传到 Google 云端硬盘 (CloudRedirect/UniversalCloudSaves/) 或本地同步盘。",
                    "官方 Steam 游戏封面: 自动获取官方正版高分辨率封面海报。",
                    "自动监控: 游戏结束时自动捕获最新存档并完成云端备份。"
                },
                HowItWorks = "通过文件系统实时监控各游戏的存档位置，在检测到文件变动及游戏退出后，通过 Google Drive API 自动将存档上传至云端。",
                ProTip = "点击游戏卡片上的 [📁 云端硬盘] 按钮，可在浏览器中直接打开云端存档目录！",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = Wpf.Ui.Controls.SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "存档版本历史与一键回滚",
                Subtitle = "时间戳快照恢复点",
                Badge = "版本控制",
                Description = "防止存档损坏、坏档更新或误操作覆盖。CloudRedirect 为每次游戏保存不可篡改的带时间戳历史快照。",
                Highlights = new()
                {
                    "自动快照: 每次游戏结束或云端同步前均会自动创建快照。",
                    "一键回滚: 任意选择以往的存档版本，一键即可安全恢复。",
                    "文件浏览: 可在 Windows 资源管理器中直接查看各快照文件。"
                },
                HowItWorks = "快照完整保存在本地存储目录中，执行回滚操作时会自动备份当前状态，确保数据万无一失。",
                ProTip = "点击游戏卡片上的 [🕒 历史版本] 即可浏览所有可恢复的存档点。",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Steam 云存档重定向 (核心引擎)",
                Subtitle = "原生 Steam API 重定向",
                Badge = "核心引擎",
                Description = "对于使用官方 Steam Cloud API 的游戏，将云读写操作自动重定向到您自己的 Google 云端硬盘或本地同步目录。",
                Highlights = new()
                {
                    "无限容量: 将存档保存在自己的 Google 云端硬盘或 NAS 中。",
                    "完全兼容: 游戏无感调用 Steam Cloud 标准接口。",
                    "私密安全: 数据直接在您的 PC 与云端存储之间传输，无第三方中转。"
                },
                HowItWorks = "原生 DLL 核心透明拦截 Steam Cloud I/O 调用，并将数据重定向到您指定的云存储路径。",
                ProTip = "请在“云存储提供商”页面完成 Google Drive 授权或配置本地同步文件夹。",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Steam 游戏库与海报",
                Subtitle = "自动扫描与识别",
                Badge = "游戏库",
                Description = "自动检测多磁盘 Steam 库中安装的游戏，并在仪表盘上实时呈现当前游玩状态与高清官方海报。",
                Highlights = new()
                {
                    "多磁盘扫描: 自动识别所有分区中的 Steam 库。",
                    "游玩状态实时展示: 仪表盘直接显示当前正在运行的游戏封面与游玩时长。"
                },
                HowItWorks = "解析 Steam VDF 清单并结合 Steam Store API 解析最新的 CDN 封面海报。",
                ProTip = "点击应用列表中的游戏，可打开其安装目录或手动触发同步。",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "stats",
                Symbol = Wpf.Ui.Controls.SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "统计与性能指标",
                Subtitle = "实时遥测数据",
                Badge = "统计",
                Description = "实时查看上传下载速度、累计同步数据量与历史同步事件日志。",
                Highlights = new()
                {
                    "实时速度图表: 监控当前读写与网络带宽。",
                    "完整同步审计: 记录每次存档同步的详细时间与状态。"
                },
                HowItWorks = "在后台同步任务执行期间采集性能数据并绘制专业图表。",
                ProTip = "游戏结束后可在统计页面核实最新同步完成记录与上传体积。",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "维护与垃圾清理",
                Subtitle = "释放磁盘空间",
                Badge = "实用工具",
                Description = "一键清理过期临时缓存文件，修剪陈旧快照，保持系统清爽。",
                Highlights = new()
                {
                    "一键清理: 安全清除无用缓存与过期临时包。",
                    "数据安全: 绝不会删除有效的游戏存档与云端备份。"
                },
                HowItWorks = "扫描暂存目录并依据保留策略自动清理超期的历史快照。",
                ProTip = "每月运行一次清理，可有效避免磁盘空间被旧版本快照占用。",
                TargetPageType = typeof(CleanupPage)
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Guardado Universal en la Nube",
                Subtitle = "Protección en Modo Seguro sin riesgo",
                Badge = "MODO SEGURO",
                Description = "Universal Cloud Saves supervisa las carpetas de guardado directamente en el sistema de archivos, manteniendo copias de seguridad, instantáneas con fecha y hora y subiendo partidas a Google Drive.",
                Highlights = new()
                {
                    "100% Seguro: Funciona fuera del proceso sin inyectar DLL en los binarios del juego.",
                    "Sincronización en la Nube: Sube partidas a Google Drive o a su carpeta local de nube.",
                    "Pósteres Oficiales de Steam: Obtiene carátulas oficiales de alta resolución para cada juego.",
                    "Monitoreo Automático: Detecta cuando juega y guarda sus avances automáticamente."
                },
                HowItWorks = "Detecta las rutas de guardado y, tras cerrar el juego, sube los cambios a Google Drive mediante la API oficial.",
                ProTip = "¡Haga clic en [📁 Drive] en cualquier tarjeta para abrir directamente su carpeta de guardado en la nube en su navegador!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = Wpf.Ui.Controls.SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Historial de Versiones y Restauración",
                Subtitle = "Puntos de restauración con fecha",
                Badge = "CONTROL DE VERSIONES",
                Description = "No vuelva a perder partidas guardadas por archivos dañados o actualizaciones defectuosas. CloudRedirect mantiene copias históricas inmutables.",
                Highlights = new()
                {
                    "Instantáneas Automáticas: Guarda una copia al salir del juego o antes de sincronizar.",
                    "Restauración en 1 Clic: Vuelva a cualquier versión anterior al instante.",
                    "Explorador de Archivos: Abra y revise los archivos de instantáneas directamente."
                },
                HowItWorks = "Guarda las copias de seguridad en su almacenamiento local y permite restaurarlas con máxima seguridad.",
                ProTip = "Haga clic en [🕒 Historial] en cualquier tarjeta de juego para ver los puntos de restauración disponibles.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Redirección Steam Cloud (Núcleo)",
                Subtitle = "Redirección nativa de la API de Steam",
                Badge = "MOTOR PRINCIPAL",
                Description = "Redirige las llamadas de guardado de Steam Cloud a su propio Google Drive o carpeta local en lugar de los servidores de Valve.",
                Highlights = new()
                {
                    "Almacenamiento Personalizado: Guarde partidas ilimitadas en su propio Google Drive.",
                    "Compatibilidad Total: Los juegos continúan usando Steam Cloud con total normalidad.",
                    "Sin Servidores Intermedios: Conexión directa entre su PC y Google Drive."
                },
                HowItWorks = "El núcleo DLL intercepta las llamadas I/O de Steam Cloud y las redirige a su almacenamiento personal.",
                ProTip = "Configure su cuenta de Google Drive en la pestaña Proveedor de la Nube.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Biblioteca de Steam y Pósteres",
                Subtitle = "Detección automática",
                Badge = "BIBLIOTECA",
                Description = "Detecta juegos instalados en todas sus bibliotecas de Steam y muestra carátulas oficiales y estado en tiempo real.",
                Highlights = new()
                {
                    "Detección Multidisco: Encuentra juegos instalados en cualquier unidad de disco.",
                    "Estado en Vivo: Muestra el juego activo y duración en el Panel de Control."
                },
                HowItWorks = "Analiza los manifiestos VDF de Valve y consulta la API de Steam Store.",
                ProTip = "Haga clic en cualquier juego para ver detalles de redirección o abrir su carpeta.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "stats",
                Symbol = Wpf.Ui.Controls.SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Estadísticas y Rendimiento",
                Subtitle = "Telemetría en tiempo real",
                Badge = "ESTADÍSTICAS",
                Description = "Supervise las velocidades de transferencia, el volumen de datos subidos y el historial de sincronizaciones.",
                Highlights = new()
                {
                    "Gráficos en Vivo: Muestra el ancho de banda de subida y descarga.",
                    "Registro de Auditoría: Historial completo de eventos de sincronización."
                },
                HowItWorks = "Registra métricas durante las tareas de subida y las presenta en gráficos interactivos.",
                ProTip = "Revise las estadísticas después de jugar para verificar la sincronización.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Mantenimiento y Limpieza",
                Subtitle = "Optimice su almacenamiento",
                Badge = "UTILIDAD",
                Description = "Limpie archivos temporales obsoletos y elimine instantáneas antiguas para liberar espacio en disco.",
                Highlights = new()
                {
                    "Optimización en 1 Clic: Limpieza rápida de caché.",
                    "Protección Segura: Nunca borra partidas guardadas activas ni copias verificadas en la nube."
                },
                HowItWorks = "Escanea directorios temporales y purga instantáneas que superen el límite configurado.",
                ProTip = "Ejecute una limpieza al mes para mantener su disco optimizado.",
                TargetPageType = typeof(CleanupPage)
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Saves em Nuvem Universal",
                Subtitle = "Proteção em Modo Seguro sem risco",
                Badge = "MODO SEGURO",
                Description = "O Universal Cloud Saves monitora as pastas de saves diretamente no sistema de arquivos, criando backups, gerando pontos de restauração e enviando saves para o Google Drive.",
                Highlights = new()
                {
                    "100% Seguro contra Anti-Cheat: Execução totalmente fora do processo sem injetar DLLs.",
                    "Sincronização em Nuvem: Envia saves para o Google Drive ou sua pasta local de nuvem.",
                    "Pôsteres Oficiais da Steam: Obtém automaticamente as artes oficiais em alta resolução.",
                    "Monitoramento Automático: Detecta quando você joga e sincroniza tudo sem esforço."
                },
                HowItWorks = "Localiza os diretórios de saves e faz o upload automático para o Google Drive ao fechar o jogo.",
                ProTip = "Clique no botão [📁 Drive] em qualquer jogo para abrir sua pasta salva diretamente no Google Drive!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = Wpf.Ui.Controls.SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Histórico de Versões e Restauração",
                Subtitle = "Pontos de restauração com data e hora",
                Badge = "CONTROLE DE VERSÕES",
                Description = "Nunca mais perca seu progresso por arquivos corrompidos ou atualizações problemáticas.",
                Highlights = new()
                {
                    "Instantâneos Automáticos: Criados sempre ao fechar o jogo ou antes de sincronizar.",
                    "Restauração em 1 Clique: Volte a qualquer versão anterior instantaneamente.",
                    "Navegador de Arquivos: Abra e examine arquivos de backup diretamente no Windows Explorer."
                },
                HowItWorks = "Armazena cópias fiéis no repositório local e permite restaurar versões anteriores com segurança.",
                ProTip = "Clique em [🕒 Histórico] em qualquer jogo para visualizar os pontos de restauração.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Redirecionamento Steam Cloud (Core)",
                Subtitle = "Redirecionamento nativo da API Steam",
                Badge = "MOTOR PRINCIPAL",
                Description = "Redireciona operações de save da Steam Cloud para seu próprio Google Drive ou pasta personalizada.",
                Highlights = new()
                {
                    "Armazenamento Personalizado: Guarde dados ilimitados no Google Drive.",
                    "Compatibilidade Total: Os jogos continuam usando a Steam Cloud normalmente.",
                    "Sem Intermediários: Comunicação direta entre seu computador e o Google Drive."
                },
                HowItWorks = "O núcleo DLL intercepta chamadas da Steam Cloud e encaminha seus saves para o armazenamento escolhido.",
                ProTip = "Configure o Google Drive na página Provedor de Nuvem.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Biblioteca Steam e Pôsteres",
                Subtitle = "Detecção automática de jogos",
                Badge = "BIBLIOTECA",
                Description = "Detecta jogos instalados em todas as bibliotecas da Steam e exibe artes oficiais e status em tempo real.",
                Highlights = new()
                {
                    "Verificação Multi-Drive: Identifica bibliotecas Steam em qualquer partição.",
                    "Status em Tempo Real: Mostra o jogo em execução e tempo jogado no Painel."
                },
                HowItWorks = "Lê manifestos VDF e consulta a API da Loja Steam para baixar as capas mais recentes.",
                ProTip = "Clique em um jogo na lista para abrir sua pasta de instalação ou sincronizar.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "stats",
                Symbol = Wpf.Ui.Controls.SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Estatísticas e Desempenho",
                Subtitle = "Telemetria de sincronização",
                Badge = "ESTATÍSTICAS",
                Description = "Acompanhe velocidades de transferência, dados enviados e histórico de sincronizações.",
                Highlights = new()
                {
                    "Gráficos em Tempo Real: Monitoramento de largura de banda de upload e download.",
                    "Log de Auditoria: Histórico detalhado de todas as sincronizações."
                },
                HowItWorks = "Métricas gravadas durante o upload em segundo plano são plotadas em gráficos estilo Steam.",
                ProTip = "Verifique a página de estatísticas após jogar para confirmar o envio dos saves.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Manutenção e Limpeza",
                Subtitle = "Otimize seu espaço",
                Badge = "UTILITÁRIO",
                Description = "Remova arquivos temporários e backups antigos para liberar espaço em disco.",
                Highlights = new()
                {
                    "Otimização em 1 Clique: Limpeza rápida de arquivos de cache obsoletos.",
                    "Verificação Segura: Saves ativos e dados na nuvem nunca são excluídos."
                },
                HowItWorks = "Analisa diretórios temporários e descarta backups antigos com segurança.",
                ProTip = "Faça uma limpeza mensal para manter seu disco leve.",
                TargetPageType = typeof(CleanupPage)
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xA4, 0xD0, 0x07)),
                Title = "Simpanan Awan Sejagat",
                Subtitle = "Perlindungan Mod Selamat sifar risiko",
                Badge = "MOD SELAMAT",
                Description = "Simpanan Awan Sejagat memantau folder simpanan permainan terus pada sistem fail, mencipta sandaran, titik pemulihan, dan memuat naik ke Google Drive secara automatik.",
                Highlights = new()
                {
                    "100% Selamat Anti-Cheat: Beroperasi di luar proses tanpa menyuntik sebarang DLL ke dalam permainan.",
                    "Penyegerakan Awan: Memuat naik simpanan terus ke Google Drive atau storan awan tempatan anda.",
                    "Poster Rasmi Steam: Mendapatkan seni poster rasmi resolusi tinggi secara automatik.",
                    "Pemantauan Automatik: Mengesan sesi permainan anda dan menyegerakkan kemajuan secara lancar."
                },
                HowItWorks = "Mengesan lokasi simpanan fail dan memuat naik perubahan ke Google Drive sebaik sahaja permainan ditutup.",
                ProTip = "Klik butang [📁 Drive] pada mana-mana kad permainan untuk membuka folder simpanan anda di Google Drive!",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "history",
                Symbol = Wpf.Ui.Controls.SymbolRegular.History24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Sejarah Versi Simpanan & Pemulihan",
                Subtitle = "Titik pemulihan bertarikh",
                Badge = "KAWALAN VERSI",
                Description = "Jangan risau tentang fail simpanan rosak atau kemas kini bermasalah. CloudRedirect menyimpan salinan setiap sesi permainan anda.",
                Highlights = new()
                {
                    "Salinan Automatik: Dicipta setiap kali permainan ditutup atau sebelum penyegerakan.",
                    "Pemulihan 1 Klik: Pulihkan mana-mana versi simpanan terdahulu dengan serta-merta.",
                    "Penyemak Imbas Fail: Buka dan periksa fail salinan terus dalam Windows Explorer."
                },
                HowItWorks = "Salinan disimpan secara selamat dalam storan setempat dan boleh dipulihkan tanpa menjejaskan simpanan lain.",
                ProTip = "Klik [🕒 Sejarah] pada mana-mana kad permainan untuk melihat semua titik pemulihan yang ada.",
                TargetPageType = typeof(UniversalSavesPage)
            },
            new()
            {
                Id = "redirection",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cloud24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Pelencongan Steam Cloud (Teras)",
                Subtitle = "Pelencongan API Steam natif",
                Badge = "ENJIN TERAS",
                Description = "Melencongkan operasi simpanan Steam Cloud ke Google Drive atau folder tempatan anda sendiri dan bukannya pelayan Valve.",
                Highlights = new()
                {
                    "Storan Tersuai: Simpan data permainan tanpa had di Google Drive anda sendiri.",
                    "Keserasian Penuh: Permainan terus menggunakan Steam Cloud seperti biasa.",
                    "Tiada Pelayan Perantara: Data berpindah terus antara komputer anda dan Google Drive."
                },
                HowItWorks = "Enjin DLL memintas panggilan fail Steam Cloud dan menghantarnya terus ke storan awan anda.",
                ProTip = "Sediakan akaun Google Drive anda di halaman Pembekal Awan.",
                TargetPageType = typeof(CloudProviderPage)
            },
            new()
            {
                Id = "apps",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Apps24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xC6, 0xD4, 0xDF)),
                Title = "Pustaka Steam & Poster Permainan",
                Subtitle = "Pengesanan automatik",
                Badge = "PUSTAKA",
                Description = "Mengesan permainan Steam yang dipasang merentasi semua pemacu cakera anda dan memaparkan poster serta status masa nyata.",
                Highlights = new()
                {
                    "Pengimbasan Berbilang Pemacu: Mengesan pustaka Steam di mana-mana pemacu cakera.",
                    "Status Masa Nyata: Memaparkan permainan yang sedang dimainkan di Papan Pemuka."
                },
                HowItWorks = "Membaca manifes Valve VDF dan memuat turun poster rasmi terkini daripada Steam Store API.",
                ProTip = "Klik mana-mana permainan dalam senarai untuk membuka foldernya atau memulakan penyegerakan.",
                TargetPageType = typeof(AppsPage)
            },
            new()
            {
                Id = "stats",
                Symbol = Wpf.Ui.Controls.SymbolRegular.DataBarHorizontal24,
                IconColor = new SolidColorBrush(Color.FromRgb(0x66, 0xC0, 0xF4)),
                Title = "Statistik & Prestasi",
                Subtitle = "Telemetri masa nyata",
                Badge = "STATISTIK",
                Description = "Pantau kelajuan pemindahan, jumlah data yang dimuat naik, dan log peristiwa penyegerakan.",
                Highlights = new()
                {
                    "Graf Masa Nyata: Memantau kelajuan muat naik dan muat turun.",
                    "Log Audit: Sejarah lengkap setiap peristiwa penyegerakan fail."
                },
                HowItWorks = "Merekod metrik prestasi semasa muat naik latar belakang dan memaparkannya dalam graf bergaya Steam.",
                ProTip = "Semak statistik selepas bermain untuk mengesahkan penyegerakan telah berjaya.",
                TargetPageType = typeof(StatsPage)
            },
            new()
            {
                Id = "cleanup",
                Symbol = Wpf.Ui.Controls.SymbolRegular.Broom24,
                IconColor = new SolidColorBrush(Color.FromRgb(0xE5, 0xA9, 0x3C)),
                Title = "Penyelenggaraan & Pembersihan",
                Subtitle = "Optimumkan storan anda",
                Badge = "UTILITI",
                Description = "Bersihkan fail cache sementara dan buang salinan lama untuk menjimatkan ruang cakera.",
                Highlights = new()
                {
                    "Optimum 1 Klik: Pembersihan segera fail cache dan muat turun lama.",
                    "Pembersihan Selamat: Tidak akan memadamkan simpanan aktif atau sandaran awan."
                },
                HowItWorks = "Mengimbas direktori sementara dan memadamkan salinan lama mengikut polisi penyimpanan anda.",
                ProTip = "Jalankan pembersihan sebulan sekali untuk memastikan pemacu anda sentiasa kemas.",
                TargetPageType = typeof(CleanupPage)
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
                Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24,
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
