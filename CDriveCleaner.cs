using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CDriveCleaner
{
    internal sealed class CleanupTarget
    {
        public string Category;
        public string Name;
        public string Path;
        public string Pattern;
        public bool Recursive;
        public string Risk;
        public bool Recommended;
        public string Note;
        public long Bytes;
        public int Files;
    }

    internal sealed class TargetResult
    {
        public CleanupTarget Target;
        public long Bytes;
        public int Files;
        public int Failed;
    }

    internal sealed class WorkResult
    {
        public readonly List<TargetResult> Results = new List<TargetResult>();
        public long Bytes;
        public int Files;
        public int Failed;
    }

    internal sealed class ProgressInfo
    {
        public string Message;
        public int Percent;
    }

    internal sealed class CleanerForm : Form
    {
        private readonly Color Ink = Color.FromArgb(35, 41, 47);
        private readonly Color Muted = Color.FromArgb(96, 105, 113);
        private readonly Color Accent = Color.FromArgb(0, 120, 110);
        private readonly Color AccentDark = Color.FromArgb(0, 92, 85);
        private readonly Color Warning = Color.FromArgb(174, 99, 0);
        private readonly Color Surface = Color.White;
        private readonly Color Canvas = Color.FromArgb(245, 247, 248);
        private readonly Color Line = Color.FromArgb(216, 222, 226);

        private readonly List<CleanupTarget> targets = new List<CleanupTarget>();
        private readonly BackgroundWorker worker = new BackgroundWorker();
        private readonly ToolTip toolTip = new ToolTip();

        private Label driveLabel;
        private Label statusLabel;
        private Label totalLabel;
        private Label selectionLabel;
        private DataGridView grid;
        private NumericUpDown ageDays;
        private Button scanButton;
        private Button cleanButton;
        private Button recommendedButton;
        private Button storageButton;
        private Button componentButton;
        private Button hibernateButton;
        private Button restoreHibernateButton;
        private Button recycleButton;
        private ProgressBar progressBar;
        private TextBox logBox;
        private bool busy;
        private bool scanCurrent;
        private bool cleanMode;
        private int scannedAgeDays;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        public CleanerForm()
        {
            Text = "C 盘清理助手";
            Icon = SystemIcons.Shield;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 680);
            Size = new Size(1160, 760);
            BackColor = Canvas;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            LoadTargets();
            RefreshDriveSummary();
            RefreshHibernateState();
            AppendLog("准备就绪。先扫描，再核对路径，最后清理所选项目。");

            worker.WorkerReportsProgress = true;
            worker.DoWork += WorkerDoWork;
            worker.ProgressChanged += WorkerProgressChanged;
            worker.RunWorkerCompleted += WorkerCompleted;
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(22, 18, 22, 18);
            root.BackColor = Canvas;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Canvas;
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.AutoSize = true;
            title.Text = "C 盘清理助手";
            title.Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Ink;
            title.Location = new Point(0, 3);
            header.Controls.Add(title);

            driveLabel = new Label();
            driveLabel.AutoSize = true;
            driveLabel.Text = "正在读取磁盘空间...";
            driveLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            driveLabel.ForeColor = Muted;
            driveLabel.Location = new Point(3, 48);
            header.Controls.Add(driveLabel);

            Label safety = new Label();
            safety.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            safety.AutoSize = false;
            safety.TextAlign = ContentAlignment.MiddleRight;
            safety.Text = "不会扫描或删除 System32、WinSxS、Installer、程序目录和个人文档";
            safety.ForeColor = Muted;
            safety.Location = new Point(470, 18);
            safety.Size = new Size(620, 38);
            header.Controls.Add(safety);
            header.Resize += delegate { safety.Left = Math.Max(360, header.ClientSize.Width - safety.Width); };

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.FlowDirection = FlowDirection.LeftToRight;
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(0, 6, 0, 6);
            toolbar.BackColor = Surface;
            root.Controls.Add(toolbar, 0, 1);

            scanButton = MakeButton("扫描", true, 88);
            scanButton.Click += delegate { StartScan(); };
            toolbar.Controls.Add(scanButton);

            recommendedButton = MakeButton("选择推荐项", false, 112);
            recommendedButton.Click += delegate { SelectRecommended(); };
            toolbar.Controls.Add(recommendedButton);

            cleanButton = MakeButton("清理所选", true, 104);
            cleanButton.Enabled = false;
            cleanButton.Click += delegate { StartClean(); };
            toolbar.Controls.Add(cleanButton);

            Label ageLabel = new Label();
            ageLabel.AutoSize = true;
            ageLabel.Text = "仅处理超过";
            ageLabel.ForeColor = Ink;
            ageLabel.Margin = new Padding(18, 9, 4, 0);
            toolbar.Controls.Add(ageLabel);

            ageDays = new NumericUpDown();
            ageDays.Minimum = 0;
            ageDays.Maximum = 90;
            ageDays.Value = 3;
            ageDays.Width = 58;
            ageDays.Margin = new Padding(0, 5, 4, 0);
            ageDays.ValueChanged += delegate
            {
                if (scanCurrent && (int)ageDays.Value != scannedAgeDays)
                {
                    scanCurrent = false;
                    cleanButton.Enabled = false;
                    statusLabel.Text = "保留天数已改变，请重新扫描";
                }
            };
            toolbar.Controls.Add(ageDays);

            Label dayLabel = new Label();
            dayLabel.AutoSize = true;
            dayLabel.Text = "天的文件";
            dayLabel.ForeColor = Ink;
            dayLabel.Margin = new Padding(0, 9, 12, 0);
            toolbar.Controls.Add(dayLabel);

            selectionLabel = new Label();
            selectionLabel.AutoSize = true;
            selectionLabel.ForeColor = Muted;
            selectionLabel.Margin = new Padding(10, 9, 0, 0);
            selectionLabel.Text = "尚未扫描";
            toolbar.Controls.Add(selectionLabel);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoGenerateColumns = false;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 239, 241);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 239, 236);
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            grid.RowTemplate.Height = 34;
            grid.CellValueChanged += GridCellValueChanged;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellDoubleClick += GridCellDoubleClick;
            root.Controls.Add(grid, 0, 2);

            DataGridViewCheckBoxColumn check = new DataGridViewCheckBoxColumn();
            check.Name = "Pick";
            check.HeaderText = "选择";
            check.Width = 56;
            grid.Columns.Add(check);
            grid.Columns.Add(MakeTextColumn("Category", "来源", 88, false));
            grid.Columns.Add(MakeTextColumn("Name", "项目", 180, false));
            grid.Columns.Add(MakeTextColumn("Size", "可释放", 92, false));
            grid.Columns.Add(MakeTextColumn("Files", "文件数", 72, false));
            grid.Columns.Add(MakeTextColumn("Risk", "风险", 64, false));
            grid.Columns.Add(MakeTextColumn("Path", "路径", 260, true));
            grid.Columns.Add(MakeTextColumn("Note", "说明", 240, true));

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            actions.BackColor = Canvas;
            root.Controls.Add(actions, 0, 3);

            Label toolsLabel = new Label();
            toolsLabel.AutoSize = true;
            toolsLabel.Text = "Windows 系统工具";
            toolsLabel.Font = new Font(Font, FontStyle.Bold);
            toolsLabel.ForeColor = Ink;
            toolsLabel.Location = new Point(0, 10);
            actions.Controls.Add(toolsLabel);

            FlowLayoutPanel systemTools = new FlowLayoutPanel();
            systemTools.Location = new Point(0, 34);
            systemTools.Size = new Size(1080, 44);
            systemTools.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            systemTools.WrapContents = false;
            actions.Controls.Add(systemTools);

            storageButton = MakeButton("打开存储设置", false, 120);
            storageButton.Click += delegate { OpenUri("ms-settings:storagesense"); };
            systemTools.Controls.Add(storageButton);

            recycleButton = MakeButton("清空回收站", false, 108);
            recycleButton.Click += delegate { EmptyRecycleBin(); };
            systemTools.Controls.Add(recycleButton);

            componentButton = MakeButton("清理组件存储", false, 124);
            componentButton.Click += delegate { RunComponentCleanup(); };
            toolTip.SetToolTip(componentButton, "调用 Windows DISM 的 StartComponentCleanup，不会手删 WinSxS。");
            systemTools.Controls.Add(componentButton);

            hibernateButton = MakeButton("关闭休眠", false, 170);
            hibernateButton.Click += delegate { DisableHibernate(); };
            systemTools.Controls.Add(hibernateButton);

            restoreHibernateButton = MakeButton("恢复休眠", false, 96);
            restoreHibernateButton.Click += delegate { EnableHibernate(); };
            systemTools.Controls.Add(restoreHibernateButton);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Surface;
            root.Controls.Add(footer, 0, 4);

            progressBar = new ProgressBar();
            progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            progressBar.Location = new Point(14, 14);
            progressBar.Size = new Size(760, 8);
            progressBar.Style = ProgressBarStyle.Continuous;
            footer.Controls.Add(progressBar);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            statusLabel.TextAlign = ContentAlignment.MiddleRight;
            statusLabel.ForeColor = Muted;
            statusLabel.Location = new Point(780, 5);
            statusLabel.Size = new Size(290, 26);
            statusLabel.Text = "就绪";
            footer.Controls.Add(statusLabel);

            totalLabel = new Label();
            totalLabel.AutoSize = true;
            totalLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            totalLabel.ForeColor = AccentDark;
            totalLabel.Location = new Point(14, 34);
            totalLabel.Text = "预计可释放 0 B";
            footer.Controls.Add(totalLabel);

            logBox = new TextBox();
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Location = new Point(14, 62);
            logBox.Size = new Size(1056, 84);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BorderStyle = BorderStyle.FixedSingle;
            logBox.BackColor = Color.FromArgb(250, 251, 252);
            logBox.ForeColor = Muted;
            footer.Controls.Add(logBox);

            footer.Resize += delegate
            {
                progressBar.Width = Math.Max(100, footer.ClientSize.Width - 330);
                statusLabel.Left = Math.Max(100, footer.ClientSize.Width - 304);
                logBox.Width = Math.Max(100, footer.ClientSize.Width - 28);
                logBox.Height = Math.Max(38, footer.ClientSize.Height - 72);
            };
        }

        private Button MakeButton(string text, bool primary, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 34;
            button.Margin = new Padding(6, 2, 2, 2);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Line;
            button.BackColor = primary ? Accent : Surface;
            button.ForeColor = primary ? Color.White : Ink;
            button.Cursor = Cursors.Hand;
            button.MouseEnter += delegate { if (button.Enabled) button.BackColor = primary ? AccentDark : Color.FromArgb(238, 242, 243); };
            button.MouseLeave += delegate { button.BackColor = primary ? Accent : Surface; };
            return button;
        }

        private DataGridViewTextBoxColumn MakeTextColumn(string name, string header, int width, bool fill)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.Width = width;
            column.ReadOnly = true;
            if (fill)
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.MinimumWidth = width;
            }
            return column;
        }

        private void LoadTargets()
        {
            targets.Clear();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            AddTarget("Windows", "用户临时文件", Path.Combine(local, "Temp"), "*", true, "低", true,
                "安装器与应用产生的临时文件；正在使用的文件会跳过。");
            AddTarget("Windows", "DirectX 着色器缓存", Path.Combine(local, "D3DSCache"), "*", true, "低", true,
                "显卡会按需重新生成。");
            AddTarget("Windows", "崩溃转储", Path.Combine(local, "CrashDumps"), "*", true, "低", true,
                "仅用于排查已经发生的程序崩溃。");
            AddTarget("Windows", "错误报告存档", Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"), "*", true, "低", true,
                "Windows 错误报告历史。");
            AddTarget("Windows", "错误报告队列", Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"), "*", true, "低", true,
                "尚未发送的错误报告；不影响系统运行。");
            AddTarget("Windows", "缩略图缓存", Path.Combine(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false, "低", false,
                "资源管理器会重新生成；清理后首次打开图片文件夹会稍慢。");
            AddTarget("Windows", "系统临时文件", Path.Combine(windows, "Temp"), "*", true, "低", false,
                "可能需要管理员权限；被系统占用的文件会跳过。");

            AddTarget("微信", "日志", Path.Combine(roaming, "Tencent", "xwechat", "log"), "*", true, "低", true,
                "建议先退出微信；不包含聊天记录和下载文件。");
            AddTarget("微信", "崩溃信息", Path.Combine(roaming, "Tencent", "xwechat", "crashinfo"), "*", true, "低", true,
                "用于故障诊断，不包含聊天记录。");

            AddBrowserTargets("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"));
            AddBrowserTargets("Chrome", Path.Combine(local, "Google", "Chrome", "User Data"));

            AddTarget("开发工具", "npm 下载缓存", Path.Combine(local, "npm-cache"), "*", true, "可重建", false,
                "以后安装依赖时会重新下载。");
            AddTarget("开发工具", "NuGet 下载缓存", Path.Combine(local, "NuGet", "v3-cache"), "*", true, "可重建", false,
                "以后还原 .NET 依赖时会重新下载。");

            foreach (CleanupTarget target in targets)
            {
                int rowIndex = grid.Rows.Add(target.Recommended, target.Category, target.Name, "待扫描", "-", target.Risk, target.Path, target.Note);
                grid.Rows[rowIndex].Tag = target;
                if (!Directory.Exists(target.Path))
                {
                    grid.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.FromArgb(145, 151, 157);
                    grid.Rows[rowIndex].Cells[0].Value = false;
                    grid.Rows[rowIndex].Cells[0].ReadOnly = true;
                    grid.Rows[rowIndex].Cells[3].Value = "不存在";
                }
                if (target.Risk == "可重建")
                    grid.Rows[rowIndex].Cells[5].Style.ForeColor = Warning;
            }
            UpdateSelectionSummary();
        }

        private void AddBrowserTargets(string browserName, string userData)
        {
            if (!Directory.Exists(userData))
                return;

            string[] profiles;
            try { profiles = Directory.GetDirectories(userData); }
            catch { return; }

            foreach (string profile in profiles)
            {
                string profileName = Path.GetFileName(profile);
                if (!string.Equals(profileName, "Default", StringComparison.OrdinalIgnoreCase) &&
                    !profileName.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                    continue;

                string display = browserName + " " + profileName;
                AddTarget("浏览器", display + " 网页缓存", Path.Combine(profile, "Cache", "Cache_Data"), "*", true, "低", true,
                    "建议先关闭浏览器；登录状态、密码和收藏夹不会删除。");
                AddTarget("浏览器", display + " 代码缓存", Path.Combine(profile, "Code Cache"), "*", true, "低", true,
                    "浏览器会按需重新生成。");
                AddTarget("浏览器", display + " GPU 缓存", Path.Combine(profile, "GPUCache"), "*", true, "低", true,
                    "浏览器会按需重新生成。");
            }
        }

        private void AddTarget(string category, string name, string path, string pattern, bool recursive, string risk, bool recommended, string note)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string full;
            try { full = Path.GetFullPath(path); }
            catch { return; }

            foreach (CleanupTarget existing in targets)
            {
                if (string.Equals(existing.Path, full, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            CleanupTarget target = new CleanupTarget();
            target.Category = category;
            target.Name = name;
            target.Path = full;
            target.Pattern = pattern;
            target.Recursive = recursive;
            target.Risk = risk;
            target.Recommended = recommended;
            target.Note = note;
            targets.Add(target);
        }

        private void StartScan()
        {
            if (busy)
                return;

            scannedAgeDays = (int)ageDays.Value;
            scanCurrent = false;
            cleanMode = false;
            SetBusy(true, "正在扫描...");
            AppendLog("开始扫描：仅统计修改时间超过 " + scannedAgeDays.ToString(CultureInfo.InvariantCulture) + " 天的文件。");
            worker.RunWorkerAsync(new List<CleanupTarget>(targets));
        }

        private void StartClean()
        {
            if (busy || !scanCurrent)
                return;

            List<CleanupTarget> selected = GetSelectedTargets();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "请先选择至少一个有内容的项目。", "没有选择项目", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long total = 0;
            StringBuilder names = new StringBuilder();
            foreach (CleanupTarget target in selected)
            {
                total += target.Bytes;
                if (names.Length < 260)
                    names.AppendLine("• " + target.Category + " / " + target.Name);
            }

            string prompt = "将永久删除以下缓存或日志，不经过回收站：\r\n\r\n" + names.ToString() +
                "\r\n预计释放：" + FormatBytes(total) +
                "\r\n仅处理超过 " + scannedAgeDays.ToString(CultureInfo.InvariantCulture) + " 天的文件。被占用或无权限的文件会跳过。";
            DialogResult answer = MessageBox.Show(this, prompt, "确认清理所选项目", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK)
                return;

            cleanMode = true;
            SetBusy(true, "正在清理...");
            AppendLog("开始清理 " + selected.Count.ToString(CultureInfo.InvariantCulture) + " 个项目。");
            worker.RunWorkerAsync(selected);
        }

        private void WorkerDoWork(object sender, DoWorkEventArgs e)
        {
            List<CleanupTarget> workTargets = (List<CleanupTarget>)e.Argument;
            WorkResult all = new WorkResult();
            DateTime cutoff = DateTime.Now.AddDays(-scannedAgeDays);
            int count = Math.Max(1, workTargets.Count);

            for (int i = 0; i < workTargets.Count; i++)
            {
                CleanupTarget target = workTargets[i];
                int percent = (int)((i * 100.0) / count);
                worker.ReportProgress(percent, new ProgressInfo { Percent = percent, Message = (cleanMode ? "正在清理：" : "正在扫描：") + target.Name });

                TargetResult result;
                if (cleanMode)
                    result = CleanTarget(target, cutoff);
                else
                    result = ScanTarget(target, cutoff);

                all.Results.Add(result);
                all.Bytes += result.Bytes;
                all.Files += result.Files;
                all.Failed += result.Failed;
            }
            e.Result = all;
        }

        private void WorkerProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ProgressInfo info = e.UserState as ProgressInfo;
            progressBar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
            if (info != null)
                statusLabel.Text = info.Message;
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false, "就绪");
            progressBar.Value = 100;

            if (e.Error != null)
            {
                AppendLog("操作中断：" + e.Error.Message);
                MessageBox.Show(this, e.Error.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            WorkResult result = (WorkResult)e.Result;
            if (!cleanMode)
            {
                foreach (TargetResult item in result.Results)
                {
                    item.Target.Bytes = item.Bytes;
                    item.Target.Files = item.Files;
                }
                RefreshGridResults();
                scanCurrent = true;
                cleanButton.Enabled = true;
                AppendLog("扫描完成：发现 " + result.Files.ToString("N0") + " 个可清理文件，共 " + FormatBytes(result.Bytes) + "。");
                statusLabel.Text = "扫描完成";
                UpdateSelectionSummary();
            }
            else
            {
                AppendLog("清理完成：已删除 " + result.Files.ToString("N0") + " 个文件，释放 " + FormatBytes(result.Bytes) +
                    (result.Failed > 0 ? "；跳过 " + result.Failed.ToString("N0") + " 个占用或无权限文件。" : "。"));
                MessageBox.Show(this,
                    "已释放 " + FormatBytes(result.Bytes) + "\r\n删除文件：" + result.Files.ToString("N0") +
                    (result.Failed > 0 ? "\r\n跳过文件：" + result.Failed.ToString("N0") : string.Empty),
                    "清理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                scanCurrent = false;
                cleanButton.Enabled = false;
                RefreshDriveSummary();
                StartScan();
            }
        }

        private TargetResult ScanTarget(CleanupTarget target, DateTime cutoff)
        {
            TargetResult result = new TargetResult();
            result.Target = target;
            if (!Directory.Exists(target.Path) || !IsAllowedTarget(target.Path))
                return result;

            VisitFiles(target.Path, target.Pattern, target.Recursive, cutoff, delegate(FileInfo file)
            {
                result.Files++;
                try { result.Bytes += file.Length; }
                catch { result.Failed++; }
            }, delegate { result.Failed++; });
            return result;
        }

        private TargetResult CleanTarget(CleanupTarget target, DateTime cutoff)
        {
            TargetResult result = new TargetResult();
            result.Target = target;
            if (!Directory.Exists(target.Path) || !IsAllowedTarget(target.Path))
                return result;

            VisitFiles(target.Path, target.Pattern, target.Recursive, cutoff, delegate(FileInfo file)
            {
                long size = 0;
                try
                {
                    size = file.Length;
                    if (file.IsReadOnly)
                        file.IsReadOnly = false;
                    file.Delete();
                    result.Files++;
                    result.Bytes += size;
                }
                catch { result.Failed++; }
            }, delegate { result.Failed++; });

            if (target.Recursive && target.Pattern == "*")
                RemoveEmptyDirectories(target.Path);
            return result;
        }

        private void VisitFiles(string root, string pattern, bool recursive, DateTime cutoff, Action<FileInfo> onFile, Action onFailure)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                DirectoryInfo directory;
                try
                {
                    directory = new DirectoryInfo(current);
                    if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    onFailure();
                    continue;
                }

                FileInfo[] files;
                try { files = directory.GetFiles(pattern, SearchOption.TopDirectoryOnly); }
                catch
                {
                    onFailure();
                    files = new FileInfo[0];
                }

                foreach (FileInfo file in files)
                {
                    try
                    {
                        if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && file.LastWriteTime < cutoff)
                            onFile(file);
                    }
                    catch { onFailure(); }
                }

                if (!recursive)
                    continue;

                DirectoryInfo[] children;
                try { children = directory.GetDirectories(); }
                catch
                {
                    onFailure();
                    continue;
                }

                foreach (DirectoryInfo child in children)
                {
                    try
                    {
                        if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Push(child.FullName);
                    }
                    catch { onFailure(); }
                }
            }
        }

        private void RemoveEmptyDirectories(string root)
        {
            List<string> directories = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                DirectoryInfo directory;
                try
                {
                    directory = new DirectoryInfo(current);
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    foreach (DirectoryInfo child in directory.GetDirectories())
                    {
                        if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            directories.Add(child.FullName);
                            pending.Push(child.FullName);
                        }
                    }
                }
                catch { }
            }

            directories.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string directory in directories)
            {
                try
                {
                    if (Directory.GetFileSystemEntries(directory).Length == 0)
                        Directory.Delete(directory, false);
                }
                catch { }
            }
        }

        private bool IsAllowedTarget(string path)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            return IsWithin(path, local) || IsWithin(path, roaming) || IsWithin(path, windowsTemp);
        }

        private bool IsWithin(string path, string root)
        {
            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void RefreshGridResults()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                CleanupTarget target = row.Tag as CleanupTarget;
                if (target == null)
                    continue;
                row.Cells[3].Value = Directory.Exists(target.Path) ? FormatBytes(target.Bytes) : "不存在";
                row.Cells[4].Value = Directory.Exists(target.Path) ? target.Files.ToString("N0") : "-";
                if (target.Bytes == 0)
                    row.Cells[0].Value = false;
                row.Cells[0].ReadOnly = target.Bytes == 0;
            }
        }

        private void SelectRecommended()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                CleanupTarget target = row.Tag as CleanupTarget;
                if (target != null && Directory.Exists(target.Path) && (!scanCurrent || target.Bytes > 0))
                    row.Cells[0].Value = target.Recommended;
            }
            UpdateSelectionSummary();
        }

        private List<CleanupTarget> GetSelectedTargets()
        {
            List<CleanupTarget> selected = new List<CleanupTarget>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool picked = row.Cells[0].Value is bool && (bool)row.Cells[0].Value;
                CleanupTarget target = row.Tag as CleanupTarget;
                if (picked && target != null && target.Bytes > 0)
                    selected.Add(target);
            }
            return selected;
        }

        private void GridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0)
                UpdateSelectionSummary();
        }

        private void GridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            CleanupTarget target = grid.Rows[e.RowIndex].Tag as CleanupTarget;
            if (target != null && Directory.Exists(target.Path))
            {
                try { Process.Start("explorer.exe", "/select,\"" + target.Path + "\""); }
                catch { }
            }
        }

        private void UpdateSelectionSummary()
        {
            if (selectionLabel == null || totalLabel == null)
                return;
            int count = 0;
            long bytes = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool picked = row.Cells[0].Value is bool && (bool)row.Cells[0].Value;
                CleanupTarget target = row.Tag as CleanupTarget;
                if (picked && target != null)
                {
                    count++;
                    bytes += target.Bytes;
                }
            }
            selectionLabel.Text = "已选 " + count.ToString(CultureInfo.InvariantCulture) + " 项";
            totalLabel.Text = "预计可释放 " + FormatBytes(bytes);
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            scanButton.Enabled = !value;
            recommendedButton.Enabled = !value;
            cleanButton.Enabled = !value && scanCurrent;
            ageDays.Enabled = !value;
            storageButton.Enabled = !value;
            recycleButton.Enabled = !value;
            componentButton.Enabled = !value;
            hibernateButton.Enabled = !value && File.Exists(@"C:\hiberfil.sys");
            restoreHibernateButton.Enabled = !value && !File.Exists(@"C:\hiberfil.sys");
            grid.Enabled = !value;
            statusLabel.Text = status;
            if (value)
                progressBar.Value = 0;
        }

        private void RefreshDriveSummary()
        {
            try
            {
                DriveInfo drive = new DriveInfo("C");
                long used = drive.TotalSize - drive.AvailableFreeSpace;
                driveLabel.Text = "C: 已用 " + FormatBytes(used) + " / " + FormatBytes(drive.TotalSize) + "    可用 " + FormatBytes(drive.AvailableFreeSpace);
            }
            catch { driveLabel.Text = "C: 空间信息暂不可用"; }
        }

        private void RefreshHibernateState()
        {
            try
            {
                FileInfo hiber = new FileInfo(@"C:\hiberfil.sys");
                if (hiber.Exists)
                {
                    hibernateButton.Text = "关闭休眠（释放约 " + FormatBytes(hiber.Length) + "）";
                    hibernateButton.Enabled = true;
                    restoreHibernateButton.Enabled = false;
                    toolTip.SetToolTip(hibernateButton, "会同时关闭 Windows 快速启动；可以用右侧按钮恢复。");
                }
                else
                {
                    hibernateButton.Text = "休眠已关闭";
                    hibernateButton.Enabled = false;
                    restoreHibernateButton.Enabled = true;
                }
            }
            catch
            {
                hibernateButton.Text = "关闭休眠";
            }
        }

        private void RunComponentCleanup()
        {
            DialogResult answer = MessageBox.Show(this,
                "将以管理员身份运行 Windows 自带的 DISM 组件清理。\r\n\r\n这不会手工删除 WinSxS，通常需要数分钟，期间不要关机。是否继续？",
                "清理 Windows 组件存储", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK)
                return;
            RunElevated("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup");
            AppendLog("已启动 Windows 组件存储清理窗口。");
        }

        private void DisableHibernate()
        {
            DialogResult answer = MessageBox.Show(this,
                "关闭休眠会删除 hiberfil.sys 并释放磁盘空间，但也会禁用“休眠”和“快速启动”。\r\n\r\n睡眠功能通常不受影响，稍后可用“恢复休眠”撤销。是否继续？",
                "关闭休眠", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK)
                return;
            RunElevated("powercfg.exe", "/hibernate off");
            AppendLog("已请求关闭休眠。管理员命令完成后，重新打开本程序可刷新状态。");
        }

        private void EnableHibernate()
        {
            DialogResult answer = MessageBox.Show(this,
                "恢复休眠会重新创建 hiberfil.sys，占用数 GB 磁盘空间，并重新启用快速启动。是否继续？",
                "恢复休眠", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK)
                return;
            RunElevated("powercfg.exe", "/hibernate on");
            AppendLog("已请求恢复休眠。管理员命令完成后，重新打开本程序可刷新状态。");
        }

        private void EmptyRecycleBin()
        {
            DialogResult answer = MessageBox.Show(this,
                "这会永久清空 C 盘回收站，文件将无法从回收站恢复。是否继续？",
                "清空回收站", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.OK)
                return;
            int result = SHEmptyRecycleBin(Handle, "C:\\", SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            if (result == 0)
            {
                AppendLog("C 盘回收站已清空。");
                RefreshDriveSummary();
            }
            else
                MessageBox.Show(this, "回收站清理未完成，错误代码：" + result.ToString(CultureInfo.InvariantCulture), "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void RunElevated(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223)
                    MessageBox.Show(this, ex.Message, "无法启动管理员命令", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法启动管理员命令", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void AppendLog(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine;
            logBox.AppendText(line);
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + units[unit];
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CleanerForm());
        }
    }
}
