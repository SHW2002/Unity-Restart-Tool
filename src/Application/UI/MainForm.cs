using System.Diagnostics;
using System.Globalization;
using UnityRestartTool.Infrastructure;
using UnityRestartTool.Models;
using UnityRestartTool.Services;
using UnityRestartTool.Settings;
using UnityRestartTool.UI.Controls;

namespace UnityRestartTool.UI;

internal sealed class MainForm : Form
{
    private const int MaxVisibleLogEntries = 1000;
    private static readonly Color BackgroundColor = Color.FromArgb(20, 23, 29);
    private static readonly Color SurfaceColor = Color.FromArgb(29, 33, 41);
    private static readonly Color RaisedColor = Color.FromArgb(37, 42, 52);
    private static readonly Color BorderColor = Color.FromArgb(61, 68, 82);
    private static readonly Color PrimaryTextColor = Color.FromArgb(239, 242, 247);
    private static readonly Color SecondaryTextColor = Color.FromArgb(164, 173, 189);
    private static readonly Color AccentColor = Color.FromArgb(29, 78, 216);
    private static readonly Color AccentHoverColor = Color.FromArgb(37, 99, 235);
    private static readonly Color SuccessColor = Color.FromArgb(78, 194, 133);
    private static readonly Color WarningColor = Color.FromArgb(245, 190, 71);
    private static readonly Color ErrorColor = Color.FromArgb(240, 103, 117);
    private static readonly Color InfoColor = Color.FromArgb(91, 163, 224);

    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly bool _startInTray;
    private readonly Icon? _applicationIcon;
    private readonly WindowService _windowService;
    private readonly EditorDiscoveryService _discoveryService;
    private readonly CompanionInstaller _companionInstaller;
    private readonly CompanionClient _companionClient;
    private readonly WindowTitleRenamerClient _titleRenamerClient;
    private readonly RestartOrchestrator _orchestrator;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2500 };
    private readonly System.Windows.Forms.Timer _scheduleTimer = new() { Interval = 10000 };
    private readonly System.Windows.Forms.Timer _titleRenamerTimer = new() { Interval = 30000 };
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, bool> _manualSelections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _operationStatuses = new(StringComparer.OrdinalIgnoreCase);

    private readonly DataGridView _instanceGrid = new();
    private readonly Button _refreshButton = new ThemedButton();
    private readonly Button _restartButton = new ThemedButton();
    private readonly Button _installButton = new ThemedButton();
    private readonly Button _uninstallButton = new ThemedButton();
    private readonly CheckBox _scheduleEnabledCheckBox = new();
    private readonly DateTimePicker _scheduleTimePicker = new();
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private readonly CheckBox _startMinimizedCheckBox = new();
    private readonly Label _nextScheduleLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _titleRenamerStatusLabel = new();
    private readonly ListView _logList = new();
    private readonly Label _statusLabel = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();

    private IReadOnlyList<EditorInstance> _instances = [];
    private bool _refreshing;
    private bool _updatingGrid;
    private bool _updatingSettings;
    private bool _checkingTitleRenamer;
    private bool _exitRequested;
    private bool _trayHintShown;

    public MainForm(
        AppSettings settings,
        SettingsStore settingsStore,
        AppLogger logger,
        bool startInTray)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _logger = logger;
        _startInTray = startInTray;
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _windowService = new WindowService();
        _discoveryService = new EditorDiscoveryService(_windowService, logger);
        _companionInstaller = new CompanionInstaller();
        _companionClient = new CompanionClient(_companionInstaller);
        _titleRenamerClient = new WindowTitleRenamerClient(logger);
        _orchestrator = new RestartOrchestrator(
            _windowService,
            _companionClient,
            _titleRenamerClient,
            logger);

        SuspendLayout();
        try
        {
            ConfigureForm();
            BuildLayout();
            ConfigureTray();
            LoadSettingsIntoControls();
            WireEvents();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void ConfigureForm()
    {
        Text = "Unity Restart Tool";
        ClientSize = new Size(1380, 850);
        MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackgroundColor;
        ForeColor = PrimaryTextColor;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        Icon = _applicationIcon ?? SystemIcons.Application;
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20, 16, 20, 12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 215F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildScheduleBand(), 0, 1);
        root.Controls.Add(BuildTitleRenamerBand(), 0, 2);
        root.Controls.Add(BuildInstanceArea(), 0, 3);
        root.Controls.Add(BuildLogArea(), 0, 4);
        root.Controls.Add(BuildStatusBar(), 0, 5);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        TableLayoutPanel header = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, 8),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        TableLayoutPanel titles = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        titles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label title = new()
        {
            AutoSize = true,
            Text = "Unity Restart Tool",
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = PrimaryTextColor,
            Margin = new Padding(0, 0, 0, 2),
        };
        _summaryLabel.AutoSize = true;
        _summaryLabel.ForeColor = SecondaryTextColor;
        _summaryLabel.Text = "正在发现 Unity 与团结编辑器...";
        _summaryLabel.Margin = new Padding(2, 2, 0, 0);
        titles.Controls.Add(title, 0, 0);
        titles.Controls.Add(_summaryLabel, 0, 1);

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 9, 0, 0),
            Margin = Padding.Empty,
        };
        ConfigureButton(_refreshButton, "刷新", false, 86);
        ConfigureButton(_installButton, "安装 / 升级", false, 132);
        ConfigureButton(_uninstallButton, "移除包", false, 112);
        ConfigureButton(_restartButton, "立即重启", true, 112);
        actions.Controls.AddRange([_refreshButton, _installButton, _uninstallButton, _restartButton]);

        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(actions, 1, 0);
        return header;
    }

    private Control BuildScheduleBand()
    {
        Panel band = new()
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            Padding = new Padding(16, 13, 16, 10),
            Margin = new Padding(0, 0, 0, 10),
        };
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _scheduleEnabledCheckBox.Text = "启用每日定时";
        StyleCheckBox(_scheduleEnabledCheckBox, new Padding(0, 8, 16, 0));
        _scheduleTimePicker.Format = DateTimePickerFormat.Custom;
        _scheduleTimePicker.CustomFormat = "HH:mm";
        _scheduleTimePicker.ShowUpDown = true;
        _scheduleTimePicker.Width = 122;
        _scheduleTimePicker.Font = new Font("Segoe UI Semibold", 10F);
        _scheduleTimePicker.Margin = new Padding(0, 4, 18, 0);
        _startWithWindowsCheckBox.Text = "开机自启";
        StyleCheckBox(_startWithWindowsCheckBox, new Padding(0, 8, 18, 0));
        _startMinimizedCheckBox.Text = "登录后驻留托盘";
        StyleCheckBox(_startMinimizedCheckBox, new Padding(0, 8, 18, 0));

        _nextScheduleLabel.AutoSize = true;
        _nextScheduleLabel.ForeColor = InfoColor;
        _nextScheduleLabel.TextAlign = ContentAlignment.MiddleRight;
        _nextScheduleLabel.Margin = new Padding(18, 8, 0, 0);

        layout.Controls.Add(_scheduleEnabledCheckBox, 0, 0);
        layout.Controls.Add(_scheduleTimePicker, 1, 0);
        layout.Controls.Add(_startWithWindowsCheckBox, 2, 0);
        layout.Controls.Add(_startMinimizedCheckBox, 3, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "错过时间不补执行",
            ForeColor = SecondaryTextColor,
            Margin = new Padding(0, 9, 0, 0),
        }, 4, 0);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill }, 5, 0);
        layout.Controls.Add(_nextScheduleLabel, 6, 0);
        band.Controls.Add(layout);
        return band;
    }

    private Control BuildInstanceArea()
    {
        Panel area = new()
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 10),
        };
        ConfigureInstanceGrid();
        area.Controls.Add(_instanceGrid);
        return area;
    }

    private Control BuildTitleRenamerBand()
    {
        TableLayoutPanel band = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(16, 6, 16, 6),
            Margin = new Padding(0, 0, 0, 10),
        };
        band.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        band.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        band.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        band.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "标题联动",
            ForeColor = PrimaryTextColor,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
            Margin = new Padding(0, 6, 0, 4),
        }, 0, 0);

        _titleRenamerStatusLabel.AutoSize = true;
        _titleRenamerStatusLabel.Text = "正在检测 Window-Title-Renamer...";
        _titleRenamerStatusLabel.ForeColor = InfoColor;
        _titleRenamerStatusLabel.Margin = new Padding(18, 6, 0, 4);
        band.Controls.Add(_titleRenamerStatusLabel, 1, 0);
        return band;
    }

    private void ConfigureInstanceGrid()
    {
        _instanceGrid.Dock = DockStyle.Fill;
        _instanceGrid.BackgroundColor = SurfaceColor;
        _instanceGrid.BorderStyle = BorderStyle.None;
        _instanceGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _instanceGrid.GridColor = BorderColor;
        _instanceGrid.RowHeadersVisible = false;
        _instanceGrid.AllowUserToAddRows = false;
        _instanceGrid.AllowUserToDeleteRows = false;
        _instanceGrid.AllowUserToResizeRows = false;
        _instanceGrid.MultiSelect = false;
        _instanceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _instanceGrid.AutoGenerateColumns = false;
        _instanceGrid.EnableHeadersVisualStyles = false;
        _instanceGrid.ColumnHeadersHeight = 36;
        _instanceGrid.RowTemplate.Height = 38;
        _instanceGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = SurfaceColor,
            ForeColor = PrimaryTextColor,
            SelectionBackColor = Color.FromArgb(30, 64, 112),
            SelectionForeColor = PrimaryTextColor,
            Padding = new Padding(6, 0, 6, 0),
        };
        _instanceGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(32, 36, 45);
        _instanceGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = RaisedColor,
            ForeColor = SecondaryTextColor,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0),
        };

        _instanceGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "SelectedColumn",
            HeaderText = "重启",
            Width = 76,
            FlatStyle = FlatStyle.Flat,
        });
        _instanceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ProjectColumn",
            HeaderText = "项目",
            MinimumWidth = 145,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 26,
            ReadOnly = true,
        });
        _instanceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "EngineColumn",
            HeaderText = "编辑器",
            Width = 194,
            ReadOnly = true,
        });
        _instanceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "RuntimeColumn",
            HeaderText = "运行时间",
            Width = 130,
            ReadOnly = true,
        });
        _instanceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "TitleColumn",
            HeaderText = "窗口标题",
            MinimumWidth = 150,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25,
            ReadOnly = true,
        });
        _instanceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CompanionColumn",
            HeaderText = "Companion",
            Width = 152,
            ReadOnly = true,
        });
        _instanceGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "ScheduleColumn",
            HeaderText = "纳入定时",
            Width = 130,
            FlatStyle = FlatStyle.Flat,
        });
        _instanceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "StatusColumn",
            HeaderText = "状态",
            MinimumWidth = 165,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 24,
            ReadOnly = true,
        });
    }

    private Control BuildLogArea()
    {
        TableLayoutPanel area = new()
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
        };
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        TableLayoutPanel header = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 6, 8, 6),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "运行日志",
            ForeColor = PrimaryTextColor,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 2),
        }, 0, 0);
        FlowLayoutPanel logActions = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        Button copyButton = new ThemedButton();
        ConfigureButton(copyButton, "复制", false, 78, 32);
        copyButton.Click += (_, _) => CopySelectedLog();
        Button openButton = new ThemedButton();
        ConfigureButton(openButton, "打开目录", false, 112, 32);
        openButton.Click += (_, _) => OpenLogDirectory();
        logActions.Controls.AddRange([copyButton, openButton]);
        header.Controls.Add(logActions, 1, 0);

        _logList.Dock = DockStyle.Fill;
        _logList.View = View.Details;
        _logList.FullRowSelect = true;
        _logList.HideSelection = false;
        _logList.BorderStyle = BorderStyle.None;
        _logList.BackColor = Color.FromArgb(24, 27, 34);
        _logList.ForeColor = PrimaryTextColor;
        _logList.HeaderStyle = ColumnHeaderStyle.None;
        _logList.Columns.Add("时间", 94);
        _logList.Columns.Add("级别", 66);
        _logList.Columns.Add("来源", 165);
        _logList.Columns.Add("内容", 980);
        area.Controls.Add(header, 0, 0);
        area.Controls.Add(_logList, 0, 1);
        return area;
    }

    private Control BuildStatusBar()
    {
        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "就绪";
        _statusLabel.ForeColor = SecondaryTextColor;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Padding = new Padding(2, 4, 0, 2);
        return _statusLabel;
    }

    private void ConfigureTray()
    {
        ToolStripMenuItem showItem = new("显示主窗口");
        showItem.Click += async (_, _) => await RestoreFromTrayAsync();
        ToolStripMenuItem restartItem = new("立即重启勾选实例");
        restartItem.Click += async (_, _) =>
        {
            await RefreshInstancesAsync(false);
            await RestartSelectedAsync(RestartTrigger.Manual);
        };
        ToolStripMenuItem pauseItem = new("暂停每日计划");
        pauseItem.Click += (_, _) =>
        {
            _scheduleEnabledCheckBox.Checked = false;
            ShowFromAnyThread("每日计划已暂停", AppLogLevel.Warning);
        };
        ToolStripMenuItem exitItem = new("彻底退出");
        exitItem.Click += (_, _) => ExitApplication();
        _trayMenu.Items.AddRange([showItem, restartItem, pauseItem, new ToolStripSeparator(), exitItem]);

        _notifyIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _notifyIcon.Text = "Unity Restart Tool";
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += async (_, _) => await RestoreFromTrayAsync();
    }

    private void LoadSettingsIntoControls()
    {
        _updatingSettings = true;
        try
        {
            _scheduleEnabledCheckBox.Checked = _settings.ScheduleEnabled;
            if (!SchedulePlanner.TryParseTime(_settings.ScheduleTime, out TimeOnly scheduledTime))
            {
                scheduledTime = new TimeOnly(4, 0);
                _settings.ScheduleTime = "04:00";
            }
            _scheduleTimePicker.Value = DateTime.Today.Add(scheduledTime.ToTimeSpan());
            _startWithWindowsCheckBox.Checked = StartupRegistration.IsEnabled();
            _settings.StartWithWindows = _startWithWindowsCheckBox.Checked;
            _startMinimizedCheckBox.Checked = _settings.StartMinimizedToTray;
            UpdateNextScheduleLabel();
        }
        finally
        {
            _updatingSettings = false;
        }
    }

    private void WireEvents()
    {
        Shown += MainForm_Shown;
        Resize += MainForm_Resize;
        FormClosing += MainForm_FormClosing;
        _refreshButton.Click += async (_, _) =>
        {
            await RefreshInstancesAsync(true);
            await RefreshTitleRenamerStatusAsync();
        };
        _restartButton.Click += async (_, _) => await RestartSelectedAsync(RestartTrigger.Manual);
        _installButton.Click += async (_, _) => await InstallCompanionForCurrentAsync();
        _uninstallButton.Click += async (_, _) => await UninstallCompanionForCurrentAsync();
        _instanceGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_instanceGrid.IsCurrentCellDirty)
            {
                _instanceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _instanceGrid.CellValueChanged += InstanceGrid_CellValueChanged;
        _instanceGrid.SelectionChanged += (_, _) => UpdatePackageButtons();
        _scheduleEnabledCheckBox.CheckedChanged += (_, _) => SaveScheduleSettings();
        _scheduleTimePicker.ValueChanged += (_, _) => SaveScheduleSettings();
        _startWithWindowsCheckBox.CheckedChanged += (_, _) => SaveStartupSettings();
        _startMinimizedCheckBox.CheckedChanged += (_, _) => SaveStartupSettings();
        _refreshTimer.Tick += async (_, _) => await RefreshInstancesAsync(false);
        _scheduleTimer.Tick += async (_, _) => await CheckScheduleAsync();
        _titleRenamerTimer.Tick += async (_, _) => await RefreshTitleRenamerStatusAsync();
        _orchestrator.ProgressChanged += Orchestrator_ProgressChanged;
        _logger.EntryWritten += Logger_EntryWritten;
    }

    private async void MainForm_Shown(object? sender, EventArgs eventArgs)
    {
        _logger.Info("应用", "Unity Restart Tool 已启动");
        await RefreshInstancesAsync(true);
        await RefreshTitleRenamerStatusAsync();
        _refreshTimer.Start();
        _scheduleTimer.Start();
        _titleRenamerTimer.Start();
        if (_startInTray)
        {
            BeginInvoke(HideToTray);
        }
    }

    private async Task RefreshInstancesAsync(bool announce)
    {
        if (_refreshing || _orchestrator.IsRunning || IsDisposed || Disposing)
        {
            return;
        }

        _refreshing = true;
        _refreshButton.Enabled = false;
        try
        {
            IReadOnlyList<EditorInstance> discovered = await Task.Run(_discoveryService.Discover);
            if (IsDisposed || Disposing)
            {
                return;
            }

            _instances = discovered;
            foreach (EditorInstance instance in _instances)
            {
                _manualSelections.TryAdd(instance.ProjectPath, true);
                if (!_settings.Projects.ContainsKey(instance.ProjectPath))
                {
                    _settings.Projects[instance.ProjectPath] = new ProjectPolicy();
                }
            }
            PopulateGrid();
            _summaryLabel.Text = _instances.Count == 0
                ? "未发现具有主窗口的 Unity 或团结编辑器"
                : $"已发现 {_instances.Count} 个主编辑器实例，后台导入进程已排除";
            if (announce)
            {
                ShowStatus($"刷新完成，共 {_instances.Count} 个实例", AppLogLevel.Info);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("发现", "刷新编辑器列表失败", exception);
            ShowStatus($"刷新失败: {exception.Message}", AppLogLevel.Error);
        }
        finally
        {
            _refreshing = false;
            _refreshButton.Enabled = true;
            UpdateActionButtons();
        }
    }

    private async Task RefreshTitleRenamerStatusAsync()
    {
        if (_checkingTitleRenamer || IsDisposed || Disposing)
        {
            return;
        }

        _checkingTitleRenamer = true;
        try
        {
            WindowTitleRenamerStatus status = await _titleRenamerClient.CheckStatusAsync(
                _lifetimeCancellation.Token);
            if (IsDisposed || Disposing)
            {
                return;
            }

            _titleRenamerStatusLabel.Text = status.Message;
            _titleRenamerStatusLabel.ForeColor = status.Health switch
            {
                WindowTitleRenamerHealth.Ready => SuccessColor,
                WindowTitleRenamerHealth.Incompatible => ErrorColor,
                WindowTitleRenamerHealth.Unavailable => WarningColor,
                _ => SecondaryTextColor,
            };
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _titleRenamerStatusLabel.Text = $"检测失败: {exception.Message}";
            _titleRenamerStatusLabel.ForeColor = ErrorColor;
            _logger.Warning("标题联动", $"状态检测失败: {exception.Message}");
        }
        finally
        {
            _checkingTitleRenamer = false;
        }
    }

    private void PopulateGrid()
    {
        string? selectedProject = CurrentInstance()?.ProjectPath;
        _updatingGrid = true;
        _instanceGrid.SuspendLayout();
        try
        {
            _instanceGrid.Rows.Clear();
            DataGridViewRow? preferredRow = null;
            foreach (EditorInstance instance in _instances)
            {
                CompanionState companion = _companionClient.GetState(instance);
                bool selected = _manualSelections.GetValueOrDefault(instance.ProjectPath, true);
                bool scheduled = _settings.Projects.GetValueOrDefault(instance.ProjectPath)?.IncludeInSchedule ?? false;
                string status = _operationStatuses.GetValueOrDefault(instance.ProjectPath, "就绪");
                int rowIndex = _instanceGrid.Rows.Add(
                    selected,
                    instance.ProjectName,
                    $"{KindLabel(instance.Kind)} {instance.EditorVersion}",
                    FormatDuration(instance.RunningTime),
                    instance.WindowTitle,
                    CompanionLabel(companion),
                    scheduled,
                    status);
                DataGridViewRow row = _instanceGrid.Rows[rowIndex];
                row.Tag = instance;
                row.Cells["ProjectColumn"].ToolTipText = instance.ProjectPath;
                row.Cells["TitleColumn"].ToolTipText = instance.WindowTitle;
                row.Cells["CompanionColumn"].ToolTipText = companion.Message;
                row.Cells["CompanionColumn"].Style.ForeColor = CompanionColor(companion.Health);
                row.Cells["StatusColumn"].ToolTipText = status;
                if (string.Equals(instance.ProjectPath, selectedProject, StringComparison.OrdinalIgnoreCase))
                {
                    preferredRow = row;
                }
            }

            _instanceGrid.ClearSelection();
            DataGridViewRow? rowToSelect = preferredRow ??
                (_instanceGrid.Rows.Count > 0 ? _instanceGrid.Rows[0] : null);
            if (rowToSelect is not null)
            {
                rowToSelect.Selected = true;
                _instanceGrid.CurrentCell = rowToSelect.Cells["ProjectColumn"];
            }
        }
        finally
        {
            _instanceGrid.ResumeLayout();
            _updatingGrid = false;
        }

        UpdateActionButtons();
        UpdatePackageButtons();
    }

    private void InstanceGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (_updatingGrid || eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0)
        {
            return;
        }

        DataGridViewRow row = _instanceGrid.Rows[eventArgs.RowIndex];
        if (row.Tag is not EditorInstance instance)
        {
            return;
        }

        string columnName = _instanceGrid.Columns[eventArgs.ColumnIndex].Name;
        if (!TryGetBooleanCellValue(
                columnName,
                row.Cells[eventArgs.ColumnIndex].Value,
                out bool value))
        {
            return;
        }

        if (columnName == "SelectedColumn")
        {
            _manualSelections[instance.ProjectPath] = value;
            UpdateActionButtons();
        }
        else if (columnName == "ScheduleColumn")
        {
            ProjectPolicy policy = _settings.Projects.GetValueOrDefault(instance.ProjectPath) ?? new ProjectPolicy();
            policy.IncludeInSchedule = value;
            _settings.Projects[instance.ProjectPath] = policy;
            SaveSettings();
        }
    }

    internal static bool TryGetBooleanCellValue(
        string columnName,
        object? cellValue,
        out bool value)
    {
        if (columnName is not "SelectedColumn" and not "ScheduleColumn")
        {
            value = false;
            return false;
        }

        value = Convert.ToBoolean(cellValue ?? false, CultureInfo.InvariantCulture);
        return true;
    }

    private async Task RestartSelectedAsync(RestartTrigger trigger)
    {
        if (_orchestrator.IsRunning)
        {
            ShowStatus("已有重启批次正在执行", AppLogLevel.Warning);
            return;
        }

        IReadOnlyList<EditorInstance> targets = trigger == RestartTrigger.Scheduled
            ? _instances.Where(instance =>
                _settings.Projects.GetValueOrDefault(instance.ProjectPath)?.IncludeInSchedule == true).ToArray()
            : _instances.Where(instance =>
                _manualSelections.GetValueOrDefault(instance.ProjectPath, true)).ToArray();
        if (targets.Count == 0)
        {
            ShowStatus(trigger == RestartTrigger.Scheduled
                ? "每日计划没有已启用的项目"
                : "请先勾选至少一个实例", AppLogLevel.Warning);
            return;
        }

        SetOperationControls(false);
        _refreshTimer.Stop();
        _logger.Info("批次", $"开始{(trigger == RestartTrigger.Manual ? "手动" : "定时")}重启，共 {targets.Count} 个目标");
        try
        {
            RestartBatchResult result = await _orchestrator.RestartAsync(
                targets,
                trigger,
                _lifetimeCancellation.Token);
            string summary = $"批次完成：成功 {result.SucceededCount}，跳过 {result.SkippedCount}，失败 {result.FailedCount}";
            _logger.Info("批次", summary);
            ShowStatus(summary, result.FailedCount > 0 ? AppLogLevel.Error :
                result.SkippedCount > 0 ? AppLogLevel.Warning : AppLogLevel.Info);
            ShowBatchNotification(summary, result.FailedCount > 0);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("批次", "重启批次失败", exception);
            ShowStatus($"重启批次失败: {exception.Message}", AppLogLevel.Error);
        }
        finally
        {
            SetOperationControls(true);
            await RefreshInstancesAsync(false);
            if (!IsDisposed && !Disposing && Visible)
            {
                _refreshTimer.Start();
            }
        }
    }

    private async Task InstallCompanionForCurrentAsync()
    {
        EditorInstance? instance = CurrentInstance();
        if (instance is null)
        {
            return;
        }

        SetOperationControls(false);
        try
        {
            await Task.Run(() => _companionInstaller.Install(instance.ProjectPath));
            _logger.Info(instance.ProjectName, "已安装或升级 Editor companion，正在等待 Unity 导入");
            ShowStatus($"{instance.ProjectName}：已安装 Companion", AppLogLevel.Info);
            await RefreshInstancesAsync(false);
        }
        catch (Exception exception)
        {
            _logger.Error(instance.ProjectName, "安装 Companion 失败", exception);
            ShowStatus($"安装失败: {exception.Message}", AppLogLevel.Error);
        }
        finally
        {
            SetOperationControls(true);
        }
    }

    private async Task UninstallCompanionForCurrentAsync()
    {
        EditorInstance? instance = CurrentInstance();
        if (instance is null)
        {
            return;
        }

        DialogResult confirmation = MessageBox.Show(
            this,
            $"确认从项目 {instance.ProjectName} 移除 Unity Restart Companion？",
            "移除 Companion",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (confirmation != DialogResult.OK)
        {
            return;
        }

        SetOperationControls(false);
        try
        {
            await Task.Run(() => _companionInstaller.Uninstall(instance.ProjectPath));
            _logger.Info(instance.ProjectName, "已移除 Editor companion");
            ShowStatus($"{instance.ProjectName}：已移除 Companion", AppLogLevel.Info);
            await RefreshInstancesAsync(false);
        }
        catch (Exception exception)
        {
            _logger.Error(instance.ProjectName, "移除 Companion 失败", exception);
            ShowStatus($"移除失败: {exception.Message}", AppLogLevel.Error);
        }
        finally
        {
            SetOperationControls(true);
        }
    }

    private async Task CheckScheduleAsync()
    {
        DateTime now = DateTime.Now;
        UpdateNextScheduleLabel();
        if (!SchedulePlanner.ShouldTrigger(_settings, now))
        {
            return;
        }

        _settings.LastScheduledTriggerDate = DateOnly.FromDateTime(now)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        SaveSettings();
        UpdateNextScheduleLabel();
        if (_orchestrator.IsRunning)
        {
            _logger.Warning("计划", "每日时间到达时已有批次运行，本日不补执行");
            return;
        }

        await RefreshInstancesAsync(false);
        await RestartSelectedAsync(RestartTrigger.Scheduled);
    }

    private void SaveScheduleSettings()
    {
        if (_updatingSettings)
        {
            return;
        }

        _settings.ScheduleEnabled = _scheduleEnabledCheckBox.Checked;
        _settings.ScheduleTime = _scheduleTimePicker.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        SaveSettings();
        UpdateNextScheduleLabel();
    }

    private void SaveStartupSettings()
    {
        if (_updatingSettings)
        {
            return;
        }

        try
        {
            StartupRegistration.SetEnabled(
                _startWithWindowsCheckBox.Checked,
                _startMinimizedCheckBox.Checked);
            _settings.StartWithWindows = _startWithWindowsCheckBox.Checked;
            _settings.StartMinimizedToTray = _startMinimizedCheckBox.Checked;
            SaveSettings();
        }
        catch (Exception exception)
        {
            _logger.Error("启动项", "更新开机自启失败", exception);
            ShowStatus($"开机自启设置失败: {exception.Message}", AppLogLevel.Error);
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            _logger.Error("设置", "保存设置失败", exception);
        }
    }

    private void UpdateNextScheduleLabel()
    {
        _nextScheduleLabel.Text = _settings.ScheduleEnabled
            ? $"下次计划：{SchedulePlanner.NextOccurrence(_settings, DateTime.Now):MM-dd HH:mm}"
            : "每日计划已关闭";
        _nextScheduleLabel.ForeColor = _settings.ScheduleEnabled ? InfoColor : SecondaryTextColor;
    }

    private void Orchestrator_ProgressChanged(object? sender, RestartProgress progress)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Orchestrator_ProgressChanged(sender, progress));
            return;
        }

        _operationStatuses[progress.ProjectPath] = progress.Message;
        DataGridViewRow? row = _instanceGrid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(candidate =>
                candidate.Tag is EditorInstance instance &&
                string.Equals(instance.ProjectPath, progress.ProjectPath, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            row.Cells["StatusColumn"].Value = progress.Message;
            row.Cells["StatusColumn"].ToolTipText = progress.Message;
            row.Cells["StatusColumn"].Style.ForeColor = progress.IsError
                ? ErrorColor
                : progress.Stage == RestartStage.Completed ? SuccessColor
                : progress.Stage == RestartStage.Skipped ? WarningColor
                : InfoColor;
        }
        ShowStatus(progress.Message, progress.IsError ? AppLogLevel.Error : AppLogLevel.Info);
    }

    private void Logger_EntryWritten(object? sender, LogEntry entry)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => AddLogEntry(entry));
        }
        else
        {
            AddLogEntry(entry);
        }
    }

    private void AddLogEntry(LogEntry entry)
    {
        ListViewItem item = new(entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        item.SubItems.Add(LogLevelLabel(entry.Level));
        item.SubItems.Add(entry.Source);
        item.SubItems.Add(entry.Message);
        item.ForeColor = LogLevelColor(entry.Level);
        item.ToolTipText = entry.Message;
        _logList.Items.Add(item);
        while (_logList.Items.Count > MaxVisibleLogEntries)
        {
            _logList.Items.RemoveAt(0);
        }
        item.EnsureVisible();
    }

    private void CopySelectedLog()
    {
        if (_logList.SelectedItems.Count == 0)
        {
            return;
        }

        string text = string.Join(Environment.NewLine, _logList.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => string.Join("\t", item.SubItems.Cast<ListViewItem.ListViewSubItem>()
                .Select(subItem => subItem.Text))));
        Clipboard.SetText(text);
        ShowStatus("已复制所选日志", AppLogLevel.Info);
    }

    private void OpenLogDirectory()
    {
        Process.Start(new ProcessStartInfo(_logger.LogDirectory) { UseShellExecute = true });
    }

    private void MainForm_Resize(object? sender, EventArgs eventArgs)
    {
        if (WindowState == FormWindowState.Minimized && Visible)
        {
            HideToTray();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        _logger.Info("应用", $"收到窗口关闭请求: {eventArgs.CloseReason}");
        bool systemIsTerminating = eventArgs.CloseReason is
            CloseReason.WindowsShutDown or
            CloseReason.TaskManagerClosing;
        if (!_exitRequested && !systemIsTerminating)
        {
            eventArgs.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        if (!Visible)
        {
            return;
        }

        Hide();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        _refreshTimer.Stop();
        _titleRenamerTimer.Stop();
        if (!_trayHintShown)
        {
            _notifyIcon.BalloonTipTitle = "Unity Restart Tool 正在后台运行";
            _notifyIcon.BalloonTipText = "每日计划和实例监控仍保持启用。";
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(2500);
            _trayHintShown = true;
        }
    }

    private async Task RestoreFromTrayAsync()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        await RefreshInstancesAsync(false);
        await RefreshTitleRenamerStatusAsync();
        if (!_orchestrator.IsRunning)
        {
            _refreshTimer.Start();
        }
        _titleRenamerTimer.Start();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _lifetimeCancellation.Cancel();
        _refreshTimer.Stop();
        _scheduleTimer.Stop();
        _titleRenamerTimer.Stop();
        _notifyIcon.Visible = false;
        Close();
    }

    private void ShowBatchNotification(string summary, bool error)
    {
        _notifyIcon.BalloonTipTitle = error ? "Unity 重启存在失败" : "Unity 重启完成";
        _notifyIcon.BalloonTipText = summary;
        _notifyIcon.BalloonTipIcon = error ? ToolTipIcon.Error : ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void ShowFromAnyThread(string message, AppLogLevel level)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowStatus(message, level));
        }
        else
        {
            ShowStatus(message, level);
        }
    }

    private void ShowStatus(string message, AppLogLevel level)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = LogLevelColor(level);
    }

    private EditorInstance? CurrentInstance() =>
        _instanceGrid.SelectedRows.Count > 0
            ? _instanceGrid.SelectedRows[0].Tag as EditorInstance
            : null;

    private void UpdateActionButtons()
    {
        _restartButton.Enabled = !_orchestrator.IsRunning &&
            _instances.Any(instance => _manualSelections.GetValueOrDefault(instance.ProjectPath, true));
    }

    private void UpdatePackageButtons()
    {
        EditorInstance? instance = CurrentInstance();
        bool enabled = instance is not null && !_orchestrator.IsRunning;
        _installButton.Enabled = enabled;
        _uninstallButton.Enabled = enabled &&
            _companionInstaller.Inspect(instance!.ProjectPath).Installed;
    }

    private void SetOperationControls(bool enabled)
    {
        _refreshButton.Enabled = enabled;
        _restartButton.Enabled = enabled;
        _installButton.Enabled = enabled;
        _uninstallButton.Enabled = enabled;
        _instanceGrid.Enabled = enabled;
    }

    private static void ConfigureButton(
        Button button,
        string text,
        bool primary,
        int width,
        int height = 36)
    {
        button.Text = text;
        button.Width = width;
        button.Height = height;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? AccentColor : BorderColor;
        button.BackColor = primary ? AccentColor : RaisedColor;
        button.ForeColor = PrimaryTextColor;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(6, 0, 0, 0);
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHoverColor : Color.FromArgb(48, 54, 66);
    }

    private static void StyleCheckBox(CheckBox checkBox, Padding margin)
    {
        checkBox.AutoSize = true;
        checkBox.ForeColor = PrimaryTextColor;
        checkBox.Margin = margin;
    }

    private static string KindLabel(EditorKind kind) => kind == EditorKind.Tuanjie ? "团结" : "Unity";

    private static string CompanionLabel(CompanionState state) => state.Health switch
    {
        CompanionHealth.Ready => "就绪",
        CompanionHealth.NotInstalled => "未安装",
        CompanionHealth.Starting => "启动中",
        CompanionHealth.Stale => "心跳过期",
        CompanionHealth.Incompatible => "版本不兼容",
        _ => "异常",
    };

    private static Color CompanionColor(CompanionHealth health) => health switch
    {
        CompanionHealth.Ready => SuccessColor,
        CompanionHealth.NotInstalled => SecondaryTextColor,
        CompanionHealth.Starting => InfoColor,
        CompanionHealth.Stale or CompanionHealth.Incompatible => WarningColor,
        _ => ErrorColor,
    };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}天 {duration.Hours:D2}:{duration.Minutes:D2}";
        }
        return $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    private static string LogLevelLabel(AppLogLevel level) => level switch
    {
        AppLogLevel.Warning => "警告",
        AppLogLevel.Error => "错误",
        _ => "信息",
    };

    private static Color LogLevelColor(AppLogLevel level) => level switch
    {
        AppLogLevel.Warning => WarningColor,
        AppLogLevel.Error => ErrorColor,
        _ => SecondaryTextColor,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.EntryWritten -= Logger_EntryWritten;
            _orchestrator.ProgressChanged -= Orchestrator_ProgressChanged;
            _refreshTimer.Dispose();
            _scheduleTimer.Dispose();
            _titleRenamerTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _applicationIcon?.Dispose();
            _lifetimeCancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
