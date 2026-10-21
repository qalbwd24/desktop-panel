using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DesktopPanel {
    internal static class Program {
        internal static string Root, DataPath, RuntimePath;
        internal static bool Preview, Smoke;
        internal static bool StartupLaunch, ManageStartup;
        internal static int? ApiPort;
        internal static Icon CreateIcon(int size) {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("DesktopPanel.AppIcon"))
            using (var icon = new Icon(stream,new Size(size,size))) return (Icon)icon.Clone();
        }
        internal static string Option(string[] args, string name) { int at = Array.IndexOf(args, name); return at >= 0 && at + 1 < args.Length ? args[at + 1] : null; }
        internal static void Log(string value) { try { Directory.CreateDirectory(RuntimePath); File.AppendAllText(Path.Combine(RuntimePath, "host.log"), DateTime.Now.ToString("s") + " " + value + Environment.NewLine, Encoding.UTF8); } catch { } }
        [STAThread] private static void Main(string[] args) {
            Root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            DataPath = Path.GetFullPath(Option(args, "--data-dir") ?? Path.Combine(Root, "data"));
            Preview = args.Contains("--preview"); Smoke = args.Contains("--smoke");
            StartupLaunch = args.Contains("--startup"); ManageStartup = !Smoke && Option(args,"--data-dir") == null && !args.Contains("--api-only");
            string portOption = Option(args, "--api-port"); if (portOption != null) ApiPort = Int32.Parse(portOption);
            RuntimePath = Smoke || args.Contains("--selftest") || Option(args, "--data-dir") != null ? Path.Combine(DataPath, ".runtime") : Path.Combine(Root, ".runtime");
            Directory.CreateDirectory(RuntimePath);
            Log("Launch pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" startup="+StartupLaunch+" isolated="+(!ManageStartup)+" cwd="+Environment.CurrentDirectory);
            Desktop.SetCurrentProcessExplicitAppUserModelID("Personal.DesktopPanel");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); if (!Smoke) Dialogs.ShowError(e.Exception.Message, "桌面组件"); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Log(Convert.ToString(e.ExceptionObject)); };
            try {
                if (args.Contains("--selftest")) { Store.SelfTest(DataPath); Startup.SelfTest(DataPath,Application.ExecutablePath); return; }
                try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
                bool first; string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(DataPath.ToUpperInvariant()))).Replace("-", "").Substring(0, 24);
                using (var mutex = new Mutex(true, "Local\\DesktopPanel-" + hash, out first)) {
                    if (!first) { if (!Smoke && !StartupLaunch) Dialogs.ShowError("这个数据目录的桌面组件已在运行，请使用托盘菜单。", "桌面组件"); return; }
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    var store = new Store(DataPath); store.ReadAll();
                    if (args.Contains("--api-only")) { using (var api = new LocalApi(store, null, ApiPort)) Application.Run(new ApplicationContext()); return; }
                    CoreWebView2Environment.GetAvailableBrowserVersionString();
                    Application.Run(new PanelContext(store));
                }
            } catch (Exception e) { Log(e.ToString()); if (Smoke) File.WriteAllText(Path.Combine(DataPath, "smoke-error.txt"), e.ToString(), Encoding.UTF8); else Dialogs.ShowError(e.Message, "桌面组件启动失败"); Environment.ExitCode = 1; }
            finally { Dialogs.Dispose(); }
        }
    }
    internal sealed class PanelContext : ApplicationContext {
        internal readonly Store Store;
        private NotifyIcon tray;
        private Icon trayIcon;
        private Surface surface;
        private string activeScreen;
        private Rectangle activeBounds;
        private bool exiting;
        private System.Windows.Forms.Timer shellTimer;
        private LocalApi api;
        private Startup startup;
        internal PanelContext(Store store) {
            Store = store;
            if (Program.ManageStartup) {
                startup = new Startup(Store.DirectoryPath,Application.ExecutablePath);
                if (!Program.StartupLaunch) try { startup.Initialize(); } catch (Exception e) { Program.Log("Startup registration: "+e.Message); Dialogs.ShowError("开机自启设置失败："+e.Message,"桌面组件"); }
            }
            if (!Program.Smoke || Program.ApiPort.HasValue) {
                try { api = new LocalApi(store, DataChanged, Program.ApiPort); }
                catch (Exception e) { Program.Log("Local API startup: " + e.Message); Dialogs.ShowError("组件仍可使用。管理后台未启动：" + e.Message, "桌面组件"); }
            }
            if (!Program.Smoke) {
                trayIcon = Program.CreateIcon(16);
                tray = new NotifyIcon { Text = "桌面组件 · 待办 / 周日程 / 倒计时", Icon = trayIcon, Visible = true };
                tray.DoubleClick += delegate { if (surface != null) surface.SendCommand("layout"); };
                BuildMenu();
            }
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.UserPreferenceChanged += PreferencesChanged;
            shellTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            shellTimer.Tick += delegate {
                if (surface == null || activeScreen != Target().DeviceName || activeBounds != Target().Bounds) { Recreate(); }
                else if (!Program.Preview && !Desktop.IsWindow(surface.DesktopParent)) {
                    Program.Log("Desktop parent changed; recreating surface."); Recreate();
                }
            };
            shellTimer.Start(); Recreate();
        }
        private Screen Target() { return Screen.PrimaryScreen; }
        private void BuildMenu() {
            if (tray == null) return;
            var menu = new ContextMenuStrip();
            menu.Items.Add("编辑布局", null, delegate { if (surface != null) surface.SendCommand("layout"); });
            var management = menu.Items.Add("管理后台", null, delegate { try { System.Diagnostics.Process.Start(api.OpenManagementUrl()); } catch (Exception e) { Dialogs.ShowError(e.Message, "管理后台"); } }); management.Enabled = api != null;
            menu.Items.Add("刷新壁纸", null, delegate { if (surface != null) surface.RefreshWallpaper(); });
            menu.Items.Add("切换桌面 / 预览窗口", null, delegate { Program.Preview = !Program.Preview; Recreate(); });
            menu.Items.Add("打开数据文件夹", null, delegate { System.Diagnostics.Process.Start("explorer.exe", Store.DirectoryPath); });
            if (startup != null) {
                var startAtLogin = new ToolStripMenuItem("开机自启") { Checked = startup.Enabled };
                menu.Opening += delegate { startAtLogin.Checked = startup.Enabled; };
                startAtLogin.Click += delegate { try { startup.SetEnabled(!startup.Enabled); BuildMenu(); } catch (Exception e) { Dialogs.ShowError(e.Message,"开机自启"); } };
                menu.Items.Add(startAtLogin);
            }
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, delegate { Exit(); });
            var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; if (old != null) old.Dispose();
        }
        private void DisplayChanged(object sender, EventArgs args) { if (surface != null && !surface.IsDisposed) surface.BeginInvoke((Action)delegate { Recreate(); BuildMenu(); }); }
        private void DataChanged(string key) {
            var current = surface;
            if (current != null && !current.IsDisposed) try { current.BeginInvoke((Action)delegate { if (!current.IsDisposed) current.RefreshData(key); }); } catch (InvalidOperationException) { }
        }
        private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs args) { if (surface != null && !surface.IsDisposed && (args.Category == UserPreferenceCategory.Desktop || args.Category == UserPreferenceCategory.General)) surface.BeginInvoke((Action)delegate { if (surface != null) surface.RefreshWallpaper(); }); }
        private void Recreate() {
            if (exiting) return;
            if (surface != null) { surface.AllowClose = true; surface.Close(); surface.Dispose(); }
            surface = null;
            if (!Program.Preview && Desktop.FindIconHost() == IntPtr.Zero) { Program.Log("Waiting for Windows desktop; retry in 5 seconds."); return; }
            var target = Target(); activeScreen = target.DeviceName; activeBounds = target.Bounds;
            surface = new Surface(this, target); surface.Show();
        }
        internal void FinishedSmoke() { Exit(); }
        internal object ProbeApi() {
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(api.Address + "api/v1/todos");
            request.Method = "POST"; request.ContentType = "application/json"; request.Headers["Authorization"] = "Bearer " + api.Token; request.Timeout = 5000;
            byte[] bytes = Encoding.UTF8.GetBytes("{\"title\":\"后台同步验证\"}"); request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            string id; using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) {
                var result = (Dictionary<string, object>)Store.Json.DeserializeObject(reader.ReadToEnd()); id = Convert.ToString(((Dictionary<string, object>)result["item"])["id"]);
            }
            string url = api.OpenManagementUrl(); var cookies = new System.Net.CookieContainer();
            var entry = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url); entry.CookieContainer = cookies; entry.Timeout = 5000;
            using (var response = entry.GetResponse()) { }
            var authenticated = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(api.Address + "api/v1/settings"); authenticated.CookieContainer = cookies; authenticated.Timeout = 5000;
            using (var response = authenticated.GetResponse()) { }
            bool singleUse = false; var second = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url); second.Timeout = 5000;
            try { using (var response = second.GetResponse()) { } } catch (System.Net.WebException e) { using (var response = e.Response as System.Net.HttpWebResponse) singleUse = response != null && response.StatusCode == System.Net.HttpStatusCode.Unauthorized; }
            return new { id = id, sessionAuthenticated = true, singleUse = singleUse };
        }
        private void Exit() {
            if (exiting) return; exiting = true;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.UserPreferenceChanged -= PreferencesChanged;
            shellTimer.Stop(); shellTimer.Dispose();
            if (api != null) api.Dispose();
            if (surface != null) { surface.AllowClose = true; surface.Close(); surface.Dispose(); }
            if (tray != null) { tray.Visible = false; var menu = tray.ContextMenuStrip; tray.Dispose(); if (menu != null) menu.Dispose(); }
            if (trayIcon != null) trayIcon.Dispose();
            ExitThread();
        }
    }
    internal sealed class Surface : Form {
        internal IntPtr DesktopParent;
        internal bool AllowClose;
        private readonly PanelContext context;
        private readonly Screen screen;
        private readonly WebView2 web = new WebView2();
        private bool ready;
        private bool interacting, editorOpen;
        private string wallpaperUrl;
        internal Surface(PanelContext owner, Screen target) {
            context = owner; screen = target; Text = "桌面组件 v0.1";
            Icon = Program.CreateIcon(32);
            FormBorderStyle = Program.Preview ? FormBorderStyle.Sizable : FormBorderStyle.None;
            ShowInTaskbar = Program.Preview; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            int previewWidth = Math.Min(2200, (int)(target.WorkingArea.Width * .64));
            Bounds = Program.Preview ? new Rectangle(target.WorkingArea.Right - previewWidth - 40, target.WorkingArea.Top + 80, previewWidth, Math.Min(1450, target.WorkingArea.Height - 160)) : target.Bounds;
            BackColor = Color.FromArgb(25, 28, 33); web.Dock = DockStyle.Fill; Controls.Add(web);
            if (!Program.Preview) Region = new Region(Rectangle.Empty);
            Shown += async delegate { await Initialize(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!AllowClose) { e.Cancel = true; if (Program.Preview) Hide(); } };
        }
        protected override bool ShowWithoutActivation { get { return !Program.Preview; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; if (!Program.Preview) cp.ExStyle |= 0x08000000 | 0x00000080; return cp; } }
        protected override void WndProc(ref Message message) { if (!Program.Preview && message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } base.WndProc(ref message); }
        private async Task Initialize() {
            try {
                if (!Program.Preview) { DesktopParent = Desktop.Attach(Handle, screen.Bounds); Program.Log("Attached parent=" + DesktopParent + " screen=" + screen.DeviceName + " bounds=" + Bounds); }
                wallpaperUrl = Wallpaper.Build(screen.Bounds, Program.RuntimePath);
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.RuntimePath, "webview-profile"));
                await web.EnsureCoreWebView2Async(environment);
                web.DefaultBackgroundColor = Color.Transparent;
                var core = web.CoreWebView2;
                UpdateMemoryTarget();
                core.SetVirtualHostNameToFolderMapping("desk.local", Path.Combine(Program.Root, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
                Directory.CreateDirectory(Path.Combine(Program.Root, "assets"));
                core.SetVirtualHostNameToFolderMapping("assets.desk.local", Path.Combine(Program.Root, "assets"), CoreWebView2HostResourceAccessKind.Allow);
                core.SetVirtualHostNameToFolderMapping("cache.desk.local", Program.RuntimePath, CoreWebView2HostResourceAccessKind.Allow);
                core.Settings.AreDevToolsEnabled = Program.Preview;
                core.Settings.AreDefaultContextMenusEnabled = !Program.Preview; core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false;
                core.ContextMenuRequested += delegate(object sender, CoreWebView2ContextMenuRequestedEventArgs e) { e.Handled = true; if (!Program.Preview) Desktop.ShowDesktopMenu(Cursor.Position); };
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { Uri uri; if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) || uri.Host != "desk.local") e.Cancel = true; };
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                core.WebMessageReceived += Message;
                core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e) { Program.Log("WebView failed: " + e.ProcessFailedKind); };
                core.Navigate("https://desk.local/index.html");
                Desktop.ShowWindow(Handle, Program.Preview ? 5 : 4);
            } catch (Exception e) { Program.Log(e.ToString()); if (Program.Smoke) { File.WriteAllText(Path.Combine(context.Store.DirectoryPath, "smoke-error.txt"), e.ToString(), Encoding.UTF8); context.FinishedSmoke(); } else Dialogs.ShowError(e.Message, "桌面组件"); }
        }
        private Dictionary<string, object> EnvironmentInfo() { return new Dictionary<string, object> { {"monitor", screen.DeviceName}, {"wallpaper", wallpaperUrl}, {"desktop", !Program.Preview}, {"smoke", Program.Smoke}, {"apiSmoke",Program.Smoke && Program.ApiPort.HasValue}, {"screenWidth", screen.Bounds.Width}, {"screenHeight", screen.Bounds.Height} }; }
        internal void SendCommand(string command) { if (Program.Preview && !Visible) { Show(); Desktop.ShowWindow(Handle, 5); } if (ready) Send(new { type = "command", command = command }); }
        private void Send(object message) { if (!web.IsDisposed && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(Store.Json.Serialize(message)); }
        private void UpdateMemoryTarget() { try { if (web.CoreWebView2 != null) web.CoreWebView2.MemoryUsageTargetLevel = Program.Preview || interacting || editorOpen ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low; } catch (Exception e) { Program.Log("Memory target unavailable: " + e.Message); } }
        internal void RefreshWallpaper() { try { wallpaperUrl = Wallpaper.Build(screen.Bounds, Program.RuntimePath); if (ready) Send(new { type = "environment", environment = EnvironmentInfo() }); } catch (Exception e) { Program.Log(e.ToString()); } }
        internal void RefreshData(string key) { if (ready) lock (context.Store.SyncRoot) { object value = context.Store.Read(key); Send(new { type = "data", key = key, value = value, revision = context.Store.Revision(key) }); } }
        private void Message(object sender, CoreWebView2WebMessageReceivedEventArgs args) {
            if (!args.Source.StartsWith("https://desk.local/", StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, object> message = null;
            try {
                message = Store.Json.DeserializeObject(args.WebMessageAsJson) as Dictionary<string, object>;
                if (message == null) return; string type = Convert.ToString(message["type"]);
                string requestId = message.ContainsKey("requestId") ? Convert.ToString(message["requestId"]) : null;
                if (type == "ready") { ready = true; lock (context.Store.SyncRoot) Send(new { type = "init", data = context.Store.ReadAll(), revisions = context.Store.Revisions(), environment = EnvironmentInfo() }); }
                else if (type == "save") { string key = Convert.ToString(message["key"]); lock (context.Store.SyncRoot) { context.Store.Write(key, message["value"], message.ContainsKey("revision") ? Convert.ToString(message["revision"]) : null); Send(new { type = "result", requestId = requestId, value = context.Store.Revision(key) }); } }
                else if (type == "read") { string key = Convert.ToString(message["key"]); lock (context.Store.SyncRoot) { object value = context.Store.Read(key); Send(new { type = "result", requestId = requestId, value = new { value = value, revision = context.Store.Revision(key) } }); } }
                else if (type == "regions") UpdateRegion(message);
                else if (type == "activity") { interacting = message.ContainsKey("active") && message["active"] is bool && (bool)message["active"]; UpdateMemoryTarget(); }
                else if (type == "edit") {
                    BeginInvoke((Action)delegate {
                        editorOpen = true; UpdateMemoryTarget();
                        try { using (var editor = new Editor(Convert.ToString(message["title"]), (IList)message["fields"], Desktop.GetDpiForWindow(Handle) / 96F)) {
                            if (message.ContainsKey("previewWidget")) { editor.PreviewChanged += delegate(Dictionary<string, object> value) { Send(new { type = "edit-preview", requestId = requestId, value = value }); }; editor.StartPosition = FormStartPosition.Manual; editor.Location = new Point(screen.WorkingArea.Left + 30, screen.WorkingArea.Top + 60); }
                            editor.Shown += delegate { Desktop.SetForegroundWindow(editor.Handle); editor.Activate(); }; var result = editor.ShowDialog(Program.Preview ? (IWin32Window)this : Dialogs.Owner); Send(new { type = "result", requestId = requestId, value = result == DialogResult.OK ? editor.Result() : null });
                        } }
                        catch (Exception e) { Send(new { type = "result", requestId = requestId, error = e.Message }); }
                        finally { editorOpen = false; UpdateMemoryTarget(); }
                    });
                } else if (type == "pick-image") {
                    using (var dialog = new OpenFileDialog { Filter = "静态图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", Title = "选择组件背景" }) {
                        if (dialog.ShowDialog(Dialogs.Owner) != DialogResult.OK) { Send(new { type = "result", requestId = requestId, value = (string)null }); return; }
                        var info = new FileInfo(dialog.FileName); if (info.Length > 12 * 1024 * 1024) throw new IOException("图片请小于 12 MB。");
                        string folder = Path.Combine(Program.Root, "assets", "backgrounds"); Directory.CreateDirectory(folder);
                        string name = Guid.NewGuid().ToString("N") + Path.GetExtension(dialog.FileName).ToLowerInvariant(); File.Copy(dialog.FileName, Path.Combine(folder, name));
                        Send(new { type = "result", requestId = requestId, value = "https://assets.desk.local/backgrounds/" + name });
                    }
                } else if (type == "api-smoke" && Program.Smoke && Program.ApiPort.HasValue) {
                    BeginInvoke((Action)(async delegate { try { object result = await Task.Run((Func<object>)context.ProbeApi); Send(new { type = "result", requestId = requestId, value = result }); } catch (Exception e) { Send(new { type = "result", requestId = requestId, error = e.Message }); } }));
                } else if (type == "smoke-result" && Program.Smoke) { BeginInvoke((Action)(async delegate { await FinishSmoke(message); })); }
            } catch (Exception e) { Program.Log(e.ToString()); Send(new { type = "result", requestId = message != null && message.ContainsKey("requestId") ? message["requestId"] : null, error = e.Message }); }
        }
        private void UpdateRegion(Dictionary<string, object> message) {
            if (Program.Preview) return;
            double viewportWidth = Convert.ToDouble(message["width"]), viewportHeight = Convert.ToDouble(message["height"]);
            if (viewportWidth <= 0 || viewportHeight <= 0) return;
            var region = new Region(); region.MakeEmpty();
            foreach (object item in (IList)message["rects"]) {
                var rect = (Dictionary<string, object>)item; double sx = (double)ClientSize.Width / viewportWidth, sy = (double)ClientSize.Height / viewportHeight;
                region.Union(new RectangleF((float)(Convert.ToDouble(rect["x"]) * sx), (float)(Convert.ToDouble(rect["y"]) * sy), (float)(Convert.ToDouble(rect["width"]) * sx), (float)(Convert.ToDouble(rect["height"]) * sy)));
            }
            var old = Region; Region = region; if (old != null) old.Dispose();
        }
        private bool CheckDesktopDialogs() {
            IntPtr desktop = Desktop.FindIconHost();
            if (desktop == IntPtr.Zero || !Desktop.IsWindowEnabled(desktop)) throw new InvalidOperationException("桌面在验证开始前已被禁用。");
            bool during = false, owned = false, preview = false;
            var fields = new object[] { new Dictionary<string,object> { {"name","numberWeight"},{"label","数字粗细"},{"type","range"},{"min",100},{"max",900},{"step",100},{"value",600} } };
            try {
                using (var editor = new Editor("桌面输入回归验证",fields,Desktop.GetDpiForWindow(Handle)/96F))
                using (var timer = new System.Windows.Forms.Timer { Interval = 150 }) {
                    editor.PreviewChanged += delegate(Dictionary<string,object> values) { preview = Convert.ToInt32(values["numberWeight"]) == 700; };
                    editor.Shown += delegate { timer.Start(); };
                    timer.Tick += delegate {
                        timer.Stop();
                        try {
                            during = Desktop.IsWindowEnabled(desktop);
                            Program.Log("Dialog probe: editorOwner="+Desktop.GetWindow(editor.Handle,4)+" expected="+Dialogs.Owner.Handle+" ownerParent="+Desktop.GetParent(Dialogs.Owner.Handle)+" ownerOwner="+Desktop.GetWindow(Dialogs.Owner.Handle,4));
                            uint ownerProcess; Desktop.GetWindowThreadProcessId(Dialogs.Owner.Handle,out ownerProcess);
                            owned = Desktop.GetWindow(editor.Handle,4) == Dialogs.Owner.Handle && Desktop.GetParent(Dialogs.Owner.Handle) == IntPtr.Zero && ownerProcess == System.Diagnostics.Process.GetCurrentProcess().Id;
                            Action<Control> adjust = null; adjust = delegate(Control control) { var slider = control as TrackBar; if (slider != null) slider.Value = 700; foreach (Control child in control.Controls) adjust(child); }; adjust(editor);
                        } finally { editor.DialogResult = DialogResult.Cancel; editor.Close(); }
                    };
                    editor.ShowDialog(Dialogs.Owner);
                }
                using (var icon = Program.CreateIcon(16)) if (icon.Width != 16 || icon.Height != 16) throw new Exception("托盘图标尺寸错误。");
                if (!during || !owned || !preview || !Desktop.IsWindowEnabled(desktop)) throw new Exception("弹窗输入验证失败：during="+during+", owned="+owned+", preview="+preview+", after="+Desktop.IsWindowEnabled(desktop));
                return true;
            } finally {
                // The test must restore its own shell-state impact even on a regression.
                if (Desktop.IsWindow(desktop) && !Desktop.IsWindowEnabled(desktop)) Desktop.EnableWindow(desktop,true);
            }
        }
        private async Task FinishSmoke(Dictionary<string, object> message) {
            try {
                bool desktopDialogs = CheckDesktopDialogs();
                using (var stream = new FileStream(Path.Combine(context.Store.DirectoryPath, "surface.png"), FileMode.Create)) await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                var result = new Dictionary<string, object> { {"frontend", message["value"]}, {"desktopDialogs",desktopDialogs}, {"desktopAttached", Program.Preview ? false : Desktop.GetParent(Handle) == DesktopParent && DesktopParent != IntPtr.Zero}, {"visible", Desktop.IsWindowVisible(Handle)}, {"parent", DesktopParent.ToInt64()}, {"monitorCount", Screen.AllScreens.Length}, {"clientWidth", ClientSize.Width}, {"clientHeight", ClientSize.Height}, {"outsideRegion", Region == null ? false : !Region.IsVisible(8,8)}, {"processId", System.Diagnostics.Process.GetCurrentProcess().Id} };
                File.WriteAllText(Path.Combine(context.Store.DirectoryPath, "smoke-result.json"), Store.Json.Serialize(result), new UTF8Encoding(false));
            } catch (Exception e) { File.WriteAllText(Path.Combine(context.Store.DirectoryPath, "smoke-error.txt"), e.ToString(), Encoding.UTF8); }
            context.FinishedSmoke();
        }
    }
}
