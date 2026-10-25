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
var blobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
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
    // Transaction optimized, not Hot (2026-10-24): the share is small and busy, so the bill is its
    // transactions, which cost about a fifth of Hot's here; the gigabytes cost a little more.
    accessTier: 'TransactionOptimized'
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

// The blog's pictures (ADR-113, the addendum on where the pictures live): private, read and written only by
// TheYard's web identity, which serves them from the site's own address with a year's cache.
resource blogContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'blog'
  properties: {
    publicAccess: 'None'
    metadata: { purpose: 'the blog\'s pictures, served at /blog/images/<hash>' }
  }
}

// The hook for a cap on the pictures: a lifecycle rule that is off until a cap is chosen.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          enabled: false
          name: 'blog-cap-hook'
          type: 'Lifecycle'
          definition: {
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 36500 } } }
            filters: { blobTypes: [ 'blockBlob' ], prefixMatch: [ 'blog/' ] }
          }
        }
      ]
    }
  }
}

@description('TheYard web identity (id-theyard-ss) principal id, which writes the blog pictures; empty leaves the grant out.')
param siteIdentityPrincipalId string = ''

resource blogWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(siteIdentityPrincipalId)) {
  name: guid(blogContainer.id, siteIdentityPrincipalId, blobDataContributor)
  scope: blogContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributor)
    principalId: siteIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
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

// The campaign's database (infra/jobhunt-sql.bicep, ADR-118): the Function signs in as its own identity, so the
// connection string names the identity's client id and holds no secret.
@description('The campaign database\'s logical server (infra/jobhunt-sql.bicep).')
param campaignSqlServer string = 'sql-theyard-ss-westus3'
@description('The campaign\'s database on it.')
param campaignSqlDatabase string = 'sqldb-jobhunt-ss'
var campaignSql = 'Server=tcp:${campaignSqlServer}${environment().suffixes.sqlServerHostname},1433;Initial Catalog=${campaignSqlDatabase};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};'

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
        // The Sheet mirror (functions/JobHunt.Application/Features/Sheet): 'shadow' writes its plan to the share beside the records
        // run's and adds nothing; 'on' appends the master's missing rows to the Sheet and checks the read-back;
        // anything else is off. The Google client, its refresh token and the Sheet's id are set out of band.
        { name: 'SHEET_MIRROR', value: 'shadow' }
        // The 30-minute inbox pass (functions/jobhunt/AtsSweepTimer.cs, at 20 and 50 past each hour), three parts.
        // The ATS sweep (functions/JobHunt.Application/Features/Ats): 'on' drops the rows in the spool, archives and reads the labels back;
        // 'shadow' writes its plan and archives nothing; anything else is off.
        { name: 'ATS_SWEEP', value: 'on' }
        // The leads harvest (functions/JobHunt.Application/Features/Leads): 'on' appends each whole alert's jobs to nightly/leads-<date>.md,
        // then archives it; 'shadow' writes leads-shadow/ only.
        { name: 'LEADS', value: 'on' }
        // The leads list: 'on' fires nightly.yml's leads run (Sonnet, low effort,
        // LEADS-LANE.md) when a pass harvested a lead that passed the filter and that leads/scored.tsv does not hold;
        // 'shadow' writes what it would fire to the pass's line; anything else never starts it.
        { name: 'LEADS_LIST', value: 'shadow' }
        // Human mail never waits for 4 AM (rung 5): 'on' fires nightly.yml's reply run (drafts only) when a person
        // waits on a thread with no draft, at most once every four hours between 7 AM and 9 PM Central; 'shadow'
        // writes what it would fire to the pass's line; anything else never starts it.
        { name: 'REPLY_ON_HUMAN', value: 'shadow' }
        // The model's step: 'on' fires nightly.yml's triage run (Sonnet, low effort) when the inbox holds mail
        // that no triage run has judged; anything else never starts it.
        { name: 'TRIAGE', value: 'on' }
        // The daily card (functions/JobHunt.Infrastructure/Features/Card): 'on' keeps nightly/CARD.md current, the one page Steve reads.
        { name: 'DAILY_CARD', value: 'on' }
        // The hiring-manager email campaign (functions/jobhunt/CampaignSendTimer.cs, ADR-118), at 8 AM Central seven
        // days a week: 'shadow' writes what it would send to EmailsSent and calls no mail service; 'on' sends from
        // Steve's Gmail, at most ten a day with a three, five, ten start; anything else is off. 'on' is Steve's
        // switch after seven mornings of shadow, never a deploy's.
        { name: 'EMAIL_CAMPAIGN', value: 'shadow' }
        { name: 'CAMPAIGN_SQL', value: campaignSql }
      ]
    }
    // #endregion jobhunt-settings
  }
}

output shareUnc string = '\\\\${storage.name}.file.${environment().suffixes.storage}\\${shareName}'
output identityPrincipalId string = identity.properties.principalId
output functionApp string = app.name
