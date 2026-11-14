# 相对上游（Hex-Dragon/PCL2）的改动

本仓库是 PCL2 的**第三方二次创作**，把启动目标换成《骑马与砍杀：战团》+ WSE2。
为了便于上游合并或审阅，这里逐条列出改动。

## 新增文件

| 文件 | 作用 |
|---|---|
| `Plain Craft Launcher 2\Modules\Warband\ModWarband.vb` | 战团侧全部逻辑：Steam 目录定位、模块扫描、命令行拼装、进程启动、`rgl_config.ini` 读写（备份 + 原子替换）、启动前体检、崩溃速诊、设置持久化 |
| `Plain Craft Launcher 2\Pages\PageWarband\PageWarbandRight.xaml(.vb)` | 战团主页（右侧内容区），沿用 PCL 的 `MyPageRight` / `MyCard` / `MyButton` / `MyComboBox` / `MyTextBox` 等控件 |
| `Plain Craft Launcher 2\Pages\PageWarband\PageWarbandLeft.xaml(.vb)` | 战团页左侧栏：当前模块、运行目标、游戏目录与快捷按钮 |

## 修改的上游文件

| 文件 | 改动 |
|---|---|
| `Plain Craft Launcher 2\FormMain.xaml` | 顶部导航新增第 6 个标签 `BtnTitleSelect5`（Text="战团"，Tag="10"） |
| `Plain Craft Launcher 2\FormMain.xaml.vb` | ① `PageType` 增加 `Warband = 10`；② `BtnTitleSelect_Click` 的 `Handles` 增加 `BtnTitleSelect5.Check`；③ `PageChangeActual` 增加 `Case PageType.Warband`；④ 构造函数里实例化两个新页面，并让程序**默认进入战团页**，同时勾选对应标签 |
| `Plain Craft Launcher 2\Modules\ModMain.vb` | 新增两个页面字段 `FrmWarbandLeft` / `FrmWarbandRight` |
| `Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj` | 老式 vbproj 改写为 **SDK 风格**（`net48` + `UseWPF`，参考程序集走 `Microsoft.NETFramework.ReferenceAssemblies`），并补齐老项目手写的 `Import`（30 条项目级 Imports）、`Resource`（52 个图片/字体）、`None`（运行时数据文件）；排除上游仓库里未纳入编译的 `Controls\MySliderDot.*`、把 `Resources\Custom.xaml` 从 `Page` 改为 `None`。原文件保留为 `Plain Craft Launcher 2.vbproj.legacy` |

## 未改动

- 新增了 `docs/preview.png` 等截图，提供本 README。
- 其余 110 多个 `.vb` 与 60 个 `.xaml` 全部保持上游原样；PCL 的「关于」页面仍在，署名与赞助入口未被修改或移除（符合上游《合理使用指南》对重度使用的要求）。

## 为什么不直接改 Minecraft 逻辑

上游的启动链路与 Minecraft 强耦合（版本隔离、MeloongCore 实例、登录、下载器）。
因此本作采用「**新增主页面 + 独立模块**」的方式接入，Minecraft 相关页面保持可用但不再是默认页；后续若要彻底裁剪，只需删除对应 `Pages\*` 与导航项。

## WSE2 命令行对照

| 运行时 | 实际命令行 |
|---|---|
| x86 | `mb_warband_wse2.exe --module "<Folder>" --no-intro` |
| x64 | `mb_warband_wse2_x64.exe --module "<Folder>" --no-intro` |
| 专用服务器 x86 | `mb_warband_wse2_dedicated.exe --config-path server_config.ini -r "<Folder>.txt" --module "<Folder>"` |
| 专用服务器 x64 | `mb_warband_wse2_dedicated_x64.exe --config-path server_config.ini -r "<Folder>.txt" --module "<Folder>"` |

工作目录固定为游戏根目录（WSE2 官方启动器传 `NULL` 由系统继承）；其余参数（附加参数框）原样追加。
