# Catpaq (WinUI 3)

> [!WARNING]
> 此为 catpaq 的非官方第三方 WinUI 3 实现，基于原 catpaq 项目的个人学习项目。
>
> 代码主要借助 DeepSeek 生成，基于 catpaq 的 C++ 图形界面实现，使用 C# 和 WinUI 3 构建，适配 Windows 11 风格。

## 简介

Catpaq 是一款基于 **ZPAQ** 格式的归档管理工具。本仓库是其第三方 WinUI 3 实现：使用 C# 编写，界面参考 Files（Windows 11 文件资源管理器）的侧边栏与浏览体验，压缩/解压引擎使用 [zpaqfranz](https://github.com/zpaq/zpaq) 生态的 zpaqfranz 程序。

> **原项目**：<https://github.com/fcorbelli/catpaq>（Lazarus/Pascal 实现）

## 功能特性

- **浏览归档**：直接在资源管理器式界面中浏览 `.zpaq` 文件内部结构，无需先解压
- **创建归档**：支持压缩级别、多卷、分块、AES / Franzen 加密等
- **全部解压缩**：解压整个归档或仅解压选中的条目
- **此电脑 / 快速访问**：Files 风格侧边栏，识别本地驱动器与固定位置
- **文件关联**：一键将 `.zpaq` 关联到 Catpaq，双击即可打开（可随时移除）
- **多语言**：内置多语言支持，自动跟随系统语言，可在设置中切换
- **Windows 11 风格 UI**：Mica 背景、导航视图、自适应主题

## 多语言

内置语言（完整翻译）：简体中文、英语、意大利语、俄语、荷兰语、德语、法语、西班牙语、葡萄牙语（巴西）、日语、韩语。

其余语言可在设置中选择或随系统自动检测，但界面暂回退英语。

## 环境要求

- Windows 10 1809+（推荐 Windows 11）
- [.NET SDK](https://dotnet.microsoft.com/)（本项目使用 `net10.0-windows10.0.26100.0` 预览版）
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) 2.3.1

## 构建

```bash
# 构建（x64）
dotnet build -c Release -p:Platform=x64

# 发布打包（自包含，无需安装 Windows App Runtime）
dotnet publish -c Release -p:Platform=x64
```

发布产物位于 `bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish\`，拷贝即用。

## 免责声明

- 本软件为个人学习项目，**按"原样"提供，不提供任何担保**。
- **不推荐在生产环境或重要数据上直接运行**，使用前请自行备份并验证归档完整性。
- 部分代码与界面翻译由 **DeepSeek AI** 生成，如有问题还望见谅。
- 压缩/解压引擎 `zpaqfranz.exe` 为第三方程序，遵循其原始许可证。
- 本软件一切条款以 **MIT 协议**为准；本免责声明与 MIT 协议不一致时，以 MIT 协议为准。

## 许可证

本项目采用 MIT 许可证，详见 [LICENSE](code/LICENSE)。

## 致谢

- 原 catpaq 项目及 [Franco Corbelli](https://github.com/fcorbelli)（Lazarus 实现与 zpaqfranz 引擎）
- [Files Community](https://github.com/files-community/Files)（侧边栏等 UI 参考，MIT）
- [zpaqfranz](https://github.com/fcorbelli/zpaqfranz)（ZPAQ 引擎）

---

[English](README_EN.md) | 简体中文
