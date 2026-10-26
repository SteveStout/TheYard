// One plan, one web app for every .NET process (ADR: One web app for every .NET
// process). What runs: a Linux B2 App Service plan and one web app for containers
// on it, which serves the SQL site, the Cosmos DB site and The Shed from one
// process, by the name each request arrives on; the rendering service's web app
// on the same plan is render.bicep.
//
// Until 19 October this file described two web apps, one per site, and The Shed
// had a third of its own (samples/theshed/infra/site.bicep, retired). They were
// the design for load and separation: one process per site and per product, so
// one could roll or fail without the others. Traffic is low and the plan's memory
// is the scarce thing, and every app on a plan costs its own share of it outside
// the app itself, so the three became one. The split is this file and one edge
// rule away when load returns: put a second entry back in `sites` with its own
// Hosts values, and point that domain's edge rule at it.
//
// Deployed in incremental mode, always. This resource group also holds the
// databases, the document store, the registry and the identity, and none of
// them is in this file; a complete-mode deployment would delete every one.
// The template is the description. Deletion is a separate, named act.

@description('Region for the plan and the web app. westus3 because westus2 refuses App Service on this subscription (measured 2026-08-31 and again 2026-09-20), and because the relational server is already there.')
param location string = 'westus3'

@description('Workload token, as in main.bicep (ADR-003)')
param baseName string = 'theyard'

@description('Owner tag, as in main.bicep (ADR-003)')
param ownerTag string = 'SS'

// #region plan-size
@description('Plan size. B2 since 8 October: 2 vCPU and 3.5 GB shared by both sites and the rendering service, after the plan read 91 per cent of B1\'s memory at the median before a third container (ADR: A rendering service beside the API). B1 is half of both.')
@allowed([
  'B1'
  'B2'
  'B3'
])
param skuName string = 'B2'
// #endregion plan-size

@description('The image the web app runs, registry/name:tag. Every roll sets this on the web app; a deployment of this file has to be told what is running so it does not roll anything back.')
param appImage string

@description('The user-assigned identity the web app runs as: registry pull, both stores, mail, and the reads of its own resource')
param identityName string = 'id-theyard-ss'

@description('Azure SQL logical server. A name, not a secret: the web app signs in as the identity and there is no password to hold.')
param sqlServer string = 'sql-theyard-ss-westus3'

@description('The database on that server the web app runs on (ADR: The SQL Server backend, addendum of 14 September)')
param sqlDatabase string = 'sqldb-theyard-ss-basic'

@description('The Application Insights component the web app reports to, by name: read, never created here')
param appInsightsName string = 'appi-theyard-ss'

@description('Application Insights connection string. An ingestion key, so it is passed in and never written here.')
@secure()
param appInsightsConnectionString string

@description('The session signing key, so a roll does not end every session')
@secure()
param authSigningKey string

@description('The operator key for the Admin tab keyed rows')
@secure()
param adminKey string

// #region plan-naming
var suffix = uniqueString(resourceGroup().id)
var upperTag = toUpper('${baseName}-${ownerTag}')
var planName = 'PLAN-${upperTag}'
// One web app, named for what it is: the one app every .NET process runs in.
var siteNames = [
  'APP-${toUpper(baseName)}-ONE-${toUpper(ownerTag)}-${toUpper(suffix)}'
]
// #endregion plan-naming

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

// #region telemetry-link
// The component the connection string below reports to. The portal shows a site as linked to it by a
// hidden tag on the site; a site deployed from a template with no tags loses it, and with it the
// portal's shortcut from the site to its telemetry. Nothing reads the tag at run time.
resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}
// #endregion telemetry-link

// #region sites-list
// The names the one web app answers to. The store a request gets when it names
// none follows the name it arrived on: the SQL site's name gets Azure SQL
// Database, the Cosmos DB site's gets Azure Cosmos DB, and The Shed's name gets
// The Shed (api/TheYard.Api/Shared/Instance/SiteHosts.cs). siteUrl is where links in an email
// point when a request arrived on no Yard name. One entry; a second entry here
// is how a site gets a web app of its own again.
var sites = [
  {
    name: siteNames[0]
    siteUrl: 'https://theyard.stevenstout.biz'
    hostSql: 'theyard.stevenstout.biz'
    hostCosmos: 'theyard-cosmos.stevenstout.biz'
    hostShed: 'theshed.stevenstout.biz'
  }
]
// #endregion sites-list

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
    tags: {
      'hidden-link: /app-insights-resource-id': appInsights.id
    }
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
        // The image is pulled from GitHub's registry, public, with no credential (ADR-009). The undo sets
        // this back to true with the Azure registry's tag, and the identity below is the pull it uses then.
        acrUseManagedIdentityCreds: false
        acrUserManagedIdentityID: identity.properties.clientId
        appSettings: [
          // The platform's three: where the container listens, that nothing is
          // mounted over /home, and how long a start may take before App
          // Service gives up on it. The process answers at once and loads a
          // hundred thousand vehicles beside the server, on a processor it shares.
          { name: 'WEBSITES_PORT', value: '8080' }
          { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'false' }
          { name: 'WEBSITES_CONTAINER_START_TIME_LIMIT', value: '600' }
          // The application's. AppServiceTemplateTests holds this list to the
          // settings the code reads and the deploy writes.
          { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
          { name: 'ConnectionStrings__YardSql', value: sqlConnection }
          { name: 'Auth__SigningKey', value: authSigningKey }
          { name: 'Admin__Key', value: adminKey }
          { name: 'Cosmos__AccountEndpoint', value: 'https://cosmos-theyard-ss.documents.azure.com:443/' }
          { name: 'Cosmos__Database', value: 'theyard' }
          { name: 'Cosmos__Credential', value: 'managed-identity' }
          // The sites, a list read by SiteSettings.cs: each a name and the store it serves,
          // and a guest application with none (ADR: The onion cut into vertical slices, rule 3).
          // No store default is set: a request that names no store and arrives on neither Yard
          // name gets the relational store.
          { name: 'Sites__0__Host', value: s.hostSql }
          { name: 'Sites__0__Store', value: 'sql' }
          { name: 'Sites__1__Host', value: s.hostCosmos }
          { name: 'Sites__1__Store', value: 'cosmos' }
          { name: 'Sites__2__Host', value: s.hostShed }
          // Off. The process warms the relational store at startup, so a roll is ready
          // on the first site's store first; the keep-warm loop then loads the other within
          // four minutes and keeps both loaded (ADR: Kept awake).
          { name: 'Store__WarmOthers', value: 'false' }
          // The catalogue is the store's (ADR: The store does the work): the process fills
          // each store's catalogue when that store holds less than it should, both of them,
          // as the only process that keeps them. Off everywhere a deploy has not said so,
          // which is every test host.
          { name: 'Catalogue__Keep', value: 'true' }
          // The heap may grow to 30 per cent of what the container sees, so a regression is
          // collected, and failing that refused, inside its own share instead of taking the
          // plan's 3.5 GB from the rendering service. GC settings read from the environment are
          // hexadecimal: 0x1E is thirty; a plain "30" would be forty-eight.
          { name: 'DOTNET_GCHeapHardLimitPercent', value: '0x1E' }
          // The collector told to trade a little processor for memory: at 7 of 9 it compacts the
          // large object heap once it is fragmented, which it otherwise never does (Memory, held by
          // the build, rule d). Read live before and after on the plan's dial.
          { name: 'DOTNET_GCConserveMemory', value: '7' }
          { name: 'Email__Endpoint', value: 'https://acs-theyard-ss.unitedstates.communication.azure.com' }
          { name: 'Email__From', value: 'DoNotReply@ae4d5c47-28c9-49e2-8d3a-a859abd0f7df.azurecomm.net' }
          { name: 'Site__Url', value: s.siteUrl }
          // The Shed, served by this process on its name (Composition/ShedMount.cs): where the
          // Dockerfile put its published files, and its own settings as its retired web app
          // carried them. The home is the sample tree inside the image, and writes are off on
          // the public site, so a stranger cannot empty or fill it before the next visitor.
          { name: 'Shed__ContentRoot', value: '/app/shed' }
          { name: 'Files__Home', value: '' }
          { name: 'Files__AllowWrites', value: 'false' }
          { name: 'Files__MaxUploadBytes', value: '10485760' }
          // The web app's own ARM path, so the Admin tab's Azure card reads this
          // web app and the plan it runs on. An identifier, not a secret (ADR-010).
          { name: 'Azure__SelfResourceId', value: resourceId('Microsoft.Web/sites', s.name) }
        ]
      }
    }
  }
]
// #endregion sites

output planId string = plan.id
output origins array = [for (s, i) in sites: 'https://${site[i].properties.defaultHostName}']
