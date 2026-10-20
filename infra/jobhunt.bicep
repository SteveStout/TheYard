// The job hunt, cloud native (mentor\PROMPT-JOBHUNT-CLOUD-NATIVE.md in Steve's
// job-hunt folder). Not part of TheYard's site: it shares the repository so the
// nightly workflow and its Function live beside the rest of his Azure work, and
// it deploys to its own resource group, RG-JOBHUNT-SS, never to RG-THEYARD-SS.
//
// What it holds, and nothing else:
//   - the storage account and the jobhunt share (the job-hunt folder, in Azure),
//     soft delete kept 14 days; the daily snapshot is taken by the Function;
//   - the Function app on Flex Consumption and its plan;
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
    siteConfig: {
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnection }
        { name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING', value: storageConnection }
        { name: 'JOBHUNT_SHARE_CONNECTION', value: storageConnection }
        { name: 'JOBHUNT_SHARE_NAME', value: shareName }
        // Off until step 0's hand run is measured; set to on with az functionapp config appsettings set.
        { name: 'NIGHTLY_DISPATCH', value: 'off' }
        { name: 'NIGHTLY_REPO', value: 'SteveStout/TheYard' }
      ]
    }
  }
}

output shareUnc string = '\\\\${storage.name}.file.${environment().suffixes.storage}\\${shareName}'
output identityPrincipalId string = identity.properties.principalId
output functionApp string = app.name
