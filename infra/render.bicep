// The rendering service's web app (ADR: A rendering service beside the API), the
// second container on the plan, beside the one web app that serves both sites. It
// draws each site's pages by asking that app's API with the site's store named on
// every read, so it holds no secret, no connection string and no store: its
// settings are where it listens and which API it reads. It stays a web app of its
// own because Node is its own runtime and its own roll (ADR: One web app for every
// .NET process).
//
// A template of its own beside appservice.bicep, because a deployment of
// appservice.bicep writes the web app's settings whole, two secrets among them,
// and creating the renderer must not be able to touch them. main.bicep
// names it as a module beside the plan's, so what runs is still one file; the
// renderer was created from this file alone, on 8 October.
// Deployed in incremental mode, always, as appservice.bicep is.

@description('Region, as the plan and both sites')
param location string = 'westus3'

@description('Workload token, as in main.bicep (ADR-003)')
param baseName string = 'theyard'

@description('Owner tag, as in main.bicep (ADR-003)')
param ownerTag string = 'SS'

@description('The image it runs, registry/theyard-render:tag. Every roll sets this; a deployment of this file is told what is running so it rolls nothing back.')
param renderImage string

@description('The user-assigned identity both sites run as; the renderer uses it for the registry pull and nothing else')
param identityName string = 'id-theyard-ss'

// #region render-naming
var suffix = uniqueString(resourceGroup().id)
var upperTag = toUpper('${baseName}-${ownerTag}')
var planName = 'PLAN-${upperTag}'
var renderName = 'APP-${toUpper(baseName)}-RENDER-${toUpper(ownerTag)}-${toUpper(suffix)}'
// The one web app it reads both sites from, by the name appservice.bicep gives it.
var oneSite = 'APP-${toUpper(baseName)}-ONE-${toUpper(ownerTag)}-${toUpper(suffix)}'
// #endregion render-naming

resource plan 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: planName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

// #region render-site
resource render 'Microsoft.Web/sites@2023-12-01' = {
  name: renderName
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
      linuxFxVersion: 'DOCKER|${renderImage}'
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
        // Each site by name, both read at the one web app's origin; the name is the store each read
        // names in X-Yard-Store, and the edge's rule for each domain names the path.
        { name: 'YARD_SITES', value: 'sql=https://${toLower(oneSite)}.azurewebsites.net,cosmos=https://${toLower(oneSite)}.azurewebsites.net' }
        { name: 'YARD_DEADLINE_MS', value: '2500' }
        // Node's heap held under the measured peak's ceiling, so a leak ends in a restart and not in the plan's memory,
        // measured 2026-10-19 at 18 MB of heap in use and 79 MB resident. Only the flags Node allows in NODE_OPTIONS
        // go here: --optimize_for_size is not one, and set here on 19 October it stopped the container starting.
        { name: 'NODE_OPTIONS', value: '--max-old-space-size=128' }
      ]
    }
  }
}
// #endregion render-site

output renderOrigin string = 'https://${render.properties.defaultHostName}'
