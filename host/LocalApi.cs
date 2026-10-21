using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DesktopPanel {
    internal sealed class LocalApi : IDisposable {
        private readonly Store store;
        private readonly Action<string> changed;
        private HttpListener listener;
        private readonly Dictionary<string, DateTime> oneTime = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> sessions = new Dictionary<string, DateTime>();
        internal readonly int Port;
        internal readonly string Token;
        internal string Address { get { return "http://127.0.0.1:" + Port + "/"; } }
        internal LocalApi(Store source, Action<string> onChanged, int? overridePort) {
            store = source; changed = onChanged;
            string settingsPath = Path.Combine(store.DirectoryPath, "api.json");
            Dictionary<string, object> config;
            if (File.Exists(settingsPath)) config = Store.Json.DeserializeObject(File.ReadAllText(settingsPath, Encoding.UTF8)) as Dictionary<string, object>;
            else { config = new Dictionary<string, object> { {"schemaVersion",1},{"enabled",true},{"port",18877},{"token",Secret()} }; File.WriteAllText(settingsPath, Store.Json.Serialize(config), new UTF8Encoding(false)); }
            if (config == null || !config.ContainsKey("token") || Convert.ToString(config["token"]).Length < 24) throw new InvalidDataException("data/api.json 的访问密钥无效。");
            Port = overridePort ?? Convert.ToInt32(config["port"]); Token = Convert.ToString(config["token"]);
            if (Port < 1024 || Port > 65535) throw new InvalidDataException("本地 API 端口必须在 1024–65535 之间。");
            if (config.ContainsKey("enabled") && !(bool)config["enabled"]) return;
            listener = new HttpListener(); listener.Prefixes.Add(Address); listener.Start();
            Task.Run((Func<Task>)Loop); Program.Log("Local API started at " + Address);
        }
        private static string Secret() { byte[] bytes = new byte[32]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes); return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_'); }
        internal string OpenManagementUrl() {
            if (listener == null) throw new InvalidOperationException("本地 API 未启用。请检查 data/api.json。 ");
            string code = Guid.NewGuid().ToString("N"); lock (oneTime) oneTime[code] = DateTime.UtcNow.AddMinutes(2);
            return Address + "session/" + code;
        }
        private async Task Loop() {
            var activeListener = listener;
            while (activeListener != null && activeListener.IsListening) {
                HttpListenerContext context;
                try { context = await activeListener.GetContextAsync(); } catch { return; }
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { Handle(context); });
            }
        }
        private bool Authorized(HttpListenerRequest request) {
            if (request.Headers["Authorization"] == "Bearer " + Token) return true;
            Cookie cookie = request.Cookies["desktop-panel-session"];
            if (cookie == null) return false;
            lock (sessions) { DateTime expires; return sessions.TryGetValue(Uri.UnescapeDataString(cookie.Value), out expires) && expires > DateTime.UtcNow; }
        }
        private void Handle(HttpListenerContext context) {
            try {
                var request = context.Request; string path = request.Url.AbsolutePath, method = request.HttpMethod;
                if (request.Headers["Origin"] != null && request.Headers["Origin"] != Address.TrimEnd('/')) { Reply(context, 403, new { error = "来源不允许" }); return; }
                if (path == "/api/health" && method == "GET") { Reply(context, 200, new { name = "DesktopPanel", version = "0.1.0", schemaVersion = 1 }); return; }
                if (path.StartsWith("/session/", StringComparison.Ordinal) && method == "GET") {
                    string code = path.Substring(9); bool valid; lock (oneTime) { DateTime expires; valid = oneTime.TryGetValue(code, out expires) && expires > DateTime.UtcNow; oneTime.Remove(code); }
                    if (!valid) { Reply(context, 401, new { error = "入口已过期，请从托盘重新打开" }); return; }
                    string session = Secret(); lock (sessions) sessions[session] = DateTime.UtcNow.AddHours(8);
                    context.Response.Headers.Add("Set-Cookie", "desktop-panel-session=" + Uri.EscapeDataString(session) + "; Path=/; HttpOnly; SameSite=Strict");
                    context.Response.Redirect(Address + "admin.html"); context.Response.Close(); return;
                }
                if (method == "GET" && (path == "/" || path == "/admin.html" || path == "/admin.js" || path == "/admin.css" || path == "/core.js" || path == "/bridge.js")) { Static(context, path == "/" ? "admin.html" : path.Substring(1)); return; }
                if (!path.StartsWith("/api/v1/", StringComparison.Ordinal)) { Reply(context, 404, new { error = "接口不存在" }); return; }
                if (!Authorized(request)) { Reply(context, 401, new { error = "需要本地 API 访问密钥" }); return; }
                string[] parts = path.Substring(8).Split('/'); string key = parts[0];
                if (key == "settings" && method == "GET") { Reply(context, 200, new { schemaVersion = 1, baseUrl = Address + "api/v1", token = Token, port = Port }); return; }
                if (Array.IndexOf(new[] {"todos","events","countdowns","layout"}, key) < 0 || parts.Length > 2) { Reply(context, 404, new { error = "数据类型不存在" }); return; }
                if (method == "GET" && parts.Length == 1) {
                    lock (store.SyncRoot) { object items = store.Read(key); string revision = store.Revision(key); context.Response.Headers["ETag"] = "\"" + revision + "\""; Reply(context, 200, new { schemaVersion = 1, items = items, revision = revision }); } return;
                }
                if (key == "layout") {
                    if (method != "PUT" || parts.Length != 1) { Reply(context,405,new {error="布局仅支持 GET 和 PUT"}); return; }
                    object value = Body(request); string expected = Expected(request); if (expected == null) { Reply(context,428,new {error="替换布局必须提供 If-Match 版本"}); return; }
                    lock (store.SyncRoot) { if (store.Revision(key) != expected) { Reply(context,409,new {error="数据版本冲突，请刷新"}); return; } store.Write(key,value,expected); }
                    if (changed != null) changed(key); Reply(context,200,new {ok=true,revision=store.Revision(key)}); return;
                }
                if (!((method == "POST" && parts.Length == 1) || ((method == "PATCH" || method == "DELETE") && parts.Length == 2))) { Reply(context,405,new {error="支持 GET、POST、PATCH、DELETE"}); return; }
                object saved = null;
                lock (store.SyncRoot) {
                    string expected = Expected(request); if (expected != null && store.Revision(key) != expected) { Reply(context,409,new {error="数据版本冲突，请刷新"}); return; }
                    var rows = new List<object>(); foreach (object row in (IList)store.Read(key)) rows.Add(row);
                    if (method == "POST") {
                        var row = Body(request) as Dictionary<string, object>; if (row == null) throw new InvalidDataException("记录必须是对象。");
                        string now = DateTime.UtcNow.ToString("o"); row["id"] = Guid.NewGuid().ToString("N"); row["createdAt"] = now; row["updatedAt"] = now;
                        if (key == "todos") { if (!row.ContainsKey("done")) row["done"] = false; if (!row.ContainsKey("order")) { double last = -1; foreach (Dictionary<string,object> existing in rows) last = Math.Max(last, existing.ContainsKey("order") ? Convert.ToDouble(existing["order"]) : 0); row["order"] = last + 1; } if (row["done"] is bool && (bool)row["done"]) row["completedAt"] = now; }
                        rows.Add(row); saved = row;
                    } else {
                        string id = Uri.UnescapeDataString(parts[1]); int index = rows.FindIndex(row => Convert.ToString(((Dictionary<string,object>)row)["id"]) == id);
                        if (index < 0) { Reply(context,404,new {error="记录不存在"}); return; }
                        if (method == "DELETE") rows.RemoveAt(index);
                        else {
                            var patch = Body(request) as Dictionary<string,object>; if (patch == null) throw new InvalidDataException("修改内容必须是对象。");
                            var row = (Dictionary<string,object>)rows[index];
                            foreach (var pair in patch) { if (pair.Key == "id" || pair.Key == "createdAt") throw new InvalidDataException("不能修改编号或创建时间。"); row[pair.Key] = pair.Value; }
                            row["updatedAt"] = DateTime.UtcNow.ToString("o"); saved = row;
                            if (key == "todos" && patch.ContainsKey("done") && row["done"] is bool) row["completedAt"] = (bool)row["done"] ? row["updatedAt"] : null;
                        }
                    }
                    store.Write(key,rows.ToArray());
                }
                if (changed != null) changed(key); Reply(context,method=="POST"?201:200,new {ok=true,item=saved,revision=store.Revision(key)});
            } catch (InvalidDataException e) { Reply(context,400,new {error=e.Message}); }
            catch (ArgumentException e) { Reply(context,400,new {error=e.Message}); }
            catch (Exception e) { Program.Log("Local API: " + e.Message); Reply(context,500,new {error="操作失败，请检查本地日志"}); }
        }
        private static string Expected(HttpListenerRequest request) { string value=request.Headers["If-Match"]; return String.IsNullOrEmpty(value)?null:value.Trim('"'); }
        private static object Body(HttpListenerRequest request) {
            if (request.ContentLength64 < 0 || request.ContentLength64 > 1024 * 1024) throw new InvalidDataException("请求大小无效，限制 1 MB。");
            byte[] buffer = new byte[(int)request.ContentLength64]; int read=0;
            while (read < buffer.Length) { int count=request.InputStream.Read(buffer,read,buffer.Length-read); if(count==0)throw new InvalidDataException("请求内容不完整。"); read+=count; }
            return Store.Json.DeserializeObject(Encoding.UTF8.GetString(buffer));
        }
        private static void Reply(HttpListenerContext context, int status, object value) {
            try { byte[] bytes=new UTF8Encoding(false).GetBytes(Store.Json.Serialize(value)); context.Response.StatusCode=status; context.Response.ContentType="application/json; charset=utf-8"; context.Response.Headers["Cache-Control"]="no-store"; context.Response.ContentLength64=bytes.Length; context.Response.OutputStream.Write(bytes,0,bytes.Length); context.Response.Close(); } catch { }
        }
        private static void Static(HttpListenerContext context, string name) {
            byte[] bytes=File.ReadAllBytes(Path.Combine(Program.Root,"ui",name));
            context.Response.ContentType=name.EndsWith(".js")?"text/javascript; charset=utf-8":name.EndsWith(".css")?"text/css; charset=utf-8":"text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"]="no-store";
            context.Response.Headers["Content-Security-Policy"]="default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            context.Response.Headers["X-Content-Type-Options"]="nosniff";
            context.Response.ContentLength64=bytes.Length; context.Response.OutputStream.Write(bytes,0,bytes.Length); context.Response.Close();
        }
        public void Dispose() { var current=listener; listener=null; if(current!=null)current.Close(); }
    }
}
