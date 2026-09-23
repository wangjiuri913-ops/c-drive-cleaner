# C 盘清理助手

Windows 图形界面的便携清理工具。先扫描、核对路径和预计空间，再由用户确认清理。

## 下载运行

下载 [CDriveCleaner-v1.0.0-portable.zip](CDriveCleaner-v1.0.0-portable.zip)，完整解压，双击 `CDriveCleaner.exe`。这是免安装运行包，不需要执行安装向导。请在文件页选择 **Download raw file** 下载 ZIP；不要把 GitHub 的源码下载包当作程序运行包。

支持 Windows 10 / 11；需要系统提供 .NET Framework。无需 Python、Node.js 或第三方库。

## 功能

- 显示 C 盘已用和可用空间。
- 扫描用户临时文件、DirectX 缓存、崩溃信息、错误报告、微信日志、Edge/Chrome 缓存等预设位置。
- 默认只处理修改时间超过 3 天的文件，可调整保留天数。
- 展示每一项路径、文件数、预计可释放空间和说明。
- 清理前二次确认；跳过无权限和被占用的文件。
- 提供 Windows 存储设置、回收站清理、DISM 组件清理、关闭/恢复休眠入口。

清理是永久删除，不经过回收站；请先退出相关应用。程序不提供任意目录输入，也不把 System32、WinSxS、Installer 或程序安装目录作为缓存清理目标。组件清理通过 Windows DISM 完成。

## 使用步骤

1. 启动程序，保留默认的 3 天保护间隔。
2. 点击“扫描”，核对勾选项、路径和预计大小。
3. 退出微信、浏览器等相关应用。
4. 点击“清理所选”，阅读确认窗口后再执行。

系统工具独立操作：关闭休眠会禁用休眠并影响快速启动；清理组件存储会请求管理员权限。不要手工删除 `hiberfil.sys`、`WinSxS` 或任意 `.dll/.sys/.exe` 文件。

## 文件说明

| 文件 | 作用 |
| --- | --- |
| `CDriveCleaner.cs` | 窗口、预设清理目标、扫描与删除逻辑 |
| `CDriveCleaner.manifest` | Windows 执行权限和兼容声明 |
| `Build.ps1` | 使用系统 C# 编译器生成 EXE |
| `CDriveCleaner-v1.0.0-portable.zip` | 免安装 Windows 运行包 |
| `使用说明.md` | 详细操作和清理边界 |
| `CHANGELOG.md` | 版本说明 |

## 从源码编译

在 Windows PowerShell 中运行：

```powershell
.\Build.ps1
```

编译结果位于 `bin\CDriveCleaner.exe`。使用 .NET Framework 自带的 C# 编译器，不需要下载依赖。

## 验证范围

已经完成编译、Windows 窗口启动和界面检查。未在用户真实缓存目录执行删除测试，不承诺所有软件版本的缓存布局一致。程序跳过占用或无权限文件，预计空间不一定等于实际释放空间。
