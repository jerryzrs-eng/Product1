param(
    [Parameter(Mandatory)][ValidateSet('List', 'New', 'Check', 'Remove')][string]$Action,
    [ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')][string]$Agent,
    [ValidatePattern('^[a-z0-9]+(-[a-z0-9]+)*$')][string]$Task,
    [switch]$Completed
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "Git 操作失败 $($args -join ' ')" }
}

$root = Invoke-Git rev-parse --show-toplevel
$worktrees = @(Invoke-Git worktree list --porcelain)
$main = $worktrees[0].Substring('worktree '.Length)
if ($Action -eq 'List') {
    Invoke-Git worktree list
    return
}
if ($Action -eq 'Check') {
    $branch = Invoke-Git branch --show-current
    if ($branch -cnotmatch '^agent/[a-z0-9]+(-[a-z0-9]+)+$' -or (Invoke-Git rev-parse --absolute-git-dir) -eq (Invoke-Git rev-parse --path-format=absolute --git-common-dir)) {
        throw '请在独立的 Agent 任务 Worktree 中操作'
    }
    if (Invoke-Git diff --name-only --diff-filter=U) { throw '工作区存在未解决的冲突' }
    Invoke-Git diff --cached --check
    "当前任务 $branch，目录 $root"
    Invoke-Git status --short --branch
    return
}
if (!$Agent -or !$Task) { throw '请提供 Agent 和 Task' }
$branch = "agent/$Agent-$Task"
$target = Join-Path (Split-Path -Parent $main) "$(Split-Path -Leaf $main)-$Agent-$Task"

if ($Action -eq 'New') {
    if ((Test-Path -LiteralPath $target) -or ("worktree $($target.Replace('\', '/'))" -in $worktrees)) {
        throw "任务目录已经存在或已注册 $target"
    }
    if (Invoke-Git branch --list $branch) { throw "本地分支已存在 $branch" }
    if (Invoke-Git ls-remote --heads origin "refs/heads/$branch") { throw "远程分支已存在 $branch" }
    Invoke-Git fetch origin 'refs/heads/master:refs/remotes/origin/master'
    Invoke-Git worktree add --no-track -b $branch $target origin/master
    "Agent $Agent，分支 $branch，目录 $target"
    return
}

if (!$Completed) { throw '请先确认任务已完成，再传入 -Completed' }
if (!(Test-Path -LiteralPath $target -PathType Container)) { throw "找不到任务目录 $target" }
$registeredPath = "worktree $($target.Replace('\', '/'))"
if ($registeredPath -notin $worktrees) { throw "目录不是本仓库的任务 Worktree $target" }
if ((Invoke-Git -C $target branch --show-current) -ne $branch) { throw '目录当前分支与任务不一致' }
if (Invoke-Git -C $target status --porcelain --untracked-files=all) { throw '任务目录仍有未提交修改' }
Invoke-Git fetch origin 'refs/heads/master:refs/remotes/origin/master'
Invoke-Git -C $target merge-base --is-ancestor HEAD origin/master
# Git 再次检查目录状态并执行非强制清理。保留本地和远程分支。
Invoke-Git worktree remove $target
