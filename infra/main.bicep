// TheYard, as it runs (ADR: One plan, two sites). One Linux App Service plan
// and two web apps for containers, in infra/appservice.bicep, called from here
// as a module; and Azure Front Door in front of both with each origin locked to
// it (ADR-001), behind a parameter that defaults off, because the subscription
// still refuses Front Door: "Free Trial and Student account is forbidden for
// Azure Frontdoor resources", measured on 2026-08-31 and not yet retried on a
// paid subscription.
//
// Until 1.0.0.157 this file described a design nobody ran. It described App
// Service while the site ran on Container Instances, and carried a branch each
// for Container Instances and Container Apps that nothing deployed after day
// one. It is now the description of what runs, and those two branches are gone
// with the platforms they described (ADR-004, addendum of 2026-09-20).
//
// HOW THIS IS DEPLOYED, AND HOW IT IS NOT. Incremental mode, always:
//
//   az deployment group create -g RG-THEYARD-SS --mode Incremental \
//     -f infra/main.bicep -p appImage=<registry>/theyard:v<N> ...
//
// RG-THEYARD-SS also holds the two databases, the document store, the
// registry, the identity, Application Insights and the mail service, and none
// of them is in this file on purpose: each was created once, holds state or a
// name other things depend on, and is named and priced in the record's
// inventory. A complete-mode deployment deletes whatever a template leaves
// out, which here is every one of them. The template is the description.
// Deletion is a separate, named, reversible act, on the owner's word.

@description('Workload token used to derive resource names')
param baseName string = 'theyard'

@description('Owner tag appended to resource names per ADR-003')
param ownerTag string = 'SS'

@description('Region for the plan and both sites. westus3: westus2, where the registry and the document store are, answers a quota of zero for App Service on this subscription.')
param location string = 'westus3'

@description('Plan size: B1 is 1 vCPU and 1.75 GB shared by both sites, measured as enough with each site warming one store; B2 doubles both')
@allowed([
  'B1'
  'B2'
  'B3'
])
param skuName string = 'B1'

@description('The image both sites run, registry/name:tag. A deployment has to be told what is running, so that describing the infrastructure never rolls a site back.')
param appImage string

@description('Application Insights connection string, an ingestion key, passed in and never written here')
@secure()
param appInsightsConnectionString string

@description('The session signing key both sites share')
@secure()
param authSigningKey string

@description('The operator key for the Admin tab keyed rows')
@secure()
param adminKey string

@description('Deploy Front Door and lock both origins to it (ADR-001). Off: the free trial refuses Front Door, and the free edge in front of the sites today is Netlify.')
param enableFrontDoor bool = false

// #region naming
// Steve's naming rule in three lines: UPPERCASE for the things people read in
// the portal, lowercase for the ones Azure requires to be (a registry name must
// be lowercase alphanumeric), and a deterministic suffix from the resource
// group id so a name is unique without being random (ADR-003).
var suffix = uniqueString(resourceGroup().id)
var upperTag = toUpper('${baseName}-${ownerTag}')
var acrName = toLower('cr${baseName}${ownerTag}${suffix}')
// #endregion naming

// The registry the image is pulled from. Existing, not created here: it holds
// every image this project has shipped, and it was made on day one.
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

// #region what-runs
// The plan and the two sites, every setting included, in one module so the
// file that says what differs between the two sites says nothing else.
module compute 'appservice.bicep' = {
  name: 'compute'
  params: {
    location: location
    baseName: baseName
    ownerTag: ownerTag
    skuName: skuName
    appImage: appImage
    appInsightsConnectionString: appInsightsConnectionString
    authSigningKey: authSigningKey
    adminKey: adminKey
  }
}
// #endregion what-runs

// The two sites the module made, by the names it gave them, so Front Door can
// point at them and lock them.
var siteNames = [
  'APP-${upperTag}-${toUpper(suffix)}'
  'APP-${toUpper(baseName)}-COSMOS-${toUpper(ownerTag)}-${toUpper(suffix)}'
]

resource site 'Microsoft.Web/sites@2023-12-01' existing = [
  for name in siteNames: {
    name: name
  }
]

resource fdProfile 'Microsoft.Cdn/profiles@2024-02-01' = if (enableFrontDoor) {
  name: 'FD-${upperTag}'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

// #region origin-lock
// With Front Door on, nothing reaches either site except through it: the
// service tag admits Front Door's address space, and the header admits only
// this profile, because every Front Door customer shares that address space.
resource siteLock 'Microsoft.Web/sites/config@2023-12-01' = [
  for (name, i) in siteNames: if (enableFrontDoor) {
    parent: site[i]
    name: 'web'
    dependsOn: [
      compute
    ]
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
]
// #endregion origin-lock

// One endpoint, one origin group, one origin and one route per site: each site
// is its own public name, so each gets its own way in.
resource fdEndpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = [
  for (name, i) in siteNames: if (enableFrontDoor) {
    parent: fdProfile
    name: toLower('fde-${i == 0 ? baseName : '${baseName}-cosmos'}-${ownerTag}-${suffix}')
    location: 'global'
    properties: {
      enabledState: 'Enabled'
    }
  }
]

resource fdOriginGroup 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = [
  for (name, i) in siteNames: if (enableFrontDoor) {
    parent: fdProfile
    name: 'og-app-${i}'
    properties: {
      loadBalancingSettings: {
        sampleSize: 4
        successfulSamplesRequired: 3
        additionalLatencyInMilliseconds: 50
      }
      healthProbeSettings: {
        probePath: '/healthz'
        probeRequestType: 'GET'
        probeProtocol: 'Https'
        probeIntervalInSeconds: 100
      }
    }
  }
]

resource fdOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = [
  for (name, i) in siteNames: if (enableFrontDoor) {
    parent: fdOriginGroup[i]
    name: 'app-origin'
    dependsOn: [
      compute
    ]
    properties: {
      hostName: '${toLower(name)}.azurewebsites.net'
      originHostHeader: '${toLower(name)}.azurewebsites.net'
      httpPort: 80
      httpsPort: 443
      priority: 1
      weight: 1000
      enabledState: 'Enabled'
      enforceCertificateNameCheck: true
    }
  }
]

resource fdRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = [
  for (name, i) in siteNames: if (enableFrontDoor) {
    parent: fdEndpoint[i]
    name: 'route-all'
    dependsOn: [
      fdOrigin[i]
    ]
    properties: {
      originGroup: {
        id: fdOriginGroup[i].id
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
]

output acrLoginServer string = acr.properties.loginServer
output planId string = compute.outputs.planId
output origins array = compute.outputs.origins
