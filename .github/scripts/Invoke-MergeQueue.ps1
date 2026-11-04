param(
    [Parameter(Mandatory)][ValidatePattern('^[1-9][0-9]*$')][string]$PullRequest,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Head
)

$ErrorActionPreference = 'Stop'
$repo = $env:GITHUB_REPOSITORY
$checkName = 'Windows 构建'
if ($env:GITHUB_REF -ne 'refs/heads/master' -or $env:MERGE_ENABLED -ne 'true') {
    throw '合并协调尚未启用，或运行来源不是 master'
}

function Get-GitHubJson([string]$Path) {
    $result = gh api $Path
    if ($LASTEXITCODE -ne 0) { throw "GitHub 查询失败 $Path" }
    $result | ConvertFrom-Json
}

$rules = @(Get-GitHubJson "repos/$repo/rules/branches/master")
$strict = $rules | Where-Object {
    $_.type -eq 'required_status_checks' -and $_.parameters.strict_required_status_checks_policy -eq $true -and
        $checkName -in $_.parameters.required_status_checks.context
}
if (!$strict -or 'pull_request' -notin $rules.type) {
    throw 'master 必须有生效的 PR 规则及严格的 Windows 构建必需检查'
}

function Get-EligiblePullRequest {
    $pr = Get-GitHubJson "repos/$repo/pulls/$PullRequest"
    if ($pr.state -ne 'open' -or $pr.draft -or $pr.merged -or $pr.base.ref -ne 'master' -or
        $pr.base.repo.full_name -ne $repo -or $pr.head.repo.full_name -ne $repo -or
        $pr.head.ref -cnotmatch '^agent/[a-z0-9]+(-[a-z0-9]+)+$' -or $pr.head.sha -ne $Head) {
        throw 'PR 已变更、已关闭、仍为草稿，或来源与目标不符合队列要求'
    }
    $pr
}

$null = Get-EligiblePullRequest
git fetch --no-tags origin 'refs/heads/master:refs/remotes/origin/master' "refs/pull/$PullRequest/head:refs/merge-queue/pr-$PullRequest"
if ($LASTEXITCODE -ne 0) { throw '无法获取最新 master 与 PR Head' }
$base = git rev-parse origin/master
if ($LASTEXITCODE -ne 0) { throw '无法解析 master 提交' }
$fetchedHead = git rev-parse "refs/merge-queue/pr-$PullRequest"
if ($LASTEXITCODE -ne 0 -or $fetchedHead -ne $Head) { throw 'PR Head 已变化，请重新检查后入队' }
git merge-base --is-ancestor $base $Head
if ($LASTEXITCODE -ne 0) { throw 'PR 尚未包含最新 master，请更新任务分支、通过 CI 后重新入队' }

$runs = Get-GitHubJson "repos/$repo/actions/workflows/build.yml/runs?event=pull_request&head_sha=$Head&per_page=20"
$run = $runs.workflow_runs | Where-Object {
    $_.head_sha -eq $Head -and $_.head_repository.full_name -eq $repo -and
        ($_.pull_requests | Where-Object { $_.number -eq [int]$PullRequest })
} | Select-Object -First 1
if (!$run -or $run.status -ne 'completed' -or $run.conclusion -ne 'success') {
    throw '这个 PR Head 的最新 PR CI 尚未通过'
}
$jobs = Get-GitHubJson "repos/$repo/actions/runs/$($run.id)/attempts/$($run.run_attempt)/jobs"
if (!($jobs.jobs | Where-Object { $_.name -eq $checkName -and $_.conclusion -eq 'success' })) {
    throw "PR CI 缺少成功的 $checkName 检查"
}

$pr = Get-EligiblePullRequest
$latestBase = (Get-GitHubJson "repos/$repo/branches/master").commit.sha
if ($latestBase -ne $base -or $pr.base.sha -ne $base -or $pr.mergeable -ne $true) {
    throw 'master 已更新或 PR 存在冲突，请重新验证后入队'
}

# 服务端校验 Head SHA，严格必需检查阻止合并落后于主分支的 PR。
$result = gh api --method PUT "repos/$repo/pulls/$PullRequest/merge" -f "sha=$Head" -f 'merge_method=merge'
if ($LASTEXITCODE -ne 0) { throw 'GitHub 拒绝合并，请检查 PR 与保护规则' }
$merged = $result | ConvertFrom-Json
if ($merged.merged -ne $true) { throw 'GitHub 未确认合并' }
"PR #$PullRequest 已合并。合并提交 $($merged.sha)，验证运行 $($run.html_url)" >> $env:GITHUB_STEP_SUMMARY
