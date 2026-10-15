using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace CodifyInstaller
{
    internal static class Program
    {
        private const string PluginId = "codify_1.5";
        private const string ConfigFileName = "qq-notify.json";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 4 && args[0].Equals("/silent", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    bool sc = args.Length > 4 ? args[4] != "0" : true;
                    bool sx = args.Length > 5 ? args[5] != "0" : true;
                    string dest = Install(args[1], args[2], args[3], sc, sx);
                    WriteLog("INSTALL_OK " + dest);
                }
                catch (Exception ex)
                {
                    WriteLog("INSTALL_FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }

            Application.Run(new MainForm());
        }

        internal static string OpencodeConfigDir
        {
            get
            {
                string profile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (string.IsNullOrEmpty(profile))
                    throw new InvalidOperationException("无法确定用户目录（USERPROFILE 未设置）");
                return Path.Combine(profile, ".config", "opencode");
            }
        }

        internal static string PluginDir
        {
            get { return Path.Combine(OpencodeConfigDir, "plugins", PluginId); }
        }

        internal static string PluginFile
        {
            get { return Path.Combine(PluginDir, "index.js"); }
        }

        internal static string ConfigFile
        {
            get { return Path.Combine(OpencodeConfigDir, ConfigFileName); }
        }

        internal static string ToggleDir
        {
            get
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return Path.Combine(desktop, "Codify 开关");
            }
        }

        internal static string InstallLog
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "install.log"); }
        }

        private static string ReadEmbeddedPlugin()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("Codify.Plugin"))
            {
                if (stream == null)
                    throw new InvalidOperationException("内嵌插件资源缺失");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static void WriteLog(string msg)
        {
            try
            {
                File.WriteAllText(InstallLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + "\n", Encoding.UTF8);
            }
            catch { }
        }

        private static string BuildConfig(string from, string code, string to, bool showCost, bool showContext)
        {
            return "{\"host\":\"smtp.qq.com\",\"port\":465,\"user\":\"" + JsonEscape(from)
                + "\",\"authCode\":\"" + JsonEscape(code)
                + "\",\"to\":\"" + JsonEscape(to)
                + "\",\"fromName\":\"OpenCode\""
                + ",\"showCost\":" + (showCost ? "true" : "false")
                + ",\"showContext\":" + (showContext ? "true" : "false")
                + "}";
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        internal static string Install(string from, string code, string to, bool showCost, bool showContext)
        {
            if (string.IsNullOrWhiteSpace(from)) throw new ArgumentException("发件邮箱不能为空");
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("授权码不能为空");
            if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("收件邮箱不能为空");

            Directory.CreateDirectory(PluginDir);

            File.WriteAllText(PluginFile, ReadEmbeddedPlugin(), new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(PluginDir, "package.json"),
                "{\n  \"name\": \"" + PluginId + "\",\n  \"version\": \"1.5.0\",\n  \"type\": \"module\",\n  \"main\": \"index.js\"\n}\n",
                new UTF8Encoding(false));

            File.WriteAllText(ConfigFile, BuildConfig(from, code, to, showCost, showContext), new UTF8Encoding(false));

            WriteToggleScripts();

            return PluginFile;
        }

        private static void WriteToggleScripts()
        {
            try
            {
                Directory.CreateDirectory(ToggleDir);
                File.WriteAllText(Path.Combine(ToggleDir, "notify-toggle.ps1"), TogglePs1, new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(ToggleDir, "启用通知.bat"), EnableBat, Encoding.ASCII);
                File.WriteAllText(Path.Combine(ToggleDir, "禁用通知.bat"), DisableBat, Encoding.ASCII);
            }
            catch (Exception ex)
            {
                WriteLog("toggle script failed: " + ex.Message);
            }
        }

        private const string TogglePs1 =
            "param([string]$Action = 'enable')\r\n" +
            "$f = Join-Path $env:USERPROFILE '.config\\opencode\\opencode.json'\r\n" +
            "$disable = @'\r\n{\r\n  \"$schema\": \"https://opencode.ai/config.json\",\r\n  \"plugins\": [\"-codify_1.5\"]\r\n}\r\n'@\r\n" +
            "$enable = @'\r\n{\r\n  \"$schema\": \"https://opencode.ai/config.json\",\r\n  \"plugins\": []\r\n}\r\n'@\r\n" +
            "$json = if ($Action -eq 'disable') { $disable } else { $enable }\r\n" +
            "[System.IO.File]::WriteAllText($f, $json, (New-Object System.Text.UTF8Encoding($false)))\r\n" +
            "if ($Action -eq 'disable') { Write-Host '  Codify 通知已禁用（回复结束不再发送邮件）' -ForegroundColor Yellow }\r\n" +
            "else { Write-Host '  Codify 通知已启用（回复结束会发送邮件）' -ForegroundColor Green }\r\n";

        private const string EnableBat =
            "@echo off\r\n" +
            "powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0notify-toggle.ps1\" enable\r\n" +
            "pause\r\n";

        private const string DisableBat =
            "@echo off\r\n" +
            "powershell -NoProfile -ExecutionPolicy Bypass -File \"%~dp0notify-toggle.ps1\" disable\r\n" +
            "pause\r\n";

        private sealed class MainForm : Form
        {
            private readonly TextBox _from;
            private readonly TextBox _code;
            private readonly TextBox _to;
            private readonly Label _status;
            private readonly Label lblSwitchState;
            private readonly CheckBox chkCost;
            private readonly CheckBox chkContext;

            public MainForm()
            {
                Text = "Codify 1.5 安装程序";
                Font = new Font("Microsoft YaHei", 9f);
                ClientSize = new Size(560, 648);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;

                int y = 14;

                var lblTitle = new Label { Left = 16, Top = y, Width = 520, Height = 26, Text = "Codify 1.5 一键安装", Font = new Font("Microsoft YaHei", 13f, FontStyle.Bold) };
                Controls.Add(lblTitle);
                y += 34;

                var info = new TextBox
                {
                    Left = 16, Top = y, Width = 520, Height = 190,
                    ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                    BackColor = SystemColors.Window, Text = InfoText
                };
                Controls.Add(info);
                y += 200;

                _from = AddField(ref y, "发件邮箱（需已开启 SMTP 服务）");
                _code = AddField(ref y, "授权码（16 位小写字母）", true);
                _to = AddField(ref y, "收件邮箱（手机 QQ 登录的那个）");

                var btnInstall = new Button { Left = 16, Top = y, Width = 160, Height = 34, Text = "安装" };
                btnInstall.Click += (s, e) => DoInstall();
                Controls.Add(btnInstall);

                var btnRestart = new Button { Left = 196, Top = y, Width = 340, Height = 34, Text = "重启 OpenCode 服务（使插件生效）" };
                btnRestart.Click += (s, e) => DoRestart();
                Controls.Add(btnRestart);
                y += 46;

                var lblSwitch = new Label { Left = 16, Top = y + 7, Width = 88, Height = 20, Text = "通知开关：" };
                Controls.Add(lblSwitch);

                var btnEnable = new Button { Left = 106, Top = y, Width = 120, Height = 32, Text = "启用通知" };
                btnEnable.Click += (s, e) => SetNotifyEnabled(true);
                Controls.Add(btnEnable);

                var btnDisable = new Button { Left = 234, Top = y, Width = 120, Height = 32, Text = "禁用通知" };
                btnDisable.Click += (s, e) => SetNotifyEnabled(false);
                Controls.Add(btnDisable);

                lblSwitchState = new Label { Left = 370, Top = y + 7, Width = 170, Height = 20, Text = "当前：…", ForeColor = Color.Gray };
                Controls.Add(lblSwitchState);
                y += 42;

                var lblMail = new Label { Left = 16, Top = y + 3, Width = 88, Height = 20, Text = "邮件内容：" };
                Controls.Add(lblMail);

                chkCost = new CheckBox { Left = 106, Top = y, Width = 140, Height = 22, Text = "显示成本", Checked = true };
                chkCost.CheckedChanged += (s, e) => UpdateFlags();
                Controls.Add(chkCost);

                chkContext = new CheckBox { Left = 254, Top = y, Width = 210, Height = 22, Text = "显示上下文使用率", Checked = true };
                chkContext.CheckedChanged += (s, e) => UpdateFlags();
                Controls.Add(chkContext);
                y += 30;

                _status = new Label { Left = 16, Top = y, Width = 520, Height = 56, Text = "填写以上信息后点击「安装」，或把含 3 行内容的 txt 拖入本窗口自动填充。", ForeColor = Color.Gray };
                Controls.Add(_status);

                AllowDrop = true;
                DragEnter += OnDragEnter;
                DragDrop += OnDragDrop;
                foreach (Control c in Controls)
                {
                    c.AllowDrop = true;
                    c.DragEnter += OnDragEnter;
                    c.DragDrop += OnDragDrop;
                }

                RefreshSwitchState();
                chkCost.Checked = GetConfigFlag("showCost", true);
                chkContext.Checked = GetConfigFlag("showContext", true);
            }

            private TextBox AddField(ref int y, string label, bool password = false)
            {
                var lbl = new Label { Left = 16, Top = y, Width = 520, Height = 18, Text = label };
                Controls.Add(lbl);
                y += 20;
                var txt = new TextBox { Left = 16, Top = y, Width = 520, Height = 24 };
                if (password) txt.PasswordChar = '*';
                Controls.Add(txt);
                y += 30;
                return txt;
            }

            private void OnDragEnter(object sender, DragEventArgs e)
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0 && files[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        e.Effect = DragDropEffects.Copy;
                }
            }

            private void OnDragDrop(object sender, DragEventArgs e)
            {
                try
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files == null || files.Length == 0) return;
                    var lines = File.ReadAllLines(files[0]);
                    if (lines.Length >= 1) _from.Text = Clean(lines[0]);
                    if (lines.Length >= 2) _code.Text = Clean(lines[1]);
                    if (lines.Length >= 3) _to.Text = Clean(lines[2]);
                    _status.Text = "已从文件自动填充：" + Path.GetFileName(files[0]);
                    _status.ForeColor = Color.Green;
                }
                catch (Exception ex)
                {
                    _status.Text = "读取文件失败：" + ex.Message;
                    _status.ForeColor = Color.Red;
                }
            }

            private static string Clean(string s)
            {
                if (s == null) return "";
                return s.Trim().TrimStart('\uFEFF').Trim();
            }

            private void SetNotifyEnabled(bool enabled)
            {
                try
                {
                    string f = Path.Combine(OpencodeConfigDir, "opencode.json");
                    string json = enabled
                        ? "{\n  \"$schema\": \"https://opencode.ai/config.json\",\n  \"plugins\": []\n}"
                        : "{\n  \"$schema\": \"https://opencode.ai/config.json\",\n  \"plugins\": [\"-" + PluginId + "\"]\n}";
                    File.WriteAllText(f, json, new UTF8Encoding(false));
                    RefreshSwitchState();
                    _status.Text = enabled ? "已启用通知：回复结束会发送邮件。" : "已禁用通知：回复结束不再发送邮件。";
                    _status.ForeColor = enabled ? Color.Green : Color.OrangeRed;
                }
                catch (Exception ex)
                {
                    _status.Text = "切换失败：" + ex.Message;
                    _status.ForeColor = Color.Red;
                }
            }

            private static bool GetConfigFlag(string key, bool def)
            {
                try
                {
                    string f = ConfigFile;
                    if (!File.Exists(f)) return def;
                    string content = File.ReadAllText(f);
                    var m = System.Text.RegularExpressions.Regex.Match(content, "\"" + key + "\"\\s*:\\s*(true|false)");
                    if (m.Success) return m.Groups[1].Value == "true";
                }
                catch { }
                return def;
            }

            private static string GetJsonString(string json, string key)
            {
                try
                {
                    var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
                catch { }
                return "";
            }

            private void UpdateFlags()
            {
                try
                {
                    string f = ConfigFile;
                    if (!File.Exists(f)) return;
                    string content = File.ReadAllText(f);
                    string user = GetJsonString(content, "user");
                    string code = GetJsonString(content, "authCode");
                    string to = GetJsonString(content, "to");
                    if (string.IsNullOrEmpty(user)) return;
                    File.WriteAllText(f, BuildConfig(user, code, to, chkCost.Checked, chkContext.Checked), new UTF8Encoding(false));
                    _status.Text = "已更新邮件显示设置（成本：" + (chkCost.Checked ? "开" : "关") + "，上下文：" + (chkContext.Checked ? "开" : "关") + "）";
                    _status.ForeColor = Color.Green;
                }
                catch (Exception ex)
                {
                    _status.Text = "更新设置失败：" + ex.Message;
                    _status.ForeColor = Color.Red;
                }
            }

            private void RefreshSwitchState()
            {
                try
                {
                    string f = Path.Combine(OpencodeConfigDir, "opencode.json");
                    bool enabled = true;
                    if (File.Exists(f))
                    {
                        string content = File.ReadAllText(f);
                        if (content.Contains("-" + PluginId)) enabled = false;
                    }
                    lblSwitchState.Text = enabled ? "当前：已启用" : "当前：已禁用";
                    lblSwitchState.ForeColor = enabled ? Color.Green : Color.OrangeRed;
                }
                catch
                {
                    lblSwitchState.Text = "当前：未知";
                }
            }

            private void DoInstall()
            {
                try
                {
                    string dest = Install(_from.Text.Trim(), _code.Text.Trim(), _to.Text.Trim(), chkCost.Checked, chkContext.Checked);
                    _status.Text = "安装完成！\n插件位置：" + dest + "\n配置：" + ConfigFile + "\n开关脚本：" + ToggleDir;
                    _status.ForeColor = Color.Green;
                }
                catch (Exception ex)
                {
                    _status.Text = "安装失败：" + ex.Message;
                    _status.ForeColor = Color.Red;
                }
            }

            private void DoRestart()
            {
                try
                {
                    string exe = FindOpencode();
                    if (exe == null)
                    {
                        _status.Text = "未找到 OpenCode，请手动执行：opencode service restart";
                        _status.ForeColor = Color.Red;
                        return;
                    }
                    var psi = new System.Diagnostics.ProcessStartInfo(exe, "service restart")
                    {
                        UseShellExecute = false,
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) throw new InvalidOperationException("无法启动进程");
                        if (!p.WaitForExit(20000)) { p.Kill(); throw new InvalidOperationException("重启超时"); }
                        if (p.ExitCode == 0)
                        {
                            _status.Text = "已重启 OpenCode 服务，插件生效。";
                            _status.ForeColor = Color.Green;
                        }
                        else
                        {
                            _status.Text = "重启失败（退出码 " + p.ExitCode + "），请手动执行：opencode service restart";
                            _status.ForeColor = Color.Red;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _status.Text = "重启失败，请手动执行：opencode service restart\n" + ex.Message;
                    _status.ForeColor = Color.Red;
                }
            }

            private static string FindOpencode()
            {
                string appdata = Environment.GetEnvironmentVariable("APPDATA");
                if (!string.IsNullOrEmpty(appdata))
                {
                    string candidate = Path.Combine(appdata, "npm", "node_modules", "@opencode", "cli", "bin", "opencode.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("cmd", "/c \"where opencode 2>nul\"")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(5000);
                        foreach (string line in output.Split('\n'))
                        {
                            string t = line.Trim();
                            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(t)) return t;
                        }
                    }
                }
                catch { }
                return null;
            }

            private const string InfoText =
                "Codify 1.5 会在 OpenCode 每轮回复结束时，通过 QQ 邮箱 SMTP 发送一封通知邮件（含回复摘要、耗时、成本与上下文使用率）到你的收件邮箱，手机 QQ 即可收到提醒。\n\n"
                + "【使用前请完成以下准备】\n"
                + "1. 登录 QQ 邮箱网页版 mail.qq.com\n"
                + "2. 进入「设置 → 账户」，找到「IMAP/SMTP 服务」并开启\n"
                + "3. 开启后点击「生成授权码」，按提示发送短信验证\n"
                + "4. 获得 16 位授权码（小写字母）\n\n"
                + "【快速填充】\n"
                + "把含 3 行内容的 txt 拖入本窗口可自动填充：\n"
                + "第 1 行 = 发件邮箱，第 2 行 = 授权码，第 3 行 = 收件邮箱\n\n"
                + "【注意】\n"
                + "- 发件邮箱与授权码必须属于同一个 QQ 邮箱账户\n"
                + "- 授权码不是 QQ 登录密码\n"
                + "- 发件邮箱与收件邮箱可以不同\n"
                + "- 安装后如 OpenCode 正在运行，请点击「重启 OpenCode 服务」使其生效";
        }
    }
}
