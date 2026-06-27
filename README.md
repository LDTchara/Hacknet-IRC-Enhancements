# HacknetIRCEnhancements

IRC 翻页 / IRC Paging

本功能来自 [TAXCore](https://github.com/Dsl114514/TAXCore)，分离为独立模组。 / This feature is extracted from [TAXCore](https://github.com/Dsl114514/TAXCore) as a standalone mod.

与 [TAXCore](https://github.com/Dsl114514/TAXCore) 不同 / Differences from [TAXCore](https://github.com/Dsl114514/TAXCore):
- 鼠标滚轮滚动（上下键与终端命令历史冲突）/ Mouse wheel (Up/Down conflicted with terminal)
- 不替换 IRC 绘制逻辑，兼容性更好 / Does not replace IRC drawing, better compatibility
- 仅翻页功能，无其他依赖 / Scrolling only, no extras
---

翻页功能的标准配置：
/ Standard scrolling controls:

| 操作 / Input | 功能 / Action |
|-------------|---------------|
| 鼠标滚轮 / Mouse Wheel | 逐行滚动 / Scroll one line |
| PageUp / PageDown | 翻页滚动 / Scroll one page |
| Home | 跳至最旧 / Jump to oldest |
| End | 跳至最新 / Jump to latest |

---

若已安装 [TAXCore](https://github.com/Dsl114514/TAXCore)，此模组会自动跳过，不重复加载。
/ If [TAXCore](https://github.com/Dsl114514/TAXCore) is installed, this mod skips loading automatically.