using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace H5BuildTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class BuildConfig
{
    public string ProjectDir { get; set; } = "";
    public string HBuilderXDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public bool CleanOutput { get; set; } = true;
    public bool Minimize { get; set; } = true;
    public bool OpenFolder { get; set; } = false;
}

internal sealed class MainForm : Form
{
    private readonly string _appDir = AppContext.BaseDirectory;
    private readonly string _settingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "H5BuildTool");
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "H5BuildTool",
        "build-h5.config.json");

    private TextBox _projectBox = null!;
    private TextBox _hbuilderBox = null!;
    private TextBox _outputBox = null!;
    private CheckBox _cleanCheck = null!;
    private CheckBox _minimizeCheck = null!;
    private CheckBox _openFolderCheck = null!;
    private RichTextBox _logBox = null!;
    private Button _buildButton = null!;
    private Button _stopButton = null!;
    private Button _checkButton = null!;
    private Button _saveButton = null!;
    private Button _openOutputButton = null!;
    private Button _scanHBuilderButton = null!;

    private Process? _currentProcess;
    private StreamWriter? _logWriter;
    private string? _lastLogFile;

    public MainForm()
    {
        Text = "H5 免登录一键打包工具";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(940, 680);
        Size = new Size(1020, 740);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi();
        LoadConfigToUi();
        AppendLog("工具已启动。");
        AppendLog("说明：本工具调用 HBuilderX 内置 uniapp-cli 做 H5 本地编译，不调用 cli.exe publish，不需要登录 DCloud。");
        AppendLog("配置保存位置：" + _configPath);
        Shown += (_, _) => TryAutoScanHBuilderXOnStartup();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_currentProcess is { HasExited: false })
        {
            var result = MessageBox.Show(this, "打包进程仍在运行，是否强制停止并退出？", "确认退出", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            TryKillCurrentProcess();
        }

        _logWriter?.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var title = new Label
        {
            AutoSize = true,
            Text = "H5 免登录一键本地打包",
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 10),
        };
        root.Controls.Add(title, 0, 0);

        var pathPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        root.Controls.Add(pathPanel, 0, 1);

        _projectBox = AddPathRow(pathPanel, 0, "项目地址", "请选择 uni-app 项目根目录（包含 manifest.json / pages.json / main.js）", BrowseProject);
        _hbuilderBox = AddPathRow(pathPanel, 1, "HBuilderX", "请选择 HBuilderX 安装目录", BrowseHBuilder);
        _outputBox = AddPathRow(pathPanel, 2, "导出目录", "默认：<项目目录>\\dist\\build\\h5，可手动选择修改", BrowseOutput);
        _projectBox.Leave += (_, _) => FillDefaultOutputDirIfEmpty();

        var optionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 10),
        };
        root.Controls.Add(optionsPanel, 0, 2);

        _cleanCheck = new CheckBox { Text = "打包前清理旧产物", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 18, 6) };
        _minimizeCheck = new CheckBox { Text = "压缩产物", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 18, 6) };
        _openFolderCheck = new CheckBox { Text = "成功后打开输出目录", AutoSize = true, Margin = new Padding(0, 6, 18, 6) };
        optionsPanel.Controls.AddRange(new Control[] { _cleanCheck, _minimizeCheck, _openFolderCheck });

        _checkButton = new Button { Text = "检查配置", Width = 96, Height = 32, Margin = new Padding(0, 2, 8, 2) };
        _checkButton.Click += (_, _) => CheckConfig(showSuccessDialog: true);
        _scanHBuilderButton = new Button { Text = "自动扫描 HBuilderX", Width = 145, Height = 32, Margin = new Padding(0, 2, 8, 2) };
        _scanHBuilderButton.Click += (_, _) => ScanHBuilderXFromUi();
        _saveButton = new Button { Text = "保存配置", Width = 96, Height = 32, Margin = new Padding(0, 2, 8, 2) };
        _saveButton.Click += (_, _) => SaveConfigFromUi(showDialog: true);
        _buildButton = new Button { Text = "开始打包", Width = 110, Height = 32, Margin = new Padding(0, 2, 8, 2) };
        _buildButton.Click += async (_, _) => await StartBuildAsync();
        _stopButton = new Button { Text = "停止", Width = 80, Height = 32, Enabled = false, Margin = new Padding(0, 2, 8, 2) };
        _stopButton.Click += (_, _) => TryKillCurrentProcess();
        _openOutputButton = new Button { Text = "打开输出目录", Width = 120, Height = 32, Margin = new Padding(0, 2, 8, 2) };
        _openOutputButton.Click += (_, _) => OpenOutputFolder();
        optionsPanel.Controls.AddRange(new Control[] { _checkButton, _scanHBuilderButton, _saveButton, _buildButton, _stopButton, _openOutputButton });

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.FromArgb(230, 230, 230),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false,
        };
        root.Controls.Add(_logBox, 0, 3);
    }

    private TextBox AddPathRow(TableLayoutPanel panel, int row, string label, string placeholder, Action browseAction)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Padding = new Padding(0, 7, 0, 7),
        };
        panel.Controls.Add(labelControl, 0, row);

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = placeholder,
            Margin = new Padding(0, 5, 8, 5),
        };
        panel.Controls.Add(textBox, 1, row);

        var button = new Button
        {
            Text = "浏览...",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3),
        };
        button.Click += (_, _) => browseAction();
        panel.Controls.Add(button, 2, row);

        return textBox;
    }

    private void BrowseProject()
    {
        var oldProjectDir = FullPathOrTrimmed(_projectBox.Text);
        var oldDefaultOutput = GetDefaultOutputDir(oldProjectDir);
        var oldOutput = FullPathOrTrimmed(_outputBox.Text);

        var selected = BrowseFolder("选择 uni-app 项目目录", _projectBox.Text);
        if (selected is null) return;

        _projectBox.Text = selected;

        // 默认导出到项目目录下 dist\build\h5。
        // 如果当前导出目录为空，或仍是旧项目的默认导出目录，则跟随项目自动切换。
        if (string.IsNullOrWhiteSpace(oldOutput) || PathEquals(oldOutput, oldDefaultOutput))
        {
            _outputBox.Text = GetDefaultOutputDir(selected);
        }
    }

    private void FillDefaultOutputDirIfEmpty()
    {
        var projectDir = FullPathOrTrimmed(_projectBox.Text);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_outputBox.Text))
        {
            _outputBox.Text = GetDefaultOutputDir(projectDir);
        }
    }

    private void BrowseHBuilder()
    {
        var selected = BrowseFolder("选择 HBuilderX 安装目录", _hbuilderBox.Text);
        if (selected is not null) _hbuilderBox.Text = selected;
    }

    private void TryAutoScanHBuilderXOnStartup()
    {
        try
        {
            if (IsValidHBuilderXDir(_hbuilderBox.Text))
            {
                AppendLog("[OK] 已有 HBuilderX 配置有效：" + FullPathOrTrimmed(_hbuilderBox.Text));
                return;
            }

            AppendLog("正在自动扫描 HBuilderX 安装目录...");
            var candidates = ScanHBuilderXInstallDirs();
            if (candidates.Count == 0)
            {
                AppendLog("[WARN] 未自动发现 HBuilderX，请手动选择目录，或点击“自动扫描 HBuilderX”。");
                return;
            }

            _hbuilderBox.Text = candidates[0];
            AppendLog("[OK] 自动发现 HBuilderX：" + candidates[0]);
            if (candidates.Count > 1)
            {
                AppendLog($"[INFO] 共发现 {candidates.Count} 个候选目录，已自动选择第一个；如需更换可点击“自动扫描 HBuilderX”。");
            }
        }
        catch (Exception ex)
        {
            AppendLog("[WARN] 自动扫描 HBuilderX 失败：" + ex.Message);
        }
    }

    private void ScanHBuilderXFromUi()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _scanHBuilderButton.Enabled = false;
            AppendLog("正在扫描 HBuilderX 安装目录...");

            var candidates = ScanHBuilderXInstallDirs();
            if (candidates.Count == 0)
            {
                AppendLog("[WARN] 未扫描到 HBuilderX 安装目录。");
                MessageBox.Show(this,
                    "未扫描到 HBuilderX 安装目录。\n\n请手动点击 HBuilderX 行右侧“浏览...”选择 HBuilderX 根目录。",
                    "未找到",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string? selected;
            if (candidates.Count == 1)
            {
                selected = candidates[0];
            }
            else
            {
                selected = SelectHBuilderXCandidate(candidates);
                if (selected is null) return;
            }

            _hbuilderBox.Text = selected;
            AppendLog("[OK] 已选择 HBuilderX：" + selected);
        }
        catch (Exception ex)
        {
            AppendLog("[FAIL] 扫描 HBuilderX 失败：" + ex.Message);
            MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
            _scanHBuilderButton.Enabled = true;
        }
    }

    private void BrowseOutput()
    {
        var selected = BrowseFolder("选择 H5 输出目录", EffectiveOutputDir());
        if (selected is not null) _outputBox.Text = selected;
    }

    private string? BrowseFolder(string description, string initialDir)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(initialDir) ? initialDir : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            ShowNewFolderButton = true,
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void LoadConfigToUi()
    {
        var config = LoadConfig();
        _projectBox.Text = config.ProjectDir;
        _hbuilderBox.Text = config.HBuilderXDir;
        _outputBox.Text = string.IsNullOrWhiteSpace(config.OutputDir) && !string.IsNullOrWhiteSpace(config.ProjectDir)
            ? GetDefaultOutputDir(config.ProjectDir)
            : config.OutputDir;
        _cleanCheck.Checked = config.CleanOutput;
        _minimizeCheck.Checked = config.Minimize;
        _openFolderCheck.Checked = config.OpenFolder;
    }

    private BuildConfig LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath, Encoding.UTF8);
                var config = JsonSerializer.Deserialize<BuildConfig>(json, JsonOptions());
                if (config is not null) return config;
            }
        }
        catch (Exception ex)
        {
            AppendLog("[WARN] 配置读取失败，使用默认配置：" + ex.Message);
        }

        return new BuildConfig();
    }

    private void SaveConfigFromUi(bool showDialog)
    {
        var config = ReadConfigFromUi();
        var json = JsonSerializer.Serialize(config, JsonOptions());
        Directory.CreateDirectory(_settingsDir);
        File.WriteAllText(_configPath, json + Environment.NewLine, new UTF8Encoding(false));
        AppendLog("[OK] 配置已保存：" + _configPath);
        if (showDialog)
        {
            MessageBox.Show(this, "配置已保存。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private BuildConfig ReadConfigFromUi()
    {
        var projectDir = FullPathOrTrimmed(_projectBox.Text);
        var hbuilderDir = FullPathOrTrimmed(_hbuilderBox.Text);
        var outputDir = FullPathOrTrimmed(_outputBox.Text);
        var defaultOutputDir = GetDefaultOutputDir(projectDir);

        return new BuildConfig
        {
            ProjectDir = projectDir,
            HBuilderXDir = hbuilderDir,
            OutputDir = PathEquals(outputDir, defaultOutputDir) ? "" : outputDir,
            CleanOutput = _cleanCheck.Checked,
            Minimize = _minimizeCheck.Checked,
            OpenFolder = _openFolderCheck.Checked,
        };
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static List<string> ScanHBuilderXInstallDirs()
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var normalized = NormalizePotentialPath(path);
                if (string.IsNullOrWhiteSpace(normalized)) return;

                foreach (var candidate in EnumerateSelfAndParents(normalized, maxParents: 8))
                {
                    if (IsValidHBuilderXDir(candidate))
                    {
                        var full = FullPathOrTrimmed(candidate);
                        results[full] = full;
                        return;
                    }
                }
            }
            catch
            {
                // 扫描时忽略单个候选路径异常，继续检查其它位置。
            }
        }

        void AddDirectChildrenByName(string? parent)
        {
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return;

            foreach (var pattern in new[] { "HBuilderX*", "HBuilder*", "DCloud*" })
            {
                foreach (var dir in SafeEnumerateDirectories(parent, pattern))
                {
                    AddIfValid(dir);
                    AddIfValid(Path.Combine(dir, "HBuilderX"));
                }
            }
        }

        AddIfValid(Environment.GetEnvironmentVariable("HBUILDERX_HOME"));
        AddIfValid(Environment.GetEnvironmentVariable("HBUILDERX_DIR"));
        AddIfValid(Environment.GetEnvironmentVariable("HBuilderX"));

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddIfValid(entry);
        }

        AddRegistryCandidates(AddIfValid);

        var commonUserDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
        };

        foreach (var dir in commonUserDirs)
        {
            AddIfValid(Path.Combine(dir, "HBuilderX"));
            AddIfValid(Path.Combine(dir, "DCloud", "HBuilderX"));
            AddIfValid(Path.Combine(dir, "Programs", "HBuilderX"));
            AddDirectChildrenByName(dir);
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;

                var root = drive.RootDirectory.FullName;
                AddIfValid(Path.Combine(root, "HBuilderX"));
                AddIfValid(Path.Combine(root, "HBuilder"));
                AddIfValid(Path.Combine(root, "DCloud", "HBuilderX"));
                AddIfValid(Path.Combine(root, "Program Files", "HBuilderX"));
                AddIfValid(Path.Combine(root, "Program Files (x86)", "HBuilderX"));

                foreach (var parentName in new[]
                         {
                             "DCloud", "soft", "software", "Software", "tool", "tools", "Tool", "Tools",
                             "app", "apps", "App", "Apps", "dev", "Dev", "Develop", "Development",
                             "Program Files", "Program Files (x86)"
                         })
                {
                    var parent = Path.Combine(root, parentName);
                    AddIfValid(Path.Combine(parent, "HBuilderX"));
                    AddDirectChildrenByName(parent);
                }

                AddDirectChildrenByName(root);
            }
            catch
            {
                // 某些盘符可能无法访问，忽略并继续。
            }
        }

        return results.Keys
            .OrderByDescending(p => File.Exists(Path.Combine(p, "HBuilderX.exe")))
            .ThenBy(p => p.Length)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsValidHBuilderXDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;

        try
        {
            var full = FullPathOrTrimmed(dir);
            return Directory.Exists(full)
                   && File.Exists(Path.Combine(full, "plugins", "node", "node.exe"))
                   && File.Exists(Path.Combine(full, "plugins", "uniapp-cli", "bin", "uniapp-cli.js"));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string path, int maxParents)
    {
        var normalized = NormalizePotentialPath(path);
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        var current = normalized;
        for (var i = 0; i <= maxParents && !string.IsNullOrWhiteSpace(current); i++)
        {
            yield return current;

            DirectoryInfo? parent;
            try
            {
                parent = Directory.GetParent(current);
            }
            catch
            {
                yield break;
            }

            if (parent is null) yield break;
            current = parent.FullName;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string parent, string pattern)
    {
        try
        {
            if (!Directory.Exists(parent)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(parent, pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizePotentialPath(string raw)
    {
        raw = Environment.ExpandEnvironmentVariables((raw ?? "").Trim());
        if (string.IsNullOrWhiteSpace(raw)) return "";

        if (raw.StartsWith("\"", StringComparison.Ordinal))
        {
            var endQuote = raw.IndexOf('"', 1);
            if (endQuote > 1)
            {
                raw = raw.Substring(1, endQuote - 1);
            }
        }

        var exeIndex = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            raw = raw.Substring(0, exeIndex + 4);
        }
        else
        {
            var jsIndex = raw.IndexOf(".js", StringComparison.OrdinalIgnoreCase);
            if (jsIndex >= 0)
            {
                raw = raw.Substring(0, jsIndex + 3);
            }
            else
            {
                var commaIndex = raw.IndexOf(',');
                if (commaIndex > 0) raw = raw.Substring(0, commaIndex);
            }
        }

        raw = raw.Trim().Trim('"');
        if (File.Exists(raw))
        {
            return Path.GetDirectoryName(FullPathOrTrimmed(raw)) ?? "";
        }

        return FullPathOrTrimmed(raw);
    }

    private static void AddRegistryCandidates(Action<string?> addCandidate)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;

                    foreach (var subKeyName in uninstall.GetSubKeyNames())
                    {
                        using var subKey = uninstall.OpenSubKey(subKeyName);
                        if (subKey is null) continue;

                        var displayName = subKey.GetValue("DisplayName")?.ToString() ?? "";
                        if (displayName.IndexOf("HBuilderX", StringComparison.OrdinalIgnoreCase) < 0
                            && displayName.IndexOf("DCloud", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        addCandidate(subKey.GetValue("InstallLocation")?.ToString());
                        addCandidate(subKey.GetValue("DisplayIcon")?.ToString());
                        addCandidate(subKey.GetValue("UninstallString")?.ToString());
                    }
                }
                catch
                {
                    // 无权限或注册表视图不存在时忽略。
                }
            }
        }
    }

    private string? SelectHBuilderXCandidate(IReadOnlyList<string> candidates)
    {
        using var form = new Form
        {
            Text = "选择 HBuilderX 安装目录",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(720, 360),
            MinimumSize = new Size(620, 300),
            Font = Font,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"扫描到 {candidates.Count} 个 HBuilderX 候选目录，请选择一个：",
            Padding = new Padding(0, 0, 0, 8),
        }, 0, 0);

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };
        foreach (var candidate in candidates)
        {
            listBox.Items.Add(candidate);
        }
        if (listBox.Items.Count > 0) listBox.SelectedIndex = 0;
        root.Controls.Add(listBox, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0),
        };
        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 2);

        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        listBox.DoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is not null) form.DialogResult = DialogResult.OK;
        };

        return form.ShowDialog(this) == DialogResult.OK
            ? listBox.SelectedItem?.ToString()
            : null;
    }

    private bool CheckConfig(bool showSuccessDialog)
    {
        try
        {
            var paths = GetBuildPaths();

            RequireNotEmpty(paths.ProjectDir, "请选择项目地址。");
            RequireNotEmpty(paths.HBuilderXDir, "请选择 HBuilderX 目录。");
            RequireNotEmpty(paths.OutputDir, "请选择导出目录。");

            RequireDir(paths.ProjectDir, "uni-app 项目目录");
            RequireFile(Path.Combine(paths.ProjectDir, "manifest.json"), "manifest.json");
            RequireFile(Path.Combine(paths.ProjectDir, "pages.json"), "pages.json");
            RequireFile(Path.Combine(paths.ProjectDir, "main.js"), "main.js");

            RequireDir(paths.HBuilderXDir, "HBuilderX 目录");
            RequireFile(paths.NodeExe, "HBuilderX Node");
            RequireDir(paths.UniCliRoot, "HBuilderX uniapp-cli 目录");
            RequireFile(paths.UniCliEntry, "uniapp-cli.js");

            var version = RunNodeVersion(paths.NodeExe);
            AppendLog("[OK] 配置检查通过。HBuilderX Node：" + version);
            AppendLog("     项目目录：" + paths.ProjectDir);
            AppendLog("     输出目录：" + paths.OutputDir);

            if (showSuccessDialog)
            {
                MessageBox.Show(this, "配置检查通过，可以开始打包。", "检查通过", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("[FAIL] 配置检查失败：" + ex.Message);
            MessageBox.Show(this, ex.Message, "配置检查失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task StartBuildAsync()
    {
        if (_currentProcess is { HasExited: false })
        {
            MessageBox.Show(this, "已有打包任务正在运行。", "请稍候", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!CheckConfig(showSuccessDialog: false)) return;

        SaveConfigFromUi(showDialog: false);
        var paths = GetBuildPaths();

        SetBusy(true);

        try
        {
            Directory.CreateDirectory(Path.Combine(_appDir, "logs"));
            _lastLogFile = Path.Combine(_appDir, "logs", $"build-h5-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logWriter?.Dispose();
            _logWriter = new StreamWriter(_lastLogFile, append: false, new UTF8Encoding(false)) { AutoFlush = true };

            AppendLog("");
            AppendLog("=== 开始 H5 本地打包 ===");
            AppendLog("项目目录：" + paths.ProjectDir);
            AppendLog("输出目录：" + paths.OutputDir);
            AppendLog("日志文件：" + _lastLogFile);

            if (_cleanCheck.Checked && Directory.Exists(paths.OutputDir))
            {
                AssertSafeCleanDirectory(paths.OutputDir, paths.ProjectDir, _appDir);
                AppendLog("[WARN] 清理旧输出目录：" + paths.OutputDir);
                Directory.Delete(paths.OutputDir, recursive: true);
            }

            Directory.CreateDirectory(paths.OutputDir);

            var psi = new ProcessStartInfo
            {
                FileName = paths.NodeExe,
                Arguments = Quote(paths.UniCliEntry),
                WorkingDirectory = paths.UniCliRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            psi.Environment["UNI_INPUT_DIR"] = paths.ProjectDir;
            psi.Environment["UNI_OUTPUT_DIR"] = paths.OutputDir;
            psi.Environment["UNI_PLATFORM"] = "h5";
            psi.Environment["NODE_ENV"] = "production";
            psi.Environment["UNI_MINIMIZE"] = _minimizeCheck.Checked ? "true" : "false";

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _currentProcess = process;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) AppendLog(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) AppendLog(e.Data);
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动打包进程。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var exitCode = process.ExitCode;
            _currentProcess = null;

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"构建失败，退出码：{exitCode}。日志：{_lastLogFile}");
            }

            var indexHtml = Path.Combine(paths.OutputDir, "index.html");
            if (!File.Exists(indexHtml))
            {
                throw new FileNotFoundException("构建命令已结束，但输出目录未找到 index.html：" + indexHtml);
            }

            AppendLog("");
            AppendLog("[OK] H5 打包完成。");
            AppendLog("输出目录：" + paths.OutputDir);

            if (_openFolderCheck.Checked)
            {
                OpenFolder(paths.OutputDir);
            }

            MessageBox.Show(this, "H5 打包完成。", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("");
            AppendLog("[FAIL] " + ex.Message);
            MessageBox.Show(this, ex.Message, "打包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _logWriter?.Dispose();
            _logWriter = null;
            _currentProcess = null;
            SetBusy(false);
        }
    }

    private BuildPaths GetBuildPaths()
    {
        var projectDir = FullPathOrTrimmed(_projectBox.Text);
        var hbuilderDir = FullPathOrTrimmed(_hbuilderBox.Text);
        var outputDir = EffectiveOutputDir();

        return new BuildPaths
        {
            ProjectDir = projectDir,
            HBuilderXDir = hbuilderDir,
            OutputDir = outputDir,
            NodeExe = Path.Combine(hbuilderDir, "plugins", "node", "node.exe"),
            UniCliRoot = Path.Combine(hbuilderDir, "plugins", "uniapp-cli"),
            UniCliEntry = Path.Combine(hbuilderDir, "plugins", "uniapp-cli", "bin", "uniapp-cli.js"),
        };
    }

    private string EffectiveOutputDir()
    {
        var outputDir = FullPathOrTrimmed(_outputBox.Text);
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            return outputDir;
        }

        var projectDir = FullPathOrTrimmed(_projectBox.Text);
        return GetDefaultOutputDir(projectDir);
    }

    private static string GetDefaultOutputDir(string projectDir)
    {
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return "";
        }

        return Path.Combine(FullPathOrTrimmed(projectDir), "dist", "build", "h5");
    }

    private static string FullPathOrTrimmed(string value)
    {
        value = Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(value)) return "";
        return Path.GetFullPath(value);
    }

    private static void RequireFile(string path, string name)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{name} 不存在：{path}");
    }

    private static void RequireNotEmpty(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
    }

    private static void RequireDir(string path, string name)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{name} 不存在：{path}");
    }

    private static string RunNodeVersion(string nodeExe)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = nodeExe,
            Arguments = "-v",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });
        if (p is null) return "unknown";
        p.WaitForExit(3000);
        return p.StandardOutput.ReadToEnd().Trim();
    }

    private static void AssertSafeCleanDirectory(string targetDir, string projectDir, string appDir)
    {
        var full = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(full)) throw new InvalidOperationException("输出目录为空，拒绝清理。");
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("输出目录是磁盘根目录，拒绝清理：" + full);
        if (full.Length < 10) throw new InvalidOperationException("输出目录过短，拒绝清理：" + full);
        if (PathEquals(full, projectDir)) throw new InvalidOperationException("导出目录不能是项目根目录，拒绝清理：" + full);
        if (PathEquals(full, appDir)) throw new InvalidOperationException("导出目录不能是工具所在目录，拒绝清理：" + full);

        if (!IsSubPath(full, projectDir) && !IsSubPath(full, appDir))
        {
            throw new InvalidOperationException("输出目录不在项目目录或工具目录下，拒绝自动清理：" + full + Environment.NewLine + "可取消“打包前清理旧产物”后重试。");
        }
    }

    private static bool PathEquals(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var left = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var right = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSubPath(string childPath, string parentPath)
    {
        if (string.IsNullOrWhiteSpace(childPath) || string.IsNullOrWhiteSpace(parentPath)) return false;
        var child = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private void SetBusy(bool busy)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetBusy(busy)));
            return;
        }

        _projectBox.Enabled = !busy;
        _hbuilderBox.Enabled = !busy;
        _outputBox.Enabled = !busy;
        _cleanCheck.Enabled = !busy;
        _minimizeCheck.Enabled = !busy;
        _openFolderCheck.Enabled = !busy;
        _checkButton.Enabled = !busy;
        _scanHBuilderButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _buildButton.Enabled = !busy;
        _openOutputButton.Enabled = !busy;
        _stopButton.Enabled = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void TryKillCurrentProcess()
    {
        try
        {
            if (_currentProcess is { HasExited: false })
            {
                AppendLog("[WARN] 正在停止打包进程...");
                _currentProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            AppendLog("[WARN] 停止失败：" + ex.Message);
        }
    }

    private void OpenOutputFolder()
    {
        var dir = EffectiveOutputDir();
        if (!Directory.Exists(dir))
        {
            MessageBox.Show(this, "输出目录不存在：" + dir, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        OpenFolder(dir);
    }

    private static void OpenFolder(string dir)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
        });
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logBox.AppendText(line + Environment.NewLine);
        _logBox.ScrollToCaret();
        _logWriter?.WriteLine(line);
    }

    private sealed class BuildPaths
    {
        public string ProjectDir { get; init; } = "";
        public string HBuilderXDir { get; init; } = "";
        public string OutputDir { get; init; } = "";
        public string NodeExe { get; init; } = "";
        public string UniCliRoot { get; init; } = "";
        public string UniCliEntry { get; init; } = "";
    }
}
