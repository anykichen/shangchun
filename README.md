# DragUploadToNas

将文件或文件夹拖拽到 `.exe` 图标上，一键上传到 NAS（WebDAV）。

## 功能

| 功能 | 说明 |
|---|---|
| 拖拽多文件 | 同时拖拽任意数量文件 |
| 递归文件夹 | 拖入文件夹自动展开所有子文件 |
| 重复检测 | HEAD 请求检查远端是否已有同名文件，有则跳过 |
| 进度显示 | 每 10% 刷新一次进度条（不弹窗） |
| 自动重试 | 失败后最多重试 3 次，间隔 1.5s |
| 大文件限制 | 默认跳过 > 4 GB 的文件（可改） |
| 日志 | 写入 `upload_log.txt`（与 exe 同目录） |
| 失败汇总 | 全部完成后列出失败文件名，等待按键 |

## 配置（修改 Program.cs 顶部常量）

```csharp
private static readonly string NasWebDavUrl = "http://10.201.2.31:5005/上传的文件/";
private static readonly string UserName     = "user";
private static readonly string Pwd          = "Cc880821/";

private static readonly int    RetryCount       = 3;
private static readonly int    RetryDelayMs     = 1500;
private static readonly bool   SkipIfExists     = true;
private static readonly long   MaxFileSizeBytes = 4L * 1024 * 1024 * 1024;
```

## 编译（需要 .NET SDK 或 Visual Studio）

```bash
# .NET 6+
dotnet publish -c Release -r win-x64 --self-contained false

# .NET Framework 4.8（Visual Studio 直接生成即可）
```

## 日志格式

```
===== 上传开始 2024-01-15 09:30:00 =====
OK    C:\Users\user\Documents\report.pdf
SKIP  已存在: C:\Users\user\Pictures\photo.jpg
FAIL  C:\Users\user\Videos\big.mp4  [连接超时]
===== 上传结束  成功:1 跳过:1 失败:1 =====
```
