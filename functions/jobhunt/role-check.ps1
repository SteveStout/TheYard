# Reads the job hunt identity's role assignments and fails on anything but the one the
# template grants: Storage File Data SMB Share Contributor on the jobhunt share. Then reads what the
# site's identity holds on the group and fails on anything but the two read-only roles the Admin
# tab's nightly card reads with: Reader and Monitoring Reader (ADR: The Nightly Function on the
# Admin tab, the addendum on the roles).
param([string]$Group = 'RG-JOBHUNT-SS', [string]$Identity = 'id-jobhunt-ss', [string]$SiteGroup = 'RG-THEYARD-SS', [string]$Site = 'id-theyard-ss')
$principal = az identity show -g $Group -n $Identity --query principalId -o tsv
$roles = az role assignment list --assignee $principal --all --query "[].{role:roleDefinitionName,scope:scope}" -o json | ConvertFrom-Json
$expected = $roles | Where-Object { $_.role -eq 'Storage File Data SMB Share Contributor' -and $_.scope -like '*/shares/jobhunt' }
$roles | Format-Table -AutoSize
if ($roles.Count -ne 1 -or -not $expected) { Write-Host "FAIL: expected exactly one assignment, found $($roles.Count)"; exit 1 }
Write-Host 'PASS: one assignment, the share role'
$scope = (az group show -n $Group --query id -o tsv)
$sitePrincipal = az identity show -g $SiteGroup -n $Site --query principalId -o tsv
# Every assignment the site's identity holds anywhere, kept when its scope is the group or a
# resource inside it, so a data role on the storage account counts as much as one on the group.
$held = @(az role assignment list --assignee $sitePrincipal --all --query "[].{role:roleDefinitionName,scope:scope}" -o json | ConvertFrom-Json) | Where-Object { $_.scope -like "$scope*" }
$held | ForEach-Object { Write-Host "  $Site on $($_.scope): $($_.role)" }
$siteRoles = @($held | ForEach-Object { if ($_.scope -eq $scope) { $_.role } else { "$($_.role) on $($_.scope)" } }) | Sort-Object
if (($siteRoles -join ',') -ne 'Monitoring Reader,Reader') { Write-Host "FAIL: expected Reader and Monitoring Reader for $Site on $Group, found $($siteRoles -join ', ')"; exit 1 }
Write-Host "PASS: $Site reads $Group, and writes nothing there"
