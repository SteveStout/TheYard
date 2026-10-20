# Reads the job hunt identity's role assignments and fails on anything but the one the
# template grants: Storage File Data SMB Share Contributor on the jobhunt share.
param([string]$Group = 'RG-JOBHUNT-SS', [string]$Identity = 'id-jobhunt-ss')
$principal = az identity show -g $Group -n $Identity --query principalId -o tsv
$roles = az role assignment list --assignee $principal --all --query "[].{role:roleDefinitionName,scope:scope}" -o json | ConvertFrom-Json
$expected = $roles | Where-Object { $_.role -eq 'Storage File Data SMB Share Contributor' -and $_.scope -like '*/shares/jobhunt' }
$roles | Format-Table -AutoSize
if ($roles.Count -ne 1 -or -not $expected) { Write-Host "FAIL: expected exactly one assignment, found $($roles.Count)"; exit 1 }
Write-Host 'PASS: one assignment, the share role'
