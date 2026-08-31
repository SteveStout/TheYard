// TheYard deployment. Target design (ADR-001): App Service origin behind Azure
// Front Door with the origin locked to the Front Door ID header. Both of those
// are forbidden or quota-zero on a free-trial subscription (measured
// 2026-08-31, quoted in the ADR addenda), so the template carries three
// switches that describe the whole journey:
//   computeKind      appservice (target) | containerapp (works on the trial)
//   skuName          B1 (target) | F1 (was also quota-blocked on the trial)
//   enableFrontDoor  true (target) | false (forbidden on the trial)
// Trial deploy: computeKind=containerapp, enableFrontDoor=false.
// Post-upgrade:  computeKind=appservice, skuName=B1, enableFrontDoor=true.

@description('Base name used to derive resource names')
param baseName string = 'theyard'

@description('Region for regional resources; Front Door itself is global')
param location string = resourceGroup().location

@description('Container image; defaults to a public placeholder until the real image lands in ACR')
param appImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Deploy Front Door and lock the origin to it. Requires pay-as-you-go and computeKind=appservice.')
param enableFrontDoor bool = true

@description('App Service plan SKU when computeKind is appservice.')
param skuName string = 'B1'

@description('Compute platform: appservice is the ADR-001 target; containerapp is the trial-compatible path.')
@allowed([
  'appservice'
  'containerapp'
])
param computeKind string = 'appservice'

var suffix = uniqueString(resourceGroup().id)
var acrName = 'cr${baseName}${suffix}'
var useAppService = computeKind == 'appservice'
var useContainerApp = computeKind == 'containerapp'

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

// ---------- App Service branch (the ADR-001 target) ----------

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = if (useAppService) {
  name: 'plan-${baseName}'
  location: location
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = if (useAppService) {
  name: 'app-${baseName}-${suffix}'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan!.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|${appImage}'
      alwaysOn: skuName == 'F1' ? false : true
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

resource siteLock 'Microsoft.Web/sites/config@2023-12-01' = if (useAppService && enableFrontDoor) {
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

resource acrPullSite 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useAppService) {
  name: guid(acr.id, 'site', 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: site!.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Container Apps branch (trial-compatible; also the shape that
// scales to zero, which works BECAUSE Front Door is absent here) ----------

resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = if (useContainerApp) {
  name: 'cae-${baseName}'
  location: location
  properties: {}
}

resource caApp 'Microsoft.App/containerApps@2024-03-01' = if (useContainerApp) {
  name: 'ca-${baseName}-${suffix}'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: caEnv!.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'theyard'
          image: appImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

resource acrPullCa 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useContainerApp) {
  name: guid(acr.id, 'ca', 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: caApp!.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Front Door details (activate post-upgrade with appservice) ----------

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
    hostName: site!.properties.defaultHostName
    originHostHeader: site!.properties.defaultHostName
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
output appHost string = useContainerApp ? caApp!.properties.configuration.ingress.fqdn : site!.properties.defaultHostName
output appName string = useContainerApp ? caApp!.name : site!.name
output computeKindOut string = computeKind