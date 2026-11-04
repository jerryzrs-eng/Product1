param([switch]$RequireApproval)

$ErrorActionPreference = 'Stop'
$repo = $env:GITHUB_REPOSITORY
$owner = $env:GITHUB_REPOSITORY_OWNER
$environmentName = 'kebiao-release'
if (!$owner -or $env:GITHUB_ACTOR -eq $owner) {
    throw '请求发起者和仓库所有者审批者必须使用不同 GitHub 身份'
}

$policyJson = gh api "repos/$repo/environments/$environmentName"
if ($LASTEXITCODE -ne 0) { throw "无法读取审批环境 $environmentName，请由仓库所有者配置" }
$policy = $policyJson | ConvertFrom-Json
$rule = $policy.protection_rules | Where-Object type -eq 'required_reviewers'
$reviewers = @($rule.reviewers)
if ($rule.prevent_self_review -ne $true -or $reviewers.Count -ne 1 -or
    $reviewers[0].type -ne 'User' -or $reviewers[0].reviewer.login -ne $owner) {
    throw '审批环境必须以仓库所有者为唯一必需审批者，并启用禁止自审'
}

if ($RequireApproval) {
    $reviewsJson = gh api "repos/$repo/actions/runs/$env:GITHUB_RUN_ID/approvals"
    if ($LASTEXITCODE -ne 0) { throw '无法验证 GitHub 审批记录' }
    $approval = $reviewsJson | ConvertFrom-Json | Where-Object {
        $_.state -eq 'approved' -and $_.user.login -eq $owner -and
            ($_.environments | Where-Object { $_.id -eq $policy.id -and $_.name -eq $environmentName })
    }
    if (!$approval) { throw '当前发布请求缺少仓库所有者的 GitHub 审批记录' }
}
