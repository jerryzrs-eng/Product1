$ErrorActionPreference = 'Stop'

if ($env:GITHUB_EVENT_NAME -ne 'workflow_dispatch' -or $env:GITHUB_REF -cne 'refs/heads/master' -or
    $env:GITHUB_RUN_ATTEMPT -ne '1' -or $env:RELEASE_COMMIT -notmatch '^[0-9a-f]{40}$') {
    throw '正式发布需要从 master 提交新的手动请求并固定完整 Commit SHA'
}
$checkout = git rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $checkout -ne $env:RELEASE_COMMIT) { throw '检出的提交与授权 SHA 不一致' }
git fetch --no-tags origin 'refs/heads/master:refs/release-check/master'
if ($LASTEXITCODE -ne 0) { throw '无法获取最新 master' }
git merge-base --is-ancestor $env:RELEASE_COMMIT refs/release-check/master
if ($LASTEXITCODE -ne 0) { throw '授权提交不在最新 master 历史中' }
