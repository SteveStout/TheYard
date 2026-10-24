// The campaign's database (ADR-118, Decision 2; the review of the email campaign, F2). Its own template so a
// deploy of it touches this one database and nothing else: a deploy of infra/jobhunt.bicep replaces the
// Function's whole settings list, and appservice.bicep is the site's. Deployed Incremental into the resource
// group that holds the server.
//
// One Basic database (5 DTU, 2 GB, local backup) on the logical server the site already runs on, which is
// Entra-only: no SQL login, no password. Basic over the free serverless offer because its price is fixed at
// about $4.90 a month and it never pauses; the free offer's allowance is 55 awake hours a month and a
// campaign that wakes it three times a day spends that by the nineteenth, then either pauses with nothing
// failing loudly or bills by the hour. The Function's identity gets a database user from its SID by script
// (ADR-039's pattern), not here: a database user is not an Azure resource.

@description('The logical server, Entra-only, in westus3 (ADR-039).')
param sqlServerName string = 'sql-theyard-ss-westus3'

@description('The server\'s region; a database lives where its server does.')
param location string = 'westus3'

@description('The campaign\'s database.')
param databaseName string = 'sqldb-jobhunt-ss'

var tags = { owner: 'SS', lane: 'campaign1025', purpose: 'the hiring-manager email campaign: Companies, Leads, EmailsSent' }

resource server 'Microsoft.Sql/servers@2023-08-01-preview' existing = {
  name: sqlServerName
}

// #region campaign-database
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}
// #endregion campaign-database

output databaseId string = database.id
