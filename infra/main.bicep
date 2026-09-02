// TheYard deployment. Production target (ADR-001): App Service origin behind
// Azure Front Door with the origin locked to the Front Door ID header.
// Author decision 2026-08-31: on the trial subscription, run WITHOUT Front
// Door or any restricted resource; the target stays the documented best
// practice for production and activates by parameters after an upgrade.
// computeKind: appservice (target) | containerapp | aci (trial-compatible).

@description('Workload token used to derive resource names')
param baseName string = 'theyard'

@description('Owner tag appended to resource names per ADR-003')
param ownerTag string = 'SS'

@description('Region for regional resources; Front Door itself is global')
param location string = resourceGroup().location

@description('Container image; the placeholder default is replaced by the ACR image once built')
param appImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Deploy Front Door and lock the origin to it (production best practice; requires pay-as-you-go)')
param enableFrontDoor bool = true

@description('App Service plan SKU when computeKind is appservice')
param skuName string = 'B1'

@description('Compute platform')
@allowed([
  'appservice'
  'containerapp'
  'aci'
])
param computeKind string = 'appservice'

@description('Minimum replicas for the container app path')
param minReplicas int = 1

// #region naming
var suffix = uniqueString(resourceGroup().id)
var upperTag = toUpper('${baseName}-${ownerTag}')
var acrName = toLower('cr${baseName}${ownerTag}${suffix}')
// #endregion naming
var useAppService = computeKind == 'appservice'
var useContainerApp = computeKind == 'containerapp'
var useAci = computeKind == 'aci'

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
  name: 'FD-${upperTag}'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = if (useAppService) {
  name: 'PLAN-${upperTag}'
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
  name: 'APP-${upperTag}-${toUpper(suffix)}'
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

// #region origin-lock
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
// #endregion origin-lock

resource acrPullSite 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useAppService) {
  name: guid(acr.id, 'site', 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: site!.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = if (useContainerApp) {
  name: toLower('cae-${baseName}-${ownerTag}')
  location: location
  properties: {}
}

resource caApp 'Microsoft.App/containerApps@2024-03-01' = if (useContainerApp) {
  name: toLower('ca-${baseName}-${ownerTag}-${suffix}')
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
        minReplicas: minReplicas
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

// ACI branch: user-assigned identity because the container group needs a
// registry credential at creation time, and a system identity cannot grant
// itself AcrPull before it exists.
resource uai 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = if (useAci) {
  name: toLower('id-${baseName}-${ownerTag}')
  location: location
}

resource acrPullUai 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useAci) {
  name: guid(acr.id, 'uai', 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: uai!.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource aci 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = if (useAci) {
  name: toLower('aci-${baseName}-${ownerTag}')
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uai!.id}': {}
    }
  }
  properties: {
    osType: 'Linux'
    restartPolicy: 'Always'
    ipAddress: {
      type: 'Public'
      dnsNameLabel: toLower('${baseName}-${ownerTag}-${suffix}')
      ports: [
        {
          protocol: 'TCP'
          port: 8080
        }
      ]
    }
    imageRegistryCredentials: [
      {
        server: acr.properties.loginServer
        identity: uai!.id
      }
    ]
    containers: [
      {
        name: 'theyard'
        properties: {
          image: appImage
          ports: [
            {
              port: 8080
            }
          ]
          resources: {
            requests: {
              cpu: 1
              memoryInGB: json('1.5')
            }
          }
        }
      }
    ]
  }
  dependsOn: [
    acrPullUai
  ]
}

resource fdEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = if (enableFrontDoor) {
  parent: fdProfile
  name: toLower('fde-${baseName}-${ownerTag}-${suffix}')
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
output appName string = useAci ? aci!.name : (useContainerApp ? caApp!.name : site!.name)
output appUrl string = useAci ? 'http://${aci!.properties.ipAddress.fqdn}:8080' : (useContainerApp ? 'https://${caApp!.properties.configuration.ingress.fqdn}' : 'https://${site!.properties.defaultHostName}')