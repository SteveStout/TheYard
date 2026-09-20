# deploy-infra.ps1: describe the infrastructure to Azure again, without changing what runs.
#
#   powershell -File scripts/deploy-infra.ps1            asks Azure what a deployment WOULD change, and changes nothing
#   powershell -File scripts/deploy-infra.ps1 -Apply     deploys infra/main.bicep, in incremental mode
#
# A web app's settings are replaced whole by a template deployment, and four of them are not in the repository:
# the image tag the last roll set, the telemetry connection string, the session signing key and the operator's
# key. So this reads those four off the first site as it runs, hands them back to the template as parameters,
# and the deployment describes the sites as they already are. The values pass through a parameters file that
# is emptied before this script returns, and none of them is ever printed.
#
# Incremental mode, always, and the mode is written out rather than left to the default. The resource group
# holds the databases, the document store, the registry and the identity, and none of them is in the template:
# a complete-mode deployment would delete every one (ADR: One plan, two sites). A test refuses the other
# word anywhere in this folder.
param([switch]$Apply)

$ErrorActionPreference = "Stop"
$rg = "RG-THEYARD-SS"
$first = "APP-THEYARD-SS-ZMNETJ67BN5H2"
$root = Split-Path -Parent $PSScriptRoot

$fx = (az webapp config show -g $rg -n $first --query linuxFxVersion -o tsv)
if ($fx -notmatch '^DOCKER\|(.+)$') { throw "could not read the image $first runs (got '$fx')" }
$image = $Matches[1]

$settings = @{}
(az webapp config appsettings list -g $rg -n $first -o json | ConvertFrom-Json) | ForEach-Object { $settings[$_.name] = $_.value }
foreach ($name in "APPLICATIONINSIGHTS_CONNECTION_STRING", "Auth__SigningKey", "Admin__Key") {
    if (-not $settings[$name]) { throw "$first holds no $name, and deploying without it would blank it" }
}

$parameters = Join-Path ([IO.Path]::GetTempPath()) "theyard-infra-parameters.json"
try {
    @{
        '$schema'      = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#"
        contentVersion = "1.0.0.0"
        parameters     = @{
            appImage                    = @{ value = $image }
            appInsightsConnectionString = @{ value = $settings["APPLICATIONINSIGHTS_CONNECTION_STRING"] }
            authSigningKey              = @{ value = $settings["Auth__SigningKey"] }
            adminKey                    = @{ value = $settings["Admin__Key"] }
        }
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $parameters -Encoding ascii

    Write-Host "image: $image (three secret values read from $first, not shown)"
    if ($Apply) {
        az deployment group create -g $rg --name "theyard-infra" --mode Incremental `
            --template-file (Join-Path $root "infra/main.bicep") --parameters "@$parameters" `
            --query "{state:properties.provisioningState, duration:properties.duration, origins:properties.outputs.origins.value}" -o json
    }
    else {
        az deployment group what-if -g $rg --name "theyard-infra" --mode Incremental `
            --template-file (Join-Path $root "infra/main.bicep") --parameters "@$parameters"
    }
}
finally {
    if (Test-Path $parameters) { Set-Content -Path $parameters -Value "{}"; Remove-Item $parameters -Force }
}
