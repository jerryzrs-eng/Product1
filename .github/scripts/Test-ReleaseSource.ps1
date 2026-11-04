$ErrorActionPreference = 'Stop'

$manualMaster = $env:GITHUB_EVENT_NAME -eq 'workflow_dispatch' -and $env:GITHUB_REF -ceq 'refs/heads/master'
$versionTag = $env:GITHUB_EVENT_NAME -eq 'push' -and $env:GITHUB_REF -ceq "refs/tags/$env:RELEASE_TAG"
if (!$manualMaster -and !$versionTag) {
    throw '正式发布只能从 master 手动发起，或推送指向 master 历史的版本标签'
}
git fetch --no-tags origin 'refs/heads/master:refs/release-check/master'
if ($LASTEXITCODE -ne 0) { throw '无法获取最新 master' }
git merge-base --is-ancestor $env:GITHUB_SHA refs/release-check/master
if ($LASTEXITCODE -ne 0) { throw '构建提交不在最新 master 历史中' }
