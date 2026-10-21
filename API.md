# 本机数据接口 v1

## 为什么同时有 JSON 和 API

JSON 定义数据并方便备份；API 是程序和未来 LLM 修改数据的受控入口。运行中推荐调用接口，不让模型直接改文件。第一版不调用 LLM，不保存服务商密钥，不访问日记。

待办、日程、倒计时各存一份 JSON 数组，布局是 JSON 对象。接口响应包含 `schemaVersion: 1`、数据和版本。未来日记可以共用编号、日期、标题与时间戳等基础字段，正文仍保留自己的格式；不为了统一而把所有内容塞入同一个文件。

## 地址和认证

默认 `http://127.0.0.1:18877/api/v1`，只监听本机。配置在 `data/api.json`：`enabled`、`port`、`token`，退出后修改再启动生效。`token` 是本地接口访问密钥，**不是** LLM 服务商 API Key。服务商地址、模型、服务商密钥和工具调用适配将在后续单独实现。

外部调用使用 `Authorization: Bearer <本地接口访问密钥>`。不把密钥放在 URL 中。托盘后台用有效期两分钟、只能兑换一次的入口换取八小时 HttpOnly / SameSite=Strict 会话，后台仍只访问本机。跨来源网页不获准读取接口。

## 路由

| 方法 | 路径 | 作用 |
|---|---|---|
| GET | `/api/health` | 程序版本，无需密钥 |
| GET | `/api/v1/todos` | 待办列表 |
| POST | `/api/v1/todos` | 新增待办 |
| PATCH | `/api/v1/todos/{id}` | 修改指定待办 |
| DELETE | `/api/v1/todos/{id}` | 删除指定待办 |
| GET / POST / PATCH / DELETE | `/api/v1/events` 或 `/events/{id}` | 日程同样操作 |
| GET / POST / PATCH / DELETE | `/api/v1/countdowns` 或 `/countdowns/{id}` | 倒计时同样操作 |
| GET / PUT | `/api/v1/layout` | 读取/替换布局，PUT 必须有 If-Match |
| GET | `/api/v1/settings` | 已认证后读取本机接口配置 |

GET 列表响应是 `{ "schemaVersion": 1, "items": [...], "revision": "..." }`，同时返回 ETag。修改成功是 `{ "ok": true, "item": {...}, "revision": "..." }`；删除的 item 为 null。

POST/PATCH/DELETE 推荐带 `If-Match: "<刚读取的 revision>"`；版本不一致返回 409，重新读取后再决定修改。没有 If-Match 的单项修改以请求到达时的最新记录为基础。PUT 替换布局不带 If-Match 返回 428。桌面组件保存整份列表时也执行版本检查。

## 数据字段

| 类型 | 必要业务字段 | 可选字段 |
|---|---|---|
| todos | `title` 文本，`done` 布尔 | `order` 顺序，`completedAt` 完成时间 |
| events | `title`，`date: YYYY-MM-DD`，`time: HH:mm` | — |
| countdowns | `title`，`date: YYYY-MM-DD` | `unit: days / weeks / months`，`eventId` 关联日程编号或 null |

POST 会生成 `id`、UTC `createdAt`、`updatedAt`。新增待办默认 `done: false`，顺序放在末尾。PATCH 保留未修改字段，不能改 id/createdAt，更新 updatedAt。桌面新增/编辑也维护创建和更新时间；早期手工导入记录允许缺少时间戳。

日期/时间按本地日历理解；时间戳使用 UTC ISO 8601。标题最多 1000 字符、每类最多 10000 条、单次请求最多 1 MB。文本作为纯文本显示，不执行 HTML。

关联倒计时的 GET 返回原始记录，客户端通过 eventId 从 events 读取当前名称和日期；title/date 是建立或编辑关联时保存的备用值。修改关联日程即可更新桌面与后台的显示，不需要改两份业务数据。取消关联请 PATCH eventId 为 null，并填写需要保留的 title/date。来源删除时显示备用值及缺失标记，不自动删除倒计时。后台与组件都使用 ui/core.js 的 resolveCountdown 和 countdownDistance。

layout.styles 保存各卡片 theme（深色/浅色）、fontSize、color、tint、accent、透明度等；layout.screens 保存屏幕布局；layout.units.schedule 保存日程距离显示单位。units 的有效值都是 days/weeks/months。

背景类型 `background` 为 `颜色` / `图片` / `无`，模糊效果 `effect` 为 `毛玻璃` / `无模糊`，`transparency` 为 0–100（100 完全透明）。`image` 独立保存，切换背景或透明度不清除。倒计时 `presentation` 为 `列表` / `简约`。旧 `mode` 字段仍能读取，新界面会转换为独立背景与效果字段；同时存在时以新字段为准。外观编辑过程的预览不写入 JSON，也不改变 API 版本，保存后才生效。

`fontWeight` 控制文字粗细，默认 500；`numberWeight` 控制倒计时数字粗细，默认 600。有效数值 100–900，UI 每 100 一档。旧配置没有这些字段时使用默认值，不修改原文件。

## PowerShell 示例

在项目根目录执行，读取密钥但不要输出它：

```powershell
$apiConfig = Get-Content -LiteralPath '.\data\api.json' -Encoding UTF8 -Raw | ConvertFrom-Json
$apiBase = 'http://127.0.0.1:' + $apiConfig.port + '/api/v1'
$apiHeaders = @{ Authorization = 'Bearer ' + $apiConfig.token }
$snapshot = Invoke-RestMethod -Uri ($apiBase + '/todos') -Headers $apiHeaders
$apiHeaders['If-Match'] = '"' + $snapshot.revision + '"'
$body = @{ title = '看第五章' } | ConvertTo-Json
Invoke-RestMethod -Uri ($apiBase + '/todos') -Method Post -Headers $apiHeaders `
  -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

## 未来 LLM 怎么连接

让模型通过明确的读取、新增、编辑和删除工具调用本地接口。模型应先读取记录和版本，再提交最小修改；服务商 API Key 只供调用模型，本地访问密钥只供调用上述数据接口。涉及日记时，再增加按日期或选定范围的读取工具和独立写入接口，不直接开放任意磁盘文件。

API 只在桌面程序运行时可用。当前没有历史审计、撤销栈、全文检索或 MCP 服务；这些没有预先加入第一版。
