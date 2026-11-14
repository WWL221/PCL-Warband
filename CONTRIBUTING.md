# 参与贡献

本仓库是 [PCL2](https://github.com/Hex-Dragon/PCL2) 的第三方二次创作（战团 / WSE2 启动支持）。
改动请尽量**收敛在 `Modules\Warband` 与 `Pages\PageWarband` 里**，避免动 PCL 的通用框架代码，方便日后同步上游。

## 环境

- Windows 10/11（x64）
- [.NET SDK 10.0](https://dotnet.microsoft.com/download)（不需要 Visual Studio）
- .NET Framework 4.8 运行时（Win10 1809+ 自带）

## 构建

```powershell
dotnet build "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -c Debug
# 产物：Plain Craft Launcher 2\bin\Debug\net48\Plain Craft Launcher 2.exe
```

首次从老式 vbproj 迁移时若遇到资源缺失（例如 `找不到资源 images\icon.ico`），
先删掉 `bin` / `obj` 再完整重建一次：`dotnet build ... -c Debug -t:Rebuild`。

## 提交

- 提交信息用 Conventional Commits：`feat(warband): …` / `fix(warband): …` / `chore: …`
- 一个 PR 只做一件事；动到上游文件时请在描述里写明原因与影响面。
- 提 PR 前请自测：能打开主界面 → 战团页能识别游戏目录 → 能列出模块 → 「启动前体检」全部项正常显示。

## 报 Bug

请用 Issue 模板，并附上：

1. 启动器版本（标题栏 v 号）与你选的运行时（x86 / x64 / dedicated）
2. 模块名与 `module.ini` 里的 `compatible_with_warband`
3. `<游戏目录>\rgl_log.txt` 的最后 100 行（或 `Logs\rgl_log_*_crash.txt`）
4. 「崩溃速诊」的输出
