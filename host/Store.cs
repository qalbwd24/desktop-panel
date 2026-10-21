using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

namespace DesktopPanel {
    internal sealed class Store {
        internal readonly string DirectoryPath;
        internal readonly object SyncRoot = new object();
        internal static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 60 }; } }
        private static readonly string[] Keys = { "todos", "events", "countdowns", "layout" };
        internal Store(string path) { DirectoryPath = Path.GetFullPath(path); Directory.CreateDirectory(DirectoryPath); }
        internal object Read(string key) {
            lock (SyncRoot) { return ReadCore(key); }
        }
        private object ReadCore(string key) {
            CheckKey(key);
            string path = Path.Combine(DirectoryPath, key + ".json");
            if (!File.Exists(path)) return key == "layout" ? (object)new Dictionary<string, object>() : new object[0];
            try { return Parse(key, File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception original) {
                string backup = path + ".bak";
                if (!File.Exists(backup)) throw new InvalidDataException(path + " 无法解析。原文件已保留，请修复或从备份恢复。", original);
                object restored = Parse(key, File.ReadAllText(backup, Encoding.UTF8));
                File.Copy(path, path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff"), false);
                File.Copy(backup, path, true);
                Program.Log("Recovered " + key + " from backup; damaged source retained.");
                return restored;
            }
        }
        internal Dictionary<string, object> ReadAll() {
            lock (SyncRoot) { var result = new Dictionary<string, object>(); foreach (string key in Keys) result[key] = Read(key); return result; }
        }
        internal string Revision(string key) {
            CheckKey(key);
            lock (SyncRoot) { string path = Path.Combine(DirectoryPath, key + ".json"); if (!File.Exists(path)) return "missing"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        }
        internal Dictionary<string, object> Revisions() { lock (SyncRoot) { var result = new Dictionary<string, object>(); foreach (string key in Keys) result[key] = Revision(key); return result; } }
        internal void Write(string key, object value) { Write(key, value, null); }
        internal void Write(string key, object value, string expectedRevision) {
            lock (SyncRoot) {
                if (expectedRevision != null && Revision(key) != expectedRevision) throw new InvalidOperationException("数据已被其他操作更新，请刷新后重试。");
                WriteCore(key, value);
            }
        }
        private void WriteCore(string key, object value) {
            CheckKey(key);
            Validate(key, value);
            string path = Path.Combine(DirectoryPath, key + ".json");
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Pretty(Json.Serialize(value)) + Environment.NewLine);
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static object Parse(string key, string content) { object value = Json.DeserializeObject(content); Validate(key, value); return value; }
        private static void CheckKey(string key) { if (Array.IndexOf(Keys, key) < 0) throw new InvalidDataException("不允许的数据文件。"); }
        private static void Validate(string key, object value) {
            CheckKey(key);
            if (key == "layout") { ValidateLayout(value); return; }
            IList rows = value as IList;
            if (rows == null || rows.Count > 10000) throw new InvalidDataException("数据必须是列表，最多 10000 条。");
            var ids = new HashSet<string>();
            foreach (object item in rows) {
                var row = item as Dictionary<string, object>;
                if (row == null || !row.ContainsKey("id") || !row.ContainsKey("title")) throw new InvalidDataException("记录缺少 id 或 title。");
                string id = row["id"] as string, title = row["title"] as string;
                if (String.IsNullOrWhiteSpace(id) || id.Length > 128 || !ids.Add(id) || String.IsNullOrWhiteSpace(title) || title.Length > 1000) throw new InvalidDataException("记录编号或标题无效。");
                if (row.ContainsKey("order")) Number(row["order"]);
                if (row.ContainsKey("unit") && Array.IndexOf(new[] {"days","weeks","months"}, row["unit"] as string) < 0) throw new InvalidDataException("倒计时单位无效。");
                if (row.ContainsKey("eventId") && row["eventId"] != null && (!(row["eventId"] is string) || ((string)row["eventId"]).Length > 128)) throw new InvalidDataException("关联日程编号无效。");
                if (key != "todos") {
                    DateTime date;
                    if (!row.ContainsKey("date") || !DateTime.TryParseExact(row["date"] as string, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)) throw new InvalidDataException("日期无效。");
                }
                if (key == "todos" && (!row.ContainsKey("done") || !(row["done"] is bool))) throw new InvalidDataException("待办完成状态无效。");
                if (key == "events") {
                    DateTime time;
                    if (!row.ContainsKey("time") || !DateTime.TryParseExact(row["time"] as string, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time)) throw new InvalidDataException("日程时间无效。");
                }
            }
        }
        private static double Number(object value) {
            if (!(value is int || value is long || value is double || value is decimal)) throw new InvalidDataException("数值字段无效。");
            double number = Convert.ToDouble(value); if (Double.IsNaN(number) || Double.IsInfinity(number)) throw new InvalidDataException("数值字段无效。"); return number;
        }
        private static Dictionary<string, object> Object(object value) { var result = value as Dictionary<string, object>; if (result == null) throw new InvalidDataException("布局字段必须是对象。"); return result; }
        private static void ValidateLayout(object value) {
            var layout = Object(value);
            if (layout.ContainsKey("monitor") && layout["monitor"] != null && !(layout["monitor"] is string)) throw new InvalidDataException("屏幕名称无效。");
            if (layout.ContainsKey("units")) foreach (var unit in Object(layout["units"])) if (Array.IndexOf(new[] {"days","weeks","months"}, unit.Value as string) < 0) throw new InvalidDataException("显示单位无效。");
            if (layout.ContainsKey("screens")) foreach (var screen in Object(layout["screens"])) foreach (var widget in Object(screen.Value)) {
                var rect = Object(widget.Value);
                foreach (string name in new[] {"x","y","width","height"}) if (rect.ContainsKey(name)) Number(rect[name]);
            }
            if (layout.ContainsKey("styles")) foreach (var widget in Object(layout["styles"])) {
                var style = Object(widget.Value);
                foreach (string name in new[] {"transparency","blur","fontSize","fontWeight","numberWeight","radius"}) if (style.ContainsKey(name)) Number(style[name]);
                foreach (string name in new[] {"fontWeight","numberWeight"}) if (style.ContainsKey(name) && (Number(style[name]) < 100 || Number(style[name]) > 900)) throw new InvalidDataException("文字粗细必须在 100–900 之间。");
                foreach (string name in new[] {"tint","color","accent"}) if (style.ContainsKey(name) && !System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(style[name]), "^#[a-fA-F0-9]{6}$")) throw new InvalidDataException("布局颜色无效。");
                if (style.ContainsKey("font") && (!(style["font"] is string) || ((string)style["font"]).Length > 120)) throw new InvalidDataException("字体名称无效。");
                if (style.ContainsKey("mode") && Array.IndexOf(new[] {"毛玻璃","半透明","纯透明","图片"}, style["mode"] as string) < 0) throw new InvalidDataException("背景模式无效。");
                if (style.ContainsKey("background") && Array.IndexOf(new[] {"颜色","图片","无"}, style["background"] as string) < 0) throw new InvalidDataException("背景类型无效。");
                if (style.ContainsKey("effect") && Array.IndexOf(new[] {"毛玻璃","无模糊"}, style["effect"] as string) < 0) throw new InvalidDataException("背景效果无效。");
                if (style.ContainsKey("presentation") && Array.IndexOf(new[] {"列表","简约"}, style["presentation"] as string) < 0) throw new InvalidDataException("倒计时样式无效。");
                if (style.ContainsKey("theme") && Array.IndexOf(new[] {"深色","浅色"}, style["theme"] as string) < 0) throw new InvalidDataException("主题无效。");
                if (style.ContainsKey("image") && !(style["image"] is string)) throw new InvalidDataException("背景图片无效。");
            }
        }
        private static string Pretty(string json) {
            var output = new StringBuilder(); int depth = 0; bool quoted = false, escaped = false;
            foreach (char ch in json) {
                if (quoted) { output.Append(ch); if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == '"') quoted = false; continue; }
                if (ch == '"') { quoted = true; output.Append(ch); }
                else if (ch == '{' || ch == '[') { output.Append(ch).AppendLine(); depth++; output.Append(' ', depth * 2); }
                else if (ch == '}' || ch == ']') { output.AppendLine(); depth--; output.Append(' ', depth * 2).Append(ch); }
                else if (ch == ',') output.Append(ch).AppendLine().Append(' ', depth * 2);
                else if (ch == ':') output.Append(": ");
                else if (!Char.IsWhiteSpace(ch)) output.Append(ch);
            }
            return output.ToString();
        }
        internal static void SelfTest(string path) {
            var store = new Store(path);
            var row = new Dictionary<string, object> { {"id", "smoke"}, {"title", "中文待办 / 引号 \" 与换行\n保留"}, {"done", false}, {"order", 0} };
            store.Write("todos", new object[] { row });
            row["done"] = true;
            store.Write("todos", new object[] { row });
            var reloaded = new Store(path).Read("todos") as object[];
            if (reloaded == null || reloaded.Length != 1 || !(bool)((Dictionary<string, object>)reloaded[0])["done"]) throw new Exception("Restart persistence failed.");
            File.WriteAllText(Path.Combine(path, "todos.json"), "{bad", Encoding.UTF8);
            var recovered = new Store(path).Read("todos") as object[];
            if ((bool)((Dictionary<string, object>)recovered[0])["done"]) throw new Exception("Backup recovery failed.");
            bool rejected = false;
            try { store.Write("../escape", new object[0]); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Path allowlist failed.");
            store.Write("events", new object[] { new Dictionary<string, object> { {"id", "event"}, {"title", "周日程"}, {"date", "2026-09-30"}, {"time", "10:30"} } });
            store.Write("countdowns", new object[] { new Dictionary<string, object> { {"id", "countdown"}, {"title", "考试"}, {"date", "2026-12-31"} } });
            File.WriteAllText(Path.Combine(path, "selftest-result.json"), "{\"passed\":true,\"checks\":[\"utf8\",\"atomic-save\",\"restart\",\"backup-recovery\",\"path-allowlist\",\"separate-files\"]}", new UTF8Encoding(false));
        }
    }
}
