# Astra 规划，6.1 Sol 实现

在同一个聊天里切换模型可保留已有上下文，不需要另建项目。是否出现具体模型选项，以本机模型选择器和账号权限为准；这里没有自动修改当前聊天模型。

1. 从模型选择器选 GPT-6 Astra，启用 Plan 模式（官方介绍支持 `/plan`）。
2. 发送下面的规划提示。
3. 确认计划后，在同一聊天切换 GPT-6.1 Sol，返回普通执行模式，发送执行提示。

规划提示：

```text
只规划，暂不修改程序。项目在本目录，以下步骤均在本目录内执行。
先阅读 AGENTS.md、README.md、DEVELOPMENT.md 和当前实现。
复核桌面嵌入、输入焦点、外接屏/DPI、数据保存和资源占用的风险。
保留已工作的待办、周日程、倒计时及独立外观。
不要加入日记联动、云同步或复杂插件系统。
给出下一阶段最小改动、验收条件及真实设备验证项。
明确哪些需要实测，哪些已有证据，不要重建整个项目。
```

执行提示：

```text
按刚才确认的计划逐步实现。所有项目文件和测试数据留在本目录。
每个阶段先做最小改动，验证通过再继续。
保留 data/ 和图片，不在生产数据上测试。
运行 build.ps1、node --test tests/*.test.mjs 和 verify.ps1，并记录验证边界。
遇到桌面层/DPI/中文输入问题先复现，再修改。
完成后更新 DEVELOPMENT.md，给出启动方式和未验证的设备行为。
```

这不是必须的固定分工。边界明确的功能可以直接用 Sol 完成；涉及桌面层、跨屏或数据管理的取舍，再用 Astra 复核规划比较有价值。

官方资料：[Plan 模式与分阶段开发](https://developers.openai.com/blog/run-long-horizon-tasks-with-codex)、[模型选择](https://developers.openai.com/api/docs/guides/model-selection)、[GPT-6.1 Sol](https://developers.openai.com/api/docs/models/gpt-6.1-sol)。这些资料不保证某个账号或当前客户端一定展示相同的模型选项。
