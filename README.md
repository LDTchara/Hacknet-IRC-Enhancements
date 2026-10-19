# HacknetIRCEnhancements

Hacknet IRC 聊天记录翻页功能。为原版 IRC 系统添加鼠标滚轮翻页和 Home/End 快速跳转，并修正负 Delay 时间戳。

A standalone IRC scrolling mod for Hacknet. Adds mouse wheel scrolling and Home/End jumps to the vanilla IRC log, plus a fix for negative-delay timestamps.

本模组功能来自 [TAXCore](https://github.com/Dsl114514/TAXCore) 的 IRC 翻页补丁，分离为独立模组后减轻了依赖，增加了日志裁剪和关键节流等改进。
This feature is extracted from [TAXCore](https://github.com/Dsl114514/TAXCore)'s IRC scroll patch, released standalone with log trimming and key throttle added.

> [!WARNING]
> 本项目部分文档与代码由 AI 辅助生成，可能存在不准确或不完整之处，请在使用前自行评估；若发现问题，欢迎通过 Issue 或 PR 指出，我们会及时修正。
> This project uses AI-assisted generation for parts of its documentation and code, which may be inaccurate or incomplete. Please evaluate before use, and feel free to report any issues via Issue or PR — we will fix them promptly.

---

## 功能 / Features

- **鼠标滚轮翻页** — 逐条滚动 IRC 聊天记录，与 TAXCore 方向一致
- **Home / End 键** — 快速跳到最老 / 最新消息
- **负 Delay 时间戳修复** — `Delay="-99"` 不再显示未来时间（KF 优先、独立备选）
- **日志裁剪** — 超过 10,000 条时自动裁剪最早的部分，防止文件无限增长
- **兼容性** — 检测 TAXCore（跳过翻页）和 KF（跳过时间戳修复）

---

## 安装 / Installation

1. 确保已安装 **Pathfinder** 框架
2. 将 `HacknetIRCEnhancements.dll` 放入 `BepInEx/plugins/`
3. 启动游戏，打开任意 IRC 聊天即生效

---

## 按键 / Controls

| 操作 | 效果 |
|------|------|
| 鼠标滚轮 / Mouse Wheel | 逐条滚动 / Scroll line by line |
| Home | 跳至最旧 / Jump to oldest |
| End | 跳至最新 / Jump to latest |

> 不使用 方向键 以避免与终端命令历史冲突。使用 PageUp/PageDown 因行数检测在高帧率下有兼容问题。
> Up/Down intentionally not used to avoid terminal command-history conflict. PageUp/PageDown omitted due to FPS-dependent line-counting edge cases.

---

## 兼容性 / Compatibility

- **KernelFix (KF)** — 同时加载时 IRC 时间戳修复由 KF 接管（KF 有配置开关），本模组跳过
- **TAXCore** — 同时加载时翻页由 TAXCore 接管，本模组只保留时间戳修复（TAXCore 不含此修复）
- 两个依赖均为 `SoftDependency`，缺失不影响加载

---

## 构建 / Build

```bash
dotnet build HacknetIRCEnhancements.csproj --nologo
```

输出 / Output: `bin/Debug/net472/HacknetIRCEnhancements.dll`

---

**License**: MIT-0 (MIT No Attribution) — see [LICENSE](LICENSE).
