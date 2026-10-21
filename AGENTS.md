# 桌面壁纸小组件

- 所有项目文件、依赖、输出和验证数据都保存在本目录。
- Windows / PowerShell；文本为 UTF-8，PowerShell 脚本保存 UTF-8 BOM。
- 首版：待办、周日程、倒计时。日记联动尚未设计，不读取或修改日记数据。
- 保持 C# 桌面宿主和普通 HTML/CSS/JS。新增组件放在 ui/widgets/，复用现有文件存储和编辑消息。
- 不加入账号、云同步、Electron 或复杂插件系统。已授权结构化数据和本地 CRUD API，未来 LLM 可调用；第一版不接入 LLM 服务商或保存其 API Key。
- 不创建全局键盘/鼠标钩子。主动编辑才取得输入焦点；组件外区域交还桌面。
- 真实桌面嵌入、Win+D、中文输入法、外接屏热插拔必须分别标注验证范围。
- 生产数据在 data/；测试必须传 --data-dir，使用 .verification/ 内隔离目录。
- 修改后运行 build.ps1、node --test tests/*.test.mjs 和 verify.ps1。保留 JSON 原件和 .bak。
- 本地 API 只监听 127.0.0.1，令牌认证，不开放任意文件路径、不启用跨域通配。API 令牌与未来 LLM API Key 分开。
