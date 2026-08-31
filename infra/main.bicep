// TheYard deployment: ACR + App Service (container), optionally fronted by
// Azure Front Door. Decisions in docs/ADR-001-front-door-origin.md.
//
// Reality check, measured 2026-08-31: free-trial subscriptions are FORBIDDEN
// from creating Front Door resources ("Free Trial and Student account is
// forbidden for Azure Frontdoor resources"). Until the subscription upgrades
// to pay-as-you-go, deploy with enableFrontDoor=false and the app serves from
// its azurewebsites.net address; flip it to true after the upgrade to add the
// edge plus the origin lock without touching anything else.

@description('Base name used to derive resource names')
param baseName string = 'theyard'

@description('Region for regional resources; Front Door itself is global')
param location string = resourceGroup().location

@description('Container image; defaults to a public placeholder until the real image lands in ACR')
param appImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Deploy Front Door and lock the origin to it. Requires a pay-as-you-go subscription.')
param enableFrontDoor bool = true

var suffix = uniqueString(resourceGroup().id)
var acrName = 'cr${baseName}${suffix}'

// Container registry. Basic tier, admin user OFF: the web app pulls with its
// managed identity via the AcrPull role assignment below.
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource fdProfile 'Microsoft.Cdn/profiles@2024-02-01' = if (enableFrontDoor) {
  name: 'fd-${baseName}'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

// Linux App Service plan. B1: smallest tier with always-on, predictable cost.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${baseName}'
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
  }
  properties: {
    reserved: true
  }
}

// The web app: single container, system-assigned identity. The Front Door
// origin lock lives in a separate conditional config resource below so this
// resource stays valid whether or not Front Door exists.
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${baseName}-${suffix}'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|${appImage}'
      alwaysOn: true
      acrUseManagedIdentityCreds: true
      appSettings: [
        {
          name: 'WEBSITES_PORT'
          value: '8080'
        }
      ]
    }
  }
}

// Origin lock, only when Front Door exists: default Deny, allow only Front
// Door's service tag AND only when X-Azure-FDID matches THIS profile.
resource siteLock 'Microsoft.Web/sites/config@2023-12-01' = if (enableFrontDoor) {
  parent: site
  name: 'web'
  properties: {
    ipSecurityRestrictionsDefaultAction: 'Deny'
    ipSecurityRestrictions: [
      {
        name: 'AllowFrontDoorOnly'
        priority: 100
        action: 'Allow'
        tag: 'ServiceTag'
        ipAddress: 'AzureFrontDoor.Backend'
        headers: {
          'x-azure-fdid': [
            fdProfile!.properties.frontDoorId
          ]
        }
      }
    ]
  }
}

// AcrPull for the site identity: pull rights, nothing more.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, site.id, 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource fdEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = if (enableFrontDoor) {
  parent: fdProfile
  name: 'fde-${baseName}-${suffix}'
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource fdOriginGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = if (enableFrontDoor) {
  parent: fdProfile
  name: 'og-app'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    healthProbeSettings: {
      probePath: '/api/facets'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 100
    }
  }
}

resource fdOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = if (enableFrontDoor) {
  parent: fdOriginGroup
  name: 'app-origin'
  properties: {
    hostName: site.properties.defaultHostName
    originHostHeader: site.properties.defaultHostName
    httpPort: 80
    httpsPort: 443
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
    enforceCertificateNameCheck: true
  }
}

resource fdRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = if (enableFrontDoor) {
  parent: fdEndpoint
  name: 'route-all'
  dependsOn: [
    fdOrigin
  ]
  properties: {
    originGroup: {
      id: fdOriginGroup!.id
    }
    supportedProtocols: [
      'Http'
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
  }
}

output acrNameOut string = acr.name
output acrLoginServer string = acr.properties.loginServer
output webAppName string = site.name
output webAppHost string = site.properties.defaultHostName
output frontDoorEnabled bool = enableFrontDoor