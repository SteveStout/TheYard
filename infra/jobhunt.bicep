// The job hunt, cloud native (mentor\PROMPT-JOBHUNT-CLOUD-NATIVE.md in Steve's
// job-hunt folder). Not part of TheYard's site: it shares the repository so the
// nightly workflow and its Function live beside the rest of his Azure work, and
// it deploys to its own resource group, RG-JOBHUNT-SS, never to RG-THEYARD-SS.
//
// What it holds, and nothing else:
//   - the storage account and the jobhunt share (the job-hunt folder, in Azure),
//     soft delete kept 14 days; the daily snapshot is taken by the Function;
//   - the Function app on Flex Consumption and its plan;
//   - Application Insights (appi-jobhunt-ss) on TheYard's Log Analytics workspace;
//   - one user-assigned identity with ONE role: Storage File Data SMB Share
//     Contributor on the share. functions/jobhunt/role-check.ps1 reads its
//     assignments and fails on any other.
//
// The Function reaches storage with the account key held in its own app
// settings, so the identity needs no blob or queue role; that is what keeps it
// at one assignment.
//
// HOW THIS IS DEPLOYED. Incremental mode, always:
//
//   az deployment group create -g RG-JOBHUNT-SS --mode Incremental -f infra/jobhunt.bicep

@description('Region. centralus: closest Azure region to Saint Charles, and Flex Consumption is offered there (read with az functionapp list-flexconsumption-locations on 2026-10-20).')
param location string = 'centralus'

@description('Storage account name, globally unique, lowercase')
param storageName string = 'stjobhuntss'

@description('Function app name')
param functionAppName string = 'func-jobhunt-ss'

@description('The share holding the job-hunt folder')
param shareName string = 'jobhunt'

@description('The Log Analytics workspace the job hunt\'s Application Insights writes to: TheYard\'s, so one workspace holds both.')
param logWorkspaceId string = '/subscriptions/df3b718c-6d99-4904-8102-6f865941f640/resourceGroups/RG-THEYARD-SS/providers/Microsoft.OperationalInsights/workspaces/log-theyard-ss'

var smbShareContributor = '0c867c2a-1d8c-454a-a3db-ab2ea1bdc8bb'
var deployContainer = 'deploy'
var tags = { owner: 'SS', lane: 'jobhunt-cloud-native' }

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    shareDeleteRetentionPolicy: { enabled: true, days: 14 }
  }
}

resource share 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: shareName
  properties: {
    shareQuota: 100
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploy 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deployContainer
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-jobhunt-ss'
  location: location
  tags: tags
}

resource shareRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(share.id, identity.id, smbShareContributor)
  scope: share
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', smbShareContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// The Function's telemetry: every function's invocations, failures and logs, in TheYard's workspace.
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-jobhunt-ss'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspaceId
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-jobhunt-ss'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deployContainer}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      scaleAndConcurrency: { maximumInstanceCount: 40, instanceMemoryMB: 512 }
      runtime: { name: 'dotnet-isolated', version: '9.0' }
    }
    // #region jobhunt-settings
    // This list REPLACES the app's settings on every deploy of this template: a setting that is on
    // in Azure and missing here is off after the next deploy. functions/jobhunt.Tests holds the
    // list to every name the Function reads, and MERGER and both dispatches to 'on' as they run
    // (ADR: The tracker merger runs in Azure).
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnection }
        { name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING', value: storageConnection }
        { name: 'JOBHUNT_SHARE_CONNECTION', value: storageConnection }
        { name: 'JOBHUNT_SHARE_NAME', value: shareName }
        // Application Insights for every function in the app (the host reads it; no code does).
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        // The 4:00 AM reply lane and the four-hour records run, both on in Azure. NIGHTLY_GITHUB_TOKEN
        // is set out of band with az functionapp config appsettings set, never here; this list
        // replaces the app's settings on a redeploy, so set the token again after one.
        { name: 'NIGHTLY_DISPATCH', value: 'on' }
        { name: 'BOOKKEEPING_DISPATCH', value: 'on' }
        { name: 'NIGHTLY_REPO', value: 'SteveStout/TheYard' }
        // The tracker merger: the only writer of Job_Hunt_Tracker.csv and its moved-out file on the
        // share. 'off' hands the tracker back to the laptop's merger (the roll back in the record).
        { name: 'MERGER', value: 'on' }
        // The shared .md files' appends, on since the cutover: the same Merger is their one writer. 'off'
        // hands them back to the laptop's merger (-AppendsOnly), with the two switches in its scripts.
        { name: 'APPENDS', value: 'on' }
        // The Sheet mirror (functions/jobhunt/Sheet): 'shadow' writes its plan to the share beside the records
        // run's and adds nothing; 'on' appends the master's missing rows to the Sheet and checks the read-back;
        // anything else is off. The Google client, its refresh token and the Sheet's id are set out of band.
        { name: 'SHEET_MIRROR', value: 'shadow' }
        // The 30-minute inbox pass (functions/jobhunt/AtsSweepTimer.cs, at 20 and 50 past each hour), three parts.
        // The ATS sweep (functions/jobhunt/Ats): 'on' drops the rows in the spool, archives and reads the labels back;
        // 'shadow' writes its plan and archives nothing; anything else is off.
        { name: 'ATS_SWEEP', value: 'on' }
        // The leads harvest (functions/jobhunt/Leads): 'on' appends each whole alert's jobs to nightly/leads-<date>.md,
        // then archives it; 'shadow' writes leads-shadow/ only.
        { name: 'LEADS', value: 'on' }
        // The model's step: 'on' fires nightly.yml's triage run (Sonnet, low effort) when the inbox holds mail from
        // the last two days that no triage run has judged; anything else never starts it.
        { name: 'TRIAGE', value: 'on' }
        // The daily card (functions/jobhunt/Card): 'on' keeps nightly/CARD.md current, the one page Steve reads.
        { name: 'DAILY_CARD', value: 'on' }
      ]
    }
    // #endregion jobhunt-settings
  }
}

output shareUnc string = '\\\\${storage.name}.file.${environment().suffixes.storage}\\${shareName}'
output identityPrincipalId string = identity.properties.principalId
output functionApp string = app.name
