using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RE8Characters
{
    public static class Catalog
    {
        public static readonly string[] BuiltInFiles = {
            "LadyDimitrescu_Ethan_FP_TP_LongDress_TEST_v0.4.zip",
            "LadyDimitrescu_Mercenaries_Ethan_FP_LongDress_TEST_v0.4.zip",
            "LadyDimitrescu_Mercenaries_LongDress_TEST_v0.4.zip",
            "LadyDimitrescu_Story_Original_LongDress_v1.1.zip",
            "LadyDimitrescu_Ethan_Breath_MultiLang_TEST_v0.2.zip"
        };
        public static readonly string[] PreviousBuiltInFiles = {
            "LadyDimitrescu_Ethan_Breath_EN_TEST_v0.1.zip",
            "LadyDimitrescu_Ethan_HighHeels_TEST_v0.3.zip",
            "高跟鞋脚步声_v0.3_仅音频内容测试.zip",
            "LadyDimitrescu_Ethan_FP_TP_LongDress_TEST_v0.3.zip",
            "LadyDimitrescu_Mercenaries_Ethan_FP_LongDress_TEST_v0.3.zip",
            "LadyDimitrescu_Ethan_HighHeels_TEST_v0.2.zip",
            "LadyDimitrescu_Mercenaries_LongDress_TEST_v0.3.zip",
            "LadyDimitrescu_Ethan_HighHeels_TEST_v0.1.zip",
            "LadyDimitrescu_Ethan_FP_TP_LongDress_TEST_v0.2.zip",
            "LadyDimitrescu_Mercenaries_Ethan_FP_LongDress_TEST_v0.2.zip",
            "LadyDimitrescu_Mercenaries_LongDress_TEST_v0.2.zip",
            "LadyDimitrescu_Ethan_FP_TP_LongDress_TEST_v0.1.zip",
            "LadyDimitrescu_Mercenaries_Ethan_FP_LongDress_TEST_v0.1.zip",
            "LadyDimitrescu_Mercenaries_LongDress_TEST_v0.1.zip"
        };
        public static List<ModPackage> Load(string home)
        {
            string output = Path.GetFullPath(Path.Combine(home, "..", "outputs"));
            var result = new List<ModPackage>();
            Add(result, home, output, "story_ethan", "剧情伊森 v0.4 · 第一 / 第三人称 + 美术造型（含佣兵伊森）", "ethan", BuiltInFiles[0]);
            Add(result, home, output, "mercenary_ethan", "佣兵伊森 v0.4 · 第一人称 + 美术造型", "ethan", BuiltInFiles[1]);
            Add(result, home, output, "mercenary_lady", "佣兵夫人 · 长裙替换 v0.4", "lady", BuiltInFiles[2]);
            Add(result, home, output, "story_lady", "剧情夫人本体 · 原始 mod + 长裙补丁", "story_lady", "LadyDimitrescu_Story_Original_LongDress_v1.1.zip");
            // Keep the v0.8 ID so an enabled English package stays checked on upgrade.
            Add(result, home, output, "ethan_breath_en", "伊森呼吸声 · 夫人 v0.2（9 种配音，随游戏语言切换）", "breath_audio", BuiltInFiles[4]);
            string custom = Path.Combine(home, "mods");
            if (Directory.Exists(custom))
                foreach (string archive in Directory.GetFiles(custom, "*.zip").OrderBy(p => p))
                    if (!PreviousBuiltInFiles.Contains(Path.GetFileName(archive), StringComparer.OrdinalIgnoreCase) &&
                        !result.Any(p => String.Equals(p.Archive, archive, StringComparison.OrdinalIgnoreCase)))
                        result.Add(new ModPackage { Id = Path.GetFileNameWithoutExtension(archive), Name = "导入包 · " + Path.GetFileNameWithoutExtension(archive), Archive = archive, Group = "custom" });
            return result;
        }
        private static void Add(List<ModPackage> list, string home, string output, string id, string name, string group, string filename)
        {
            string local = Path.Combine(home, "mods", filename);
            string archive = File.Exists(local) ? local : Path.Combine(output, filename);
            if (File.Exists(archive)) list.Add(new ModPackage { Id = id, Name = name, Group = group, Archive = archive });
        }
        public static List<ModPackage> AllTargets(List<ModPackage> packages, bool includeBreaths = false)
        {
            var result = packages.Where(p => p.Id == "story_ethan" || p.Id == "mercenary_lady" || p.Id == "story_lady").ToList();
            if (result.Count != 3) throw new FileNotFoundException("一键全部替换需要剧情伊森、佣兵夫人和剧情夫人本体三个包，请保持 mods 或 outputs 文件夹中的 ZIP 可用。");
            if (includeBreaths) result.AddRange(packages.Where(p => p.Id == "ethan_breath_en"));
            return result;
        }
    }

    public sealed class MainForm : Form
    {
        private readonly string home;
        private readonly ManagerEngine engine;
        private Settings settings;
        private List<ModPackage> packages;
        private TextBox pathBox, logBox;
        private CheckedListBox choices;
        private Label status;
        private Button apply, all, restore, launch, browse, import;
        private ProgressBar progress;
        private bool changingChecks, busy;
        private readonly Color blue = Color.FromArgb(34, 93, 186);

        public MainForm(string applicationHome)
        {
            home = applicationHome; engine = new ManagerEngine(home);
            settings = File.Exists(Path.Combine(home, "data", "settings.json")) ? Codec.Read<Settings>(Path.Combine(home, "data", "settings.json")) : new Settings();
            packages = Catalog.Load(home);
            Text = "RE8 人物替换管理器 beta0.9 · 多语言呼吸声版";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(960, 693); MinimumSize = new Size(976, 732);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            BuildUi();
            engine.Log = AddLog;
            RefreshState();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; AddLog("操作进行中，完成后即可关闭窗口。"); } };
        }
        private Label Caption(string text, int x, int y, int width, int height, float size, bool bold)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = Color.FromArgb(32, 42, 60) };
            Controls.Add(label); return label;
        }
        private Button ActionButton(string text, int x, int y, int width, bool primary, Action action)
        {
            var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 40), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? blue : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 53, 74), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? blue : Color.FromArgb(209, 217, 229);
            button.Click += delegate { action(); }; Controls.Add(button); return button;
        }
        private void BuildUi()
        {
            Caption("RE8 人物替换", 28, 22, 650, 43, 24F, true);
            Caption("一键替换全部人物；呼吸声按勾选项启用。保持目前的脚步声配置。", 30, 74, 875, 27, 10F, false);
            Caption("游戏目录", 30, 118, 100, 25, 10F, true);
            pathBox = new TextBox { Text = settings.GameRoot, ReadOnly = true, Location = new Point(30, 147), Size = new Size(744, 28), BackColor = Color.White };
            Controls.Add(pathBox);
            browse = ActionButton("选择目录", 794, 141, 134, false, BrowseGame);
            Caption("人物与呼吸声", 30, 203, 450, 25, 11F, true);
            Caption("夫人呼吸声支持中、英、日、德、法、意、西、巴葡、俄语，随游戏配音切换。", 30, 232, 895, 25, 9F, false);
            choices = new CheckedListBox { Location = new Point(30, 267), Size = new Size(898, 144), CheckOnClick = true,
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, HorizontalScrollbar = true };
            foreach (var mod in packages) choices.Items.Add(mod);
            choices.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (changingChecks || e.NewValue != CheckState.Checked) return;
                var selected = choices.Items[e.Index] as ModPackage;
                if (selected.Group != "ethan") return;
                changingChecks = true;
                for (int i = 0; i < choices.Items.Count; i++)
                    if (i != e.Index && ((ModPackage)choices.Items[i]).Group == "ethan") choices.SetItemChecked(i, false);
                changingChecks = false;
            };
            Controls.Add(choices);
            all = ActionButton("一键全部替换", 30, 432, 180, true, delegate {
                try {
                    var selected = Catalog.AllTargets(packages, choices.CheckedItems.Cast<ModPackage>().Any(p => p.Id == "ethan_breath_en"));
                    changingChecks = true;
                    for (int i = 0; i < choices.Items.Count; i++) choices.SetItemChecked(i, selected.Any(p => p.Id == ((ModPackage)choices.Items[i]).Id));
                    changingChecks = false;
                    ApplyPackages(selected);
                } catch (Exception ex) { changingChecks = false; ShowError(ex); }
            });
            apply = ActionButton("应用勾选项", 225, 432, 155, false, delegate { ApplyPackages(choices.CheckedItems.Cast<ModPackage>().ToList()); });
            restore = ActionButton("停用并还原", 395, 432, 155, false, delegate { Work(delegate { engine.Restore(); }); });
            launch = ActionButton("启动游戏", 565, 432, 155, false, delegate {
                try { engine.ValidateGame(settings.GameRoot); Process.Start(new ProcessStartInfo("steam://rungameid/1196590") { UseShellExecute = true }); }
                catch (Exception ex) { ShowError(ex); }
            });
            import = ActionButton("导入替换 ZIP", 735, 432, 193, false, ImportPackage);
            status = Caption("就绪", 30, 494, 898, 30, 10F, true);
            progress = new ProgressBar { Location = new Point(30, 525), Size = new Size(898, 5), Visible = false, Style = ProgressBarStyle.Marquee };
            Controls.Add(progress);
            logBox = new TextBox { Location = new Point(30, 548), Size = new Size(898, 89), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 9F) };
            Controls.Add(logBox);
            Caption("保留 data 文件夹。英语呼吸声已实测，其余语言待实测；取消勾选并应用可恢复原声。", 30, 654, 898, 24, 9F, false);
        }
        private void AddLog(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AddLog), text); return; }
            logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        }
        private void ShowError(Exception exception)
        {
            AddLog(exception.Message);
            MessageBox.Show(this, exception.Message, "人物替换未完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void RefreshState()
        {
            try {
                var current = engine.ReadState();
                status.Text = ManagerEngine.IsActive(current) ? (current.Status == "Active" ? "已启用：" + String.Join("、", current.Mods.Select(m =>
                    m.Id == "story_ethan" ? "剧情与佣兵伊森" : m.Id == "mercenary_ethan" ? "佣兵伊森" :
                    m.Id == "mercenary_lady" ? "佣兵夫人" : m.Id == "story_lady" ? "剧情夫人本体" : m.Id == "ethan_breath_en" ? "夫人呼吸声" : m.Id == "ethan_high_heels" ? "高跟鞋声" : m.Name)) : "上次操作未完成，请先点击“停用并还原”。") : "本工具尚未启用替换 · 勾选人物或音效后应用";
                if (current != null && ManagerEngine.IsActive(current)) {
                    if (current.Status == "Active" && current.Mods.Any(m => packages.Any(p => p.Id == m.Id &&
                        !String.Equals(Path.GetFileName(p.Archive), Path.GetFileName(m.Archive), StringComparison.OrdinalIgnoreCase))))
                        status.Text = "当前仍启用旧版包 · 退出游戏后点击“一键全部替换”或“应用勾选项”更新";
                    settings.GameRoot = current.GameRoot; pathBox.Text = current.GameRoot;
                    changingChecks = true;
                    for (int i = 0; i < choices.Items.Count; i++) choices.SetItemChecked(i, current.Mods.Any(m => m.Id == ((ModPackage)choices.Items[i]).Id));
                    changingChecks = false;
                } else if (choices.CheckedItems.Count == 0) {
                    for (int i = 0; i < choices.Items.Count; i++) if (((ModPackage)choices.Items[i]).Id == "mercenary_ethan") choices.SetItemChecked(i, true);
                }
                if (packages.Count == 0) AddLog("没有发现人物 ZIP。请保留项目中的 outputs 文件夹，或点击“导入人物 ZIP”。");
            } catch (Exception ex) { status.Text = "状态读取失败 · 请保留 data 文件夹"; AddLog(ex.Message); }
        }
        private void SetBusy(bool value)
        {
            busy = value; foreach (Control control in new Control[] { apply, all, restore, launch, browse, import, choices }) control.Enabled = !value;
            progress.Visible = value; if (value) status.Text = "正在处理人物替换，请稍候…";
        }
        private void Work(Action operation)
        {
            if (busy) return;
            SetBusy(true);
            Task.Factory.StartNew(delegate {
                Exception error = null; try { operation(); } catch (Exception ex) { error = ex; }
                BeginInvoke(new Action(delegate { SetBusy(false); RefreshState(); if (error != null) ShowError(error); }));
            });
        }
        private void ApplyPackages(List<ModPackage> selected)
        {
            Work(delegate { engine.Install(settings.GameRoot, selected); });
        }
        private void BrowseGame()
        {
            try {
                if (ManagerEngine.IsActive(engine.ReadState())) throw new IOException("请先停用并还原，再更换游戏目录。");
                using (var dialog = new FolderBrowserDialog { Description = "选择包含 re8.exe 的游戏目录", SelectedPath = settings.GameRoot })
                    if (dialog.ShowDialog(this) == DialogResult.OK) {
                        engine.ValidateGame(dialog.SelectedPath); settings.GameRoot = dialog.SelectedPath; pathBox.Text = settings.GameRoot;
                        Codec.Save(Path.Combine(home, "data", "settings.json"), settings); AddLog("已更新游戏目录。");
                    }
            } catch (Exception ex) { ShowError(ex); }
        }
        private void ImportPackage()
        {
            using (var dialog = new OpenFileDialog { Title = "选择人物或音效 ZIP", Filter = "替换 ZIP (*.zip)|*.zip" })
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    string source = dialog.FileName;
                    Work(delegate {
                        int count = engine.InspectPackage(source).Count;
                        string folder = Path.Combine(home, "mods"); Directory.CreateDirectory(folder);
                        string filename = Path.GetFileName(source);
                        if (Catalog.PreviousBuiltInFiles.Contains(filename, StringComparer.OrdinalIgnoreCase))
                            throw new IOException("此包已停用，请使用本管理器当前列出的人物或音效包。");
                        string name = Catalog.BuiltInFiles.Contains(filename, StringComparer.OrdinalIgnoreCase) ? filename : Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + filename;
                        string destination = Path.Combine(folder, name);
                        if (!File.Exists(destination)) File.Copy(source, destination, false);
                        else if (Codec.FileHash(destination) != Codec.FileHash(source)) throw new IOException("同名替换包已经存在，请先移走 mods 文件夹中的旧 ZIP 后重新导入。");
                        BeginInvoke(new Action(delegate { packages = Catalog.Load(home); choices.Items.Clear(); foreach (var mod in packages) choices.Items.Add(mod); }));
                        AddLog("已导入替换包，包含 " + count + " 个资源文件。");
                    });
                }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string home = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            try {
                if (args.Length > 0 && args[0] == "--self-test") { Tests.Run(home); return 0; }
                if (args.Length > 0 && args[0] == "--scan-game") {
                    var engine = new ManagerEngine(home);
                    string game = args.Length > 1 ? args[1] : new Settings().GameRoot;
                    var result = engine.ScanOnly(game, Catalog.AllTargets(Catalog.Load(home), true));
                    Codec.Save(Path.Combine(home, "read_only_game_scan.json"), result); return 0;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                // Rendering a preview only reads settings/state and never runs
                // an install. It can coexist with an already-open live manager.
                if (args.Length > 1 && args[0] == "--ui-preview") {
                    using (var preview = new MainForm(home)) {
                        preview.ShowInTaskbar = false; preview.Opacity = 0; preview.Show(); Application.DoEvents();
                        using (var bitmap = new Bitmap(preview.Width, preview.Height)) { preview.DrawToBitmap(bitmap, new Rectangle(0, 0, preview.Width, preview.Height)); bitmap.Save(args[1]); }
                        preview.Close(); return 0;
                    }
                }
                using (var gate = new Mutex(false, "Local\\RE8CharacterManager")) {
                    if (!gate.WaitOne(0, false)) { MessageBox.Show("人物替换管理器已经打开。"); return 0; }
                    try {
                        var form = new MainForm(home);
                        Application.Run(form);
                    } finally { gate.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) {
                File.WriteAllText(Path.Combine(home, "last_error.txt"), ex.ToString());
                if (args.Length == 0) MessageBox.Show(ex.Message, "人物替换管理器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
    }
}
