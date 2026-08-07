<#
.SYNOPSIS
    成员合并去重：把同一人的多条 Gallery 成员记录合并为一条（特征等权融合，删除被合并的成员）。

.DESCRIPTION
    调用 POST /family/merge/{groupId}，targetMemberId 保留，mergeMemberIds 中的成员
    特征等权融合进目标成员后被删除。适合清理"赖国伟-背面/赖国伟-正面"这类同一人多条记录。

.EXAMPLE
    .\merge_members.ps1 -GroupId group1 -TargetMemberId 9f51332364ad -MergeMemberIds 947b55f15b23
#>
param(
    [Parameter(Mandatory = $true)][string]$GroupId,
    [Parameter(Mandatory = $true)][string]$TargetMemberId,
    [Parameter(Mandatory = $true)][string[]]$MergeMemberIds,
    [int]$Port = 9000
)

$body = @{ targetMemberId = $TargetMemberId; mergeMemberIds = $MergeMemberIds } | ConvertTo-Json -Compress
$r = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/family/merge/$GroupId" -Method Post -ContentType 'application/json' -Body $body

"合并后的成员列表 (group=$GroupId):"
$r | ForEach-Object { "  - $($_.id)  $($_.name)  enrolled=$($_.enrolledAt)" }
