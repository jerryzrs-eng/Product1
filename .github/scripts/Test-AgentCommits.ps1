param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Base,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Head
)

$ErrorActionPreference = 'Stop'
$commits = @(git rev-list --no-merges "$Base..$Head")
if ($LASTEXITCODE -ne 0) { throw '无法读取 PR 提交范围' }
foreach ($commit in $commits) {
    $body = (git show -s '--format=%B' $commit) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) { throw "无法读取提交 $commit" }
    foreach ($field in '开发 Agent', '开发模型', 'Agent 会话') {
        if ($body -notmatch "(?m)^$field[：:][^\S\r\n]*[^*\s]") {
            throw "提交 $commit 缺少字段 $field"
        }
    }
}
"已验证 $($commits.Count) 个 Agent 任务提交的署名格式"
