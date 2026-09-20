// One plan, two sites (ADR: One plan, two sites). What runs: a Linux B1 App
// Service plan and two web apps for containers on it, one per site, both
// running the same image as the same user-assigned identity. The two sites
// differ in which store a request gets when it names none, and in which
// address each calls its own and its peer's. Everything else is one list.
//
// Deployed in incremental mode, always. This resource group also holds the
// databases, the document store, the registry and the identity, and none of
// them is in this file; a complete-mode deployment would delete every one.
// The template is the description. Deletion is a separate, named act.

@description('Region for the plan and both sites. westus3 because westus2 refuses App Service on this subscription (measured 2026-08-31 and again 2026-09-20), and because the relational server is already there.')
param location string = 'westus3'

@description('Workload token, as in main.bicep (ADR-003)')
param baseName string = 'theyard'

@description('Owner tag, as in main.bicep (ADR-003)')
param ownerTag string = 'SS'

@description('Plan size. B1 is 1 vCPU and 1.75 GB shared by both sites; B2 doubles both.')
@allowed([
  'B1'
  'B2'
  'B3'
])
param skuName string = 'B1'

@description('The image both sites run, registry/name:tag. Every roll sets this on the sites; a deployment of this file has to be told what is running so it does not roll anything back.')
param appImage string

@description('The user-assigned identity both sites run as: registry pull, both stores, mail, and the reads of its own resource')
param identityName string = 'id-theyard-ss'

@description('Azure SQL logical server. A name, not a secret: the sites sign in as the identity and there is no password to hold.')
param sqlServer string = 'sql-theyard-ss-westus3'

@description('The database on that server the sites run on (ADR: The SQL Server backend, addendum of 14 September)')
param sqlDatabase string = 'sqldb-theyard-ss-basic'

@description('Application Insights connection string. An ingestion key, so it is passed in and never written here.')
@secure()
param appInsightsConnectionString string

@description('The session signing key both sites share, so a roll does not end every session')
@secure()
param authSigningKey string

@description('The operator key for the Admin tab keyed rows')
@secure()
param adminKey string

// #region plan-naming
var suffix = uniqueString(resourceGroup().id)
var upperTag = toUpper('${baseName}-${ownerTag}')
var planName = 'PLAN-${upperTag}'
// Two sites, named the way the two container groups were: the first is the
// workload, the second says which store it defaults to.
var siteNames = [
  'APP-${upperTag}-${toUpper(suffix)}'
  'APP-${toUpper(baseName)}-COSMOS-${toUpper(ownerTag)}-${toUpper(suffix)}'
]
// #endregion plan-naming

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

// #region two-sites
// What differs between the sites, and nothing else. Each site's peer is the
// other one, read server side at its azurewebsites.net address for the
// comparison card, and linked for visitors at its public name behind the edge.
var sites = [
  {
    name: siteNames[0]
    storeDefault: 'sql'
    siteUrl: 'https://theyard.stevenstout.biz'
    peerSite: 'https://theyard-cosmos.stevenstout.biz'
    peerUrl: 'https://${toLower(siteNames[1])}.azurewebsites.net'
  }
  {
    name: siteNames[1]
    storeDefault: 'cosmos'
    siteUrl: 'https://theyard-cosmos.stevenstout.biz'
    peerSite: 'https://theyard.stevenstout.biz'
    peerUrl: 'https://${toLower(siteNames[0])}.azurewebsites.net'
  }
]
// #endregion two-sites

var sqlConnection = 'Server=tcp:${sqlServer}${environment().suffixes.sqlServerHostname},1433;Initial Catalog=${sqlDatabase};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};'

// #region plan
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    // "reserved" is how the API spells Linux.
    reserved: true
  }
}
// #endregion plan

// #region sites
resource site 'Microsoft.Web/sites@2023-12-01' = [
  for s in sites: {
    name: s.name
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
      // No affinity cookie. App Service adds one to every response by default,
      // to pin a visitor to one of several instances; there is one instance, and
      // a cookie on every response is a cookie the edge has to carry and a
      // reader of the network tab has to wonder about.
      clientAffinityEnabled: false
      siteConfig: {
        linuxFxVersion: 'DOCKER|${appImage}'
        alwaysOn: true
        healthCheckPath: '/healthz'
        http20Enabled: true
        minTlsVersion: '1.2'
        ftpsState: 'Disabled'
        // The registry pull as the user-assigned identity, no admin user and no password anywhere.
        acrUseManagedIdentityCreds: true
        acrUserManagedIdentityID: identity.properties.clientId
        appSettings: [
          // The platform's three: where the container listens, that nothing is
          // mounted over /home, and how long a start may take before App
          // Service gives up on it. The process reads a hundred thousand
          // vehicles into memory before it answers, on a processor it shares.
          { name: 'WEBSITES_PORT', value: '8080' }
          { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'false' }
          { name: 'WEBSITES_CONTAINER_START_TIME_LIMIT', value: '600' }
          // The application's, name for name what the container groups carried
          // (infra/aci-theyard.yaml, infra/aci-theyard-cosmos.yaml); a test holds
          // this list to those two files.
          { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
          { name: 'ConnectionStrings__YardSql', value: sqlConnection }
          { name: 'Auth__SigningKey', value: authSigningKey }
          { name: 'Admin__Key', value: adminKey }
          { name: 'Cosmos__AccountEndpoint', value: 'https://cosmos-theyard-ss.documents.azure.com:443/' }
          { name: 'Cosmos__Database', value: 'theyard' }
          { name: 'Cosmos__Credential', value: 'managed-identity' }
          { name: 'Store__Default', value: s.storeDefault }
          // Off here, on in the container groups. Each site warms the store it
          // serves and the other warms on first use: four catalogues of a hundred
          // thousand vehicles on one 1.75 GB machine measured as paging, and two
          // measured as fitting (ADR: One plan, two sites).
          { name: 'Store__WarmOthers', value: 'false' }
          // And a store a site does not serve gives its catalogue back after ten
          // minutes of nobody asking, because the proof card loads it on demand
          // and it used to stay until the next roll.
          { name: 'Store__ReleaseIdleMinutes', value: '10' }
          { name: 'Email__Endpoint', value: 'https://acs-theyard-ss.unitedstates.communication.azure.com' }
          { name: 'Email__From', value: 'DoNotReply@ae4d5c47-28c9-49e2-8d3a-a859abd0f7df.azurecomm.net' }
          { name: 'Peer__Url', value: s.peerUrl }
          { name: 'Peer__Site', value: s.peerSite }
          { name: 'Site__Url', value: s.siteUrl }
          // The site's own ARM path, so the Admin tab's Azure card reads this
          // site and the plan it shares. An identifier, not a secret (ADR-010).
          { name: 'Azure__SelfResourceId', value: resourceId('Microsoft.Web/sites', s.name) }
        ]
      }
    }
  }
]
// #endregion sites

output planId string = plan.id
output origins array = [for (s, i) in sites: 'https://${site[i].properties.defaultHostName}']
