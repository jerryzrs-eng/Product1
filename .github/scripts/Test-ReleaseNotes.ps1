param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Commit
)

$ErrorActionPreference = 'Stop'
$versionPattern = '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
if ($Tag -cnotmatch $versionPattern) { throw "无效的发布标签 $Tag" }

$notesPath = "docs/更新说明/$Tag.md"
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) { throw "缺少更新说明 $notesPath" }

$lines = @(Get-Content -LiteralPath $notesPath -Encoding utf8 | Where-Object { $_.Trim() })
if ($lines.Count -eq 0 -or $lines[0].Trim() -cne "# 课表 $Tag") {
    throw "更新说明的标题必须为 # 课表 $Tag"
}
$header = @($lines | Select-Object -Skip 1 | Select-Object -First 5 | Where-Object { $_ -notmatch '^#' })
$fields = @{}
foreach ($line in $header) {
    if ($line -notmatch '^(开发 Agent|发布 Agent|开发模型|关联提交|Agent 会话)[：:]\s*(.*)$') { break }
    $key = $Matches[1]
    $value = $Matches[2].Trim().Trim('*').Trim()
    if (-not $value -or $fields.ContainsKey($key)) { throw "字段为空或重复 $key" }
    $fields[$key] = $value
}
foreach ($key in '开发 Agent', '发布 Agent', '开发模型', '关联提交') {
    if (-not $fields.ContainsKey($key)) { throw "标题下方缺少字段 $key" }
}

$head = git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $Commit) { throw '检出的提交与构建提交不一致' }
$tags = @(git tag --list 'v*' --merged $Commit --sort=-version:refname)
if ($LASTEXITCODE -ne 0) { throw '无法读取版本标签' }
$existing = git tag --list $Tag
if ($LASTEXITCODE -ne 0) { throw '无法查询发布标签' }
if ($existing) {
    $tagCommit = git rev-parse "refs/tags/$Tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $Commit) { throw "标签 $Tag 指向其他提交" }
}

$version = [version]$Tag.Substring(1)
$base = $tags | Where-Object { $_ -cmatch $versionPattern -and [version]$_.Substring(1) -lt $version } | Select-Object -First 1
$range = if ($base) { "$base..$Commit" } else { $Commit }
$allowed = @(git rev-list $range)
if ($LASTEXITCODE -ne 0) { throw "无法读取版本提交范围 $range" }
foreach ($sha in ($fields['关联提交'] -split '[\s,，、]+' | Where-Object { $_ })) {
    if ($sha -notmatch '^[0-9a-f]{40}$' -or $sha -notin $allowed) {
        throw "关联提交必须是本版本范围内的完整 SHA。收到 $sha，范围 $range"
    }
}
if ($fields['关联提交'] -notmatch '[0-9a-f]{40}') { throw '关联提交至少需要一个完整 SHA' }

"发布说明校验通过。标签 $Tag，提交范围 $range"
