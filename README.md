# PCL Plus

基于 [Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL) 开源版的第三方修改版本。

恢复了开源版移除的功能，并补充了一批实用特性。

> ⚠️ **本项目为第三方修改版，与 PCL 官方及开发者无任何关联。请勿向官方反馈本分支的问题。**

## 📥 下载

前往 [Releases](https://github.com/cavanluo666/PCL-Plus/releases/latest) 下载最新版本。

| 文件 | 说明 |
|---|---|
| `PCL-Plus-v1.0.0-Windows-x64.exe` | 标准版（.NET Framework 4.8 / Windows 7 SP1+） |

```
SHA256: 408ce8c8ce83664ee7f6e141f842bf83a11db533ca882e7ecfb77ed31d679e91
大小:   11.3 MB
```

## 功能一览

### 恢复的功能

| 功能 | 说明 |
|---|---|
| **最新版下载** | 上游改版后移除了最新版下载入口，本分支已恢复（正式版 / 预览版 / 远古版 / 愚人节版完整列表） |
| **百宝箱** | 恢复工具箱功能 |
| **正版登录** | 修复了微软账号登录失效的问题 |

### 新增的功能

| 功能 | 说明 |
|---|---|
| **联机** | 支持红石（Redstone）与 Terracotta 两种隧道方案，可创建或加入房间 |
| **AI 分析** | 独立的设置页面，内置 79 个 AI 服务商、514 个模型，可用 AI 分析崩溃日志并生成修复建议 |
| **Mod 介绍查看** | 在资源详情页查看 Mod 的完整介绍，支持 Markdown 排版与图片显示 |
| **Mod 介绍翻译** | 三种翻译引擎可选：Google 翻译 / 在线 AI / 本地模型，支持自动翻译 |
| **本地翻译模型** | 内置 aria2 下载器，可离线部署 llama.cpp 与翻译模型，全程无需联网 |
| **更多模组加载器** | 补充 Legacy Fabric 与 Quilt（含 QSL 一键安装） |
| **存档 / 截图管理** | 在版本详情页直接管理存档与截图 |
| **下载源实测选优** | 自动测速官方源与镜像源，按实测延迟选择，避免镜像源拖慢下载 |

## 模组加载器支持

```
Forge  ·  NeoForge  ·  Fabric  ·  Legacy Fabric  ·  Quilt
Fabric API  ·  QSL（Quilt Standard Libraries）  ·  OptiFine  ·  OptiFabric  ·  LiteLoader
```

相较官方版本补充了以下支持：

- **Legacy Fabric** —— 面向 1.8 ~ 1.13 的 Fabric 分支，让老版本也能使用 Fabric 模组
- **Quilt** —— Fabric 的独立分支，选中后会自动安装 QSL

三者（Fabric / Legacy Fabric / Quilt）互斥，选中一个会自动清除其他两个的选择。

## 构建

### 环境要求

- **Windows 10 / 11**
- **.NET Framework 4.8** 引用程序集（随 Visual Studio Build Tools 安装）
- **.NET SDK 8.0 或更高**（用于解析 PCLCS / MeloongCore 这类 SDK 风格项目）
- **Visual Studio 2022 Build Tools** 或完整版 VS（提供 MSBuild）

### 构建步骤

```powershell
# Debug 构建
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-pcl.ps1 -Configuration Debug

# Release 构建（推荐）
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-pcl.ps1 -Configuration Release

# 强制完整重编
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-pcl.ps1 -Configuration Release -Rebuild
```

构建产物位于 `Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe`。

### 关于构建脚本

`build-pcl.ps1` 存在的意义是解决一个具体问题：本项目的 `PCLCS` 和 `MeloongCore` 是 SDK 风格项目，而 VS 版 `MSBuild.exe` 默认无法解析它们（.NET SDK 未向 Build Tools 注册组件时尤其如此）。

脚本通过两个环境变量显式接管解析：

```
MSBuildSDKsPath              = <SDK 目录>\Sdks
MSBuildEnableWorkloadResolver = false
```

如果你的环境能直接用 MSBuild 打开 `Plain Craft Launcher 2.sln` 构建，也可以不用这个脚本。

## 项目结构

```
.
├── Plain Craft Launcher 2/          # WPF 主程序（VB.NET）
│   ├── Modules/                    # 核心功能模块
│   │   ├── Base/                   # 网络、加载器、本地翻译等基础模块
│   │   └── Minecraft/              # Minecraft 相关逻辑
│   ├── Pages/                      # 页面
│   ├── Images/                     # 图片资源
│   └── Plain Craft Launcher 2.vbproj
├── PCLCS/                          # C# 组件（Minecraft 数据类型与工具）
├── MeloongCore/                    # 基础库（netstandard2.0）
├── patches/                        # 上游依赖补丁
├── build-pcl.ps1                   # 构建脚本
└── Plain Craft Launcher 2.sln      # 解决方案
```

## 常用配置

配置文件位置（以默认目录为例）：

| 内容 | 路径 |
|---|---|
| 设置注册表 | `HKCU\Software\PCLDebug` |
| 启动器数据 | `%APPDATA%\..\PCL\`（随安装位置而定） |
| 日志 | `PCL\Log1.txt` |
| 本地翻译模型 | `PCL\LocalAi\` |

## 注意事项

1. **AI 功能需要自备 API Key** —— 在「设置 → AI 分析」里配置，默认使用智谱 `glm-4-flash`（免费）
2. **本地翻译模型需要下载** —— 约 200 MB，首次启动后可自动后台下载
3. **Mod 介绍翻译默认用 Google 翻译** —— 无需配置，速度最快；也可切换为在线 AI 或本地模型

## 许可证与合规

本项目是**第三方基于 PCL 独立进行二次创作的产物**，与 PCL 官方及开发者无任何关联：

- 请勿向 PCL 官方反馈本分支的任何问题
- 本项目公开源代码，并继续以 PCL 原《存储库合理使用指南》作为使用约定
- 原始许可证见 [LICENCE](LICENCE)

## 致谢

感谢所有为这个项目提供帮助的开源项目与开发者：

- [龙腾猫跃](https://github.com/Meloong-Git) 与 PCL 开源社区 —— 提供了 Plain Craft Launcher 本身
- [BMCLAPI](https://bmclapi2.bangbang93.com/) —— 提供 Minecraft 镜像源
- [Ponderfly/GoogleTranslateIpCheck](https://github.com/Ponderfly/GoogleTranslateIpCheck) —— Google 翻译可用 IP 列表（MIT）
- [aria2](https://aria2.github.io/) —— 多线程下载器
- [llama.cpp](https://github.com/ggml-org/llama.cpp) —— 本地推理引擎
- [Haidass-Translate](https://huggingface.co/DALabCommunity/Haidass-Translate-143M) —— 本地翻译模型
- [Axolotl](https://github.com/Mystic-Stars/Axolotl) —— 下载源测速与 Mod 介绍功能的思路参考
