# Codify

OpenCode 插件 + Windows 一键安装器：在 OpenCode 每轮回复结束时，通过 **QQ 邮箱 SMTP** 发送通知邮件到你的手机。

## 功能

- **回复已就绪通知**：含回复摘要、回复耗时
- **成本统计**：本次回复成本 + 当前对话总成本（$）
- **上下文使用率**：本次回复占用 + 当前对话总占用（token / 上下文上限）
- **一键开关**：安装器界面按钮 / 桌面脚本 / 手动配置
- **拖拽填充**：把含 3 行内容的 txt 拖入安装器自动填充

## 目录结构

| 文件 | 说明 |
|---|---|
| `Program.cs` | Windows 图形化安装器源码（C# WinForms，无需 .NET SDK） |
| `index.js` | OpenCode 插件源码（零依赖，仅用 Node 内置模块） |
| `build.ps1` | 编译安装器的脚本 |

## 安装器功能

1. 填写 **发件邮箱 / 授权码 / 收件邮箱**（或拖入 3 行 txt 自动填充）
2. 点击「安装」→ 写入插件与配置到 `%USERPROFILE%\.config\opencode\`
3. 点击「重启 OpenCode 服务」使插件生效
4. 底部可切换「显示成本」「显示上下文使用率」

安装时会在桌面生成 `Codify 开关\` 文件夹，含启用/禁用脚本。

## 使用前提

1. 登录 QQ 邮箱网页版 → 设置 → 账户
2. 开启「IMAP/SMTP 服务」
3. 生成 16 位授权码
4. 发件邮箱与授权码必须属于同一账户；发件与收件可以不同

## 编译安装器

```powershell
./build.ps1
```

或手动：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /codepage:65001 `
  /resource:"index.js,Codify.Plugin" `
  /out:"codify-setup.exe" `
  /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  "Program.cs"
```

## 插件配置

`%USERPROFILE%\.config\opencode\qq-notify.json`：

```json
{
  "host": "smtp.qq.com",
  "port": 465,
  "user": "发件邮箱@qq.com",
  "authCode": "SMTP授权码",
  "to": "收件邮箱@qq.com",
  "fromName": "OpenCode",
  "showCost": true,
  "showContext": true
}
```

## 开关

编辑 `%USERPROFILE%\.config\opencode\opencode.json`：

```jsonc
// 启用
{ "$schema": "https://opencode.ai/config.json", "plugins": [] }

// 禁用
{ "$schema": "https://opencode.ai/config.json", "plugins": ["-codify_1.5"] }
```

## 许可

MIT
