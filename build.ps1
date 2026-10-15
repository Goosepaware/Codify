# 编译 Codify 安装器
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
& $csc /nologo /target:winexe /codepage:65001 `
  /resource:"index.js,Codify.Plugin" `
  /out:"codify-setup.exe" `
  /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  "Program.cs"
if ($LASTEXITCODE -eq 0) { Write-Host "编译成功：codify-setup.exe" }
