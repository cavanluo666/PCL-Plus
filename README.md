# PCL Plus

基于 [PCL2](https://github.com/Meloong-Git/PCL)（Plain Craft Launcher 2）的开源分支，恢复了官方开源版移除的功能，并补充了一批实用特性。

> ⚠️ 本项目为**非官方分支**，与 PCL 官方无关，请勿向官方反馈本分支的问题。

## 相比原版的新增与恢复

### 恢复的功能

- **下载页**、**百宝箱**（官方开源版移除了这些）
- **主题功能**：保持官方赞助解锁机制，未做解限
- **正版登录**（修复了微软登录失效的问题）

### 新增功能

| 功能 | 说明 |
|---|---|
| **联机** | 红石（Redstone）/ Terracotta 两种隧道方案，支持创建与加入房间 |
| **AI 分析** | 独立的设置页，支持 79 个 AI 服务商、514 个模型，可用 AI 分析崩溃日志并给出修复建议 |
| **Mod 介绍查看** | 在资源详情页查看 Mod 的完整介绍（支持 Markdown 与图片） |
| **Mod 介绍翻译** | 支持 Google 翻译 / 在线 AI / 本地模型三种引擎，可设置自动翻译 |
| **本地翻译模型** | 内置 aria2 下载器，可离线部署 llama.cpp + 翻译模型 |
| **更多模组加载器** | 在官方基础上补充了 **Legacy Fabric**、**Quilt**（含 QSL 一键安装） |
| **存档 / 截图管理** | 在版本详情页管理存档与截图 |
| **下载源实测选优** | 自动测速官方源与镜像源，按实测延迟选择，避免镜像源拖慢下载 |

### 加载器支持

```
Forge · NeoForge · Fabric · Legacy Fabric · Quilt
Fabric API · QSL（Quilt Standard Libraries）· OptiFine · OptiFabric · LiteLoader
```

## 版本

本仓库**只有标准版**，不提供主题解限版本。

标准版保持与 PCL 官方一致的赞助解锁行为，仅恢复被开源版移除的下载页、百宝箱等功能。

## 构建

需要 .NET Framework 4.8 SDK 与 MSBuild。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-pcl.ps1 -Configuration Release -Rebuild
```

产物输出到 `PCL-main\Plain Craft Launcher 2\bin\`。

> 构建脚本位于仓库根目录的 `build-pcl.ps1`。

## 项目结构

```
PCL-main/
├── Plain Craft Launcher 2/   # WPF 主程序（VB.NET）
│   ├── Modules/             # 核心模块
│   ├── Pages/               # 页面
│   └── Images/              # 图片资源
├── PCLCS/                   # C# 组件（Minecraft 相关数据类型）
├── MeloongCore/             # 基础库
└── patches/                 # 依赖补丁
```


## ⚠️ 第三方修改版声明

本项目是**第三方基于 PCL 独立进行二次创作的产物**，与 PCL 官方及开发者**无任何关联**。

- 请勿向 PCL 官方反馈本分支的任何问题
- 软件名称中的修改版后缀已表明其为第三方产物
- 本项目公开源代码，并继续以 PCL 原《存储库合理使用指南》作为使用约定

## 致谢

- [龙腾猫跃](https://github.com/Meloong-Git) 与 PCL 开源社区
- [BMCLAPI](https://bmclapi2.bangbang93.com/)（镜像源）
- [Ponderfly/GoogleTranslateIpCheck](https://github.com/Ponderfly/GoogleTranslateIpCheck)（Google 翻译可用 IP 列表，MIT）
- [aria2](https://aria2.github.io/)（多线程下载器）
- [llama.cpp](https://github.com/ggml-org/llama.cpp)（本地推理）
- [Haidass-Translate](https://huggingface.co/DALabCommunity/Haidass-Translate-143M)（本地翻译模型）
- [Axolotl](https://github.com/Mystic-Stars/Axolotl)（下载源测速与 Mod 介绍思路参考）

## 许可

本项目基于 PCL 开源版修改，遵循其原始开源协议。
