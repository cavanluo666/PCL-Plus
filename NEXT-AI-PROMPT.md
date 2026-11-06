# 给下一个 AI 的交接说明

> 本文档用于向接手的 AI 说明项目背景、文件位置与注意事项。
> 上一个 AI（DeepSeek Harness）已完成源码开发与 GitHub 发布，你的任务是**制作宣传片**。

---

## 一、你的任务

为 **PCL Plus** 制作一支宣传片（视频 / 动图 / 图文皆可）。

---

## 二、项目是什么

**PCL Plus** 是基于 [Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL) 开源版的 **第三方修改版** Minecraft 启动器。

它恢复了官方开源版移除的功能，并补充了一批实用特性。

### 核心卖点（宣传片重点展示）

| 功能 | 亮点 |
|---|---|
| **恢复下载页 + 百宝箱** | 官方开源版砍掉的，这里都有 |
| **联机功能** | 红石 / Terracotta 双方案，一键创建或加入房间 |
| **AI 崩溃分析** | 79 个 AI 服务商、514 个模型可选，AI 直接告诉你崩溃原因和修复步骤 |
| **Mod 介绍查看** | 完整 Markdown 介绍 + 图片，不再是干巴巴的文本框 |
| **Mod 介绍翻译** | Google 翻译约 0.5 秒出中文，Markdown 格式完整保留 |
| **10 种模组加载器** | 比官方多了 Legacy Fabric 和 Quilt（含 QSL 一键安装） |
| **下载源智能选优** | 自动测速官方源与镜像源，选最快的 |

---

## 三、文件位置（重要）

### 可运行的启动器（宣传片录制用这个）

```
C:\Users\Administrator\Desktop\新建文件夹\PCL-Plus-Standard.exe
C:\Users\Administrator\Desktop\新建文件夹\PCL-Plus-Themes.exe
```

**两个版本的区别**：

| 版本 | 说明 |
|---|---|
| **PCL-Plus-Standard.exe** | 标准版（**宣传片建议用这个**） |
| **PCL-Plus-Themes.exe** | 主题解限版（**仅供自用，不要对外宣传或分发**） |

> ⚠️ **宣传片里只能出现标准版**，不要提及或展示主题解限功能 —— PCL 原许可证禁止绕过赞助解锁。

### 同类备份

```
D:\代码\pcl\dist\PCL-Plus-Standard.exe   （sha256: 408ce8c8ce83664e）
D:\代码\pcl\dist\PCL-Plus-Themes.exe     （sha256: a5b282b990d9a7bf）
```

### 源码位置

| 路径 | 用途 |
|---|---|
| `D:\代码\pcl\PCL-main` | **开发工作目录**，含构建产物，可直接编译 |
| `D:\代码\pcl-upload` | **干净 Git 仓库**，已推送 GitHub，无构建产物 |

### GitHub 仓库

```
https://github.com/cavanluo666/PCL-Plus
```

---

## 四、录制宣传片的建议

### 环境准备

用户机器上已有：

- `.minecraft` 目录（含一个 Fabric 整合包实例）
- Java 8 / 21 / 25
- PCL 数据在 `C:\Users\Administrator\Desktop\新建文件夹\PCL\`

### 推荐演示流程

1. **启动器首屏** —— 展示界面与主题
2. **恢复的下载页** —— 进入「下载」看看版本列表
3. **百宝箱** —— 展示工具箱
4. **联机** —— 展示创建房间界面与两种方案
5. **AI 分析** —— 展示 AI 流式输出分析结果（实际操作需 API Key，见注意事项）
6. **Mod 介绍** —— 版本设置 → Mod 管理 → 点 ℹ 图标，展示带图片的介绍和翻译
7. **加载器** —— 下载页展开 Quilt / Legacy Fabric 卡片，展示 QSL 一键安装

### 注意事项

- **AI 功能需要 API Key** —— 在「设置 → AI 分析」里配置，否则 AI 分析无法演示
- **Mod 介绍需要联网** —— 介绍来自 Modrinth
- **本地翻译模型约 200 MB** —— 首次启动会后台下载，等它下完再录制
- **不要录制任何涉及正版账号密码的画面**

---

## 五、合规要求（**必读**）

PCL 原许可证叫《PCL 分发有限许可》，对二次创作有以下要求：

### 必须做到

1. **明确标注是第三方修改版**，不能让人误以为与官方有关
2. **关于页首位署名龙腾猫跃**（代码里已经改好了）
3. **不要宣传主题解限功能** —— 原许可证第 5 条明确禁止「与 PCL 的赞助解锁功能类似的功能或表现」

### 命名风险（需要留意）

PCL 许可证**禁止**的命名示例：

```
PCL 某服务器特供版  PCL 3  PCL 手机版  PCL++  PCL Pro
```

当前名称 **「PCL Plus」** 与 `PCL++` / `PCL Pro` 属于同一类（「像是官方的后续作品」）。

**建议**：宣传片中如果要使用正式名称，建议用许可证允许的格式，例如：

```
PCL Plus 修改版
PCL (Plus Mod)
```

或者向用户确认后再决定。

### 宣传片里应该出现的说明

```
本项目为第三方基于 PCL 的独立修改版，与 PCL 官方及开发者无任何关联。
```

---

## 六、项目技术信息（如果宣传片需要字幕/说明）

- **技术栈**：VB.NET / WPF / C#，.NET Framework 4.8
- **版本号**：1.0.0（基于 PCL 2.99.0.1）
- **开源协议**：遵循 PCL 原《存储库合理使用指南》
- **依赖**：BMCLAPI、aria2、Google 翻译接口、llama.cpp

---

## 七、如需修改代码

构建命令：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File 'D:\代码\pcl\build-pcl.ps1' -Configuration Release -Rebuild
```

产物在：

```
D:\代码\pcl\PCL-main\Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe
```

**构建环境注意**：

- .NET SDK 在 `C:\Program Files\dotnet`（**不是** D:\dotnet，之前被清理磁盘的 AI 删了）
- VS BuildTools 在 `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools`
- 构建脚本已自动处理 NuGet 还原

---

## 八、一句话总结

**用桌面上的 `PCL-Plus-Standard.exe` 录制，突出「恢复的功能 + 新增的 AI/联机/介绍翻译/更多加载器」，但不要提主题解限、并在片中声明这是第三方修改版。**
