// TheYard minimal deployment: ACR + App Service (container) behind Azure Front Door.
// Decisions and tradeoffs are recorded in docs/ADR-001-front-door-origin.md.
// Deliberately minimal for the first deploy; monitoring and polish come later.

@description('Base name used to derive resource names')
param baseName string = 'theyard'

@description('Region for regional resources; Front Door itself is global')
param location string = resourceGroup().location

@description('Container image; defaults to a public placeholder until the real image lands in ACR')
param appImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

var suffix = uniqueString(resourceGroup().id)
var acrName = 'cr${baseName}${suffix}'
var planName = 'plan-${baseName}'
var siteName = 'app-${baseName}-${suffix}'

// Container registry. Basic tier, admin user OFF: the web app pulls with its
// managed identity via the AcrPull role assignment below, which is the answer
// to the interview question about registry credentials.
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

// Front Door profile is declared before the site so the site's access
// restriction can pin this profile's unique frontDoorId header value.
resource fdProfile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: 'fd-${baseName}'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

// Linux App Service plan. B1: smallest tier with always-on, predictable cost.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
  }
  properties: {
    reserved: true
  }
}

// The web app: single container, system-assigned identity, and the origin
// lock: default Deny, allow only Front Door's service tag AND only when the
// X-Azure-FDID header matches THIS profile, so other Front Doors bounce too.
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: siteName
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
              fdProfile.properties.frontDoorId
            ]
          }
        }
      ]
    }
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

resource fdEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: fdProfile
  name: 'fde-${baseName}-${suffix}'
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource fdOriginGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: fdProfile
  name: 'og-app'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
      additionalLatencyInMilliseconds: 50
    }
    // The probe is what keeps the origin permanently awake; that tradeoff is
    // accepted in ADR-001. 100s is the gentlest useful cadence.
    healthProbeSettings: {
      probePath: '/api/facets'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 100
    }
  }
}

resource fdOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
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

resource fdRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: fdEndpoint
  name: 'route-all'
  dependsOn: [
    fdOrigin
  ]
  properties: {
    originGroup: {
      id: fdOriginGroup.id
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
output frontDoorUrl string = 'https://${fdEndpoint.properties.hostName}'