// The Shed's one web app on TheYard's plan (ADR-001). Everything shared is
// referenced as existing and never described here: the plan, the identity that
// pulls from the registry, the registry itself. Deploying this file creates or
// updates one site and touches nothing else. Incremental mode, always.
//
//   az deployment group create -g RG-THEYARD-SS --mode Incremental \
//     --template-file samples/maplarge/infra/site.bicep --parameters appImage=<registry>/theshed:v1

targetScope = 'resourceGroup'

@description('Where the plan is.')
param location string = 'westus3'

@description('The plan both TheYard sites run on; this site joins it.')
param planName string = 'PLAN-THEYARD-SS'

@description('The user-assigned identity that pulls images from the registry.')
param identityName string = 'id-theyard-ss'

@description('The image this site runs, registry/name:tag. Every roll sets it; a deployment has to be told what is running so it does not roll anything back.')
param appImage string

@description('The commit the image was built from, shown in the footer.')
param shedCommit string = 'unknown'

@description('The largest upload the public site accepts, in bytes. Ten megabytes: enough to try, not enough to fill the disk.')
param maxUploadBytes int = 10485760

// #region naming
var suffix = uniqueString(resourceGroup().id)
var siteName = 'APP-THESHED-SS-${toUpper(suffix)}'
// #endregion naming

resource plan 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: planName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

// #region site
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: siteName
  location: location
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOCKER|${appImage}'
      alwaysOn: true
      healthCheckPath: '/healthz'
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: identity.properties.clientId
      appSettings: [
        { name: 'WEBSITES_PORT', value: '8080' }
        { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'false' }
        { name: 'WEBSITES_CONTAINER_START_TIME_LIMIT', value: '300' }
        // The home is the sample tree inside the image. Uploads and deletes land on the
        // container's own disk and are gone at the next roll, which is what a public
        // demo of a file browser should be.
        { name: 'Files__Home', value: '' }
        { name: 'Files__MaxUploadBytes', value: string(maxUploadBytes) }
        { name: 'SHED_COMMIT', value: shedCommit }
      ]
    }
  }
}
// #endregion site

output siteName string = site.name
output origin string = 'https://${site.properties.defaultHostName}'
