param([string]$Action = 'enable')
$f = Join-Path $env:USERPROFILE '.config\opencode\opencode.json'
$disableJson = @'
{
  "$schema": "https://opencode.ai/config.json",
  "plugins": ["-codify_1.5"]
}
'@
$enableJson = @'
{
  "$schema": "https://opencode.ai/config.json",
  "plugins": []
}
'@
$json = if ($Action -eq 'disable') { $disableJson } else { $enableJson }
[System.IO.File]::WriteAllText($f, $json, (New-Object System.Text.UTF8Encoding($false)))
if ($Action -eq 'disable') {
    Write-Host ''
    Write-Host '  Codify 通知已禁用（回复结束不再发送邮件）' -ForegroundColor Yellow
} else {
    Write-Host ''
    Write-Host '  Codify 通知已启用（回复结束会发送邮件）' -ForegroundColor Green
}
