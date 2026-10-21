using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Microsoft.Win32;

namespace DesktopPanel {
    internal sealed class Startup {
        internal const string ValueName = "DesktopWallpaperWidgets";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
        private readonly string keyPath, preferencePath, command, executable, shortcutPath, approvalPath;
        internal Startup(string folder,string executable,string registryPath = RunKey,string shortcut = null,string approval = ApprovalKey) {
            this.executable = Path.GetFullPath(executable); keyPath = registryPath; approvalPath = approval;
            preferencePath = Path.Combine(folder,"startup.json"); command = "\"" + this.executable + "\" --startup";
            shortcutPath = shortcut ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"DesktopWallpaperWidgets.lnk");
        }
        private bool BlockedByWindows { get { using (var key = Registry.CurrentUser.OpenSubKey(approvalPath)) {
            var state = key == null ? null : key.GetValue(Path.GetFileName(shortcutPath)) as byte[];
            return state != null && state.Length > 0 && (state[0] == 3 || state[0] == 7);
        } } }
        // WSH is included in Windows. Explicit working directory also works with Chinese paths.
        private bool Shortcut(bool write) {
            if (!write && !File.Exists(shortcutPath)) return false;
            object shell = null, link = null;
            try {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic wsh = shell; link = wsh.CreateShortcut(shortcutPath); dynamic item = link;
                if (!write) return String.Equals((string)item.TargetPath,executable,StringComparison.OrdinalIgnoreCase) && (string)item.Arguments == "--startup";
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
                item.TargetPath = executable; item.Arguments = "--startup";
                item.WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable),".."));
                item.IconLocation = executable + ",0"; item.Description = "桌面组件：待办、周日程和倒计时"; item.WindowStyle = 7;
                item.Save(); return true;
            } finally {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
        internal bool Enabled { get { return Shortcut(false) && !BlockedByWindows; } }
        internal void Initialize() {
            bool desired = true;
            if (File.Exists(preferencePath)) {
                var saved = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(preferencePath,Encoding.UTF8));
                if (saved == null || !saved.ContainsKey("enabled") || !(saved["enabled"] is bool)) throw new IOException("自启配置格式异常，已保留。");
                desired = (bool)saved["enabled"];
            }
            // Respect an off choice; migrate an owned legacy Run entry on manual launch.
            // A disabled Windows Startup app is never silently re-enabled.
            if (desired && Shortcut(false) && BlockedByWindows) return;
            SetEnabled(desired);
        }
        internal void SetEnabled(bool enabled) {
            if (File.Exists(shortcutPath) && !Shortcut(false)) throw new IOException("同名启动快捷方式不属于当前程序，已保留。");
            if (enabled && BlockedByWindows) throw new IOException("Windows 已禁用此启动项，请在 设置 → 应用 → 启动 中启用桌面组件。");
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) {
                object previous = key.GetValue(ValueName);
                if (previous != null && (!(previous is string) || !String.Equals((string)previous,command,StringComparison.OrdinalIgnoreCase))) throw new IOException("同名启动项不属于当前程序，已保留。");
                byte[] before = File.Exists(shortcutPath) ? File.ReadAllBytes(shortcutPath) : null;
                string temporary = preferencePath + ".tmp-" + Guid.NewGuid().ToString("N");
                try {
                    if (enabled) Shortcut(true); else if (before != null) File.Delete(shortcutPath);
                    key.DeleteValue(ValueName,false);
                    Directory.CreateDirectory(Path.GetDirectoryName(preferencePath));
                    File.WriteAllText(temporary,"{\"enabled\":"+(enabled?"true":"false")+"}"+Environment.NewLine,new UTF8Encoding(false));
                    if (File.Exists(preferencePath)) File.Replace(temporary,preferencePath,preferencePath+".bak",true); else File.Move(temporary,preferencePath);
                } catch {
                    if (before == null) { if (File.Exists(shortcutPath)) File.Delete(shortcutPath); } else File.WriteAllBytes(shortcutPath,before);
                    if (previous == null) key.DeleteValue(ValueName,false); else key.SetValue(ValueName,(string)previous,RegistryValueKind.String);
                    throw;
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        internal static void SelfTest(string folder,string executable) {
            string testKey = @"Software\PersonalDesktopPanel\Verification\" + Guid.NewGuid().ToString("N");
            string link = Path.Combine(folder,"Startup","DesktopWallpaperWidgets.lnk");
            var startup = new Startup(folder,executable,testKey,link,testKey+@"\Approval");
            try {
                startup.Initialize(); if (!startup.Enabled) throw new Exception("默认自启注册失败。");
                startup.SetEnabled(false); startup.Initialize(); if (startup.Enabled || File.Exists(link)) throw new Exception("取消自启后被重新开启。");
                startup.SetEnabled(true); if (!startup.Enabled) throw new Exception("重新启用失败。");
                File.Delete(link);
                using (var key = Registry.CurrentUser.CreateSubKey(testKey)) key.SetValue(ValueName,"\""+Path.GetFullPath(executable)+"\" --startup");
                startup.Initialize();
                using (var key = Registry.CurrentUser.OpenSubKey(testKey)) if (!startup.Enabled || key.GetValue(ValueName)!=null) throw new Exception("旧自启迁移失败。");
                using (var key = Registry.CurrentUser.CreateSubKey(testKey+@"\Approval")) key.SetValue(Path.GetFileName(link),new byte[]{3,0,0,0},RegistryValueKind.Binary);
                startup.Initialize(); if (startup.Enabled) throw new Exception("Windows 禁用状态未生效。");
                bool rejected = false; try { startup.SetEnabled(true); } catch (IOException) { rejected=true; }
                if (!rejected) throw new Exception("不应覆盖 Windows 的禁用选择。");
                using (var key = Registry.CurrentUser.OpenSubKey(testKey+@"\Approval",true)) key.DeleteValue(Path.GetFileName(link));
                using (var key = Registry.CurrentUser.CreateSubKey(testKey)) key.SetValue(ValueName,"other-application.exe");
                byte[] before = File.ReadAllBytes(link); rejected=false;
                try { startup.SetEnabled(false); } catch (IOException) { rejected=true; }
                if (!rejected || Convert.ToBase64String(before)!=Convert.ToBase64String(File.ReadAllBytes(link))) throw new Exception("其他程序启动项未保护。");
                File.WriteAllText(Path.Combine(folder,"startup-test-result.json"),"{\"passed\":true,\"checks\":[\"default-on\",\"disable-persists\",\"enable\",\"shortcut-path\",\"legacy-migration\",\"windows-disabled\",\"foreign-entry-preserved\"]}",new UTF8Encoding(false));
            } finally { Registry.CurrentUser.DeleteSubKeyTree(testKey,false); if (File.Exists(link)) File.Delete(link); }
        }
    }
}
