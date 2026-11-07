// The meters that can run away, each with an alert that emails the hour it starts (ADR: What Azure charges, the
// addendum on the spike guard). Cost Management reports a day late, so the budget learns of a runaway a day after
// it began; these read the meters themselves. Deployed into RG-JOBHUNT-SS, Incremental, beside
// infra/jobhunt-alerts.bicep, whose action group they email; the resources in RG-THEYARD-SS are named with their
// own group, since an alert rule may watch a resource in another group of the same subscription.
//
// Every line is about four times the busiest quiet hour of the week before it was set, so an ordinary day never
// emails and a walker, a loop or a flood does within the hour. Priced from Azure's retail price list: a metric
// rule is $0.10 a month for its one time series, a log rule checked hourly $0.50, so this file adds about $1.00.

@description('Where the log rule lives.')
param location string = 'centralus'

@description('Blob transactions in an hour on the job-hunt account: a quiet week peaked near 8,500.')
param blobTransactionsPerHour int = 25000

@description('Bytes out of the job-hunt account in an hour: a quiet week peaked near 540 MB.')
param blobEgressPerHour int = 2000000000

@description('Function executions in an hour: the timers and the merger peaked near 210.')
param functionExecutionsPerHour int = 1000

@description('Emails sent in an hour: the free sender stops at 100.')
param emailSendsPerHour int = 50

@description('Bytes the web plan sent in an hour: a quiet week peaked near 125 MB.')
param planBytesSentPerHour int = 1000000000

@description('Billable gigabytes the workspace took in a day: its daily cap is 0.1.')
param ingestionGbPerDay string = '0.08'

var tags = { owner: 'SS', lane: 'jobhunt-cloud-native' }

resource alertGroup 'Microsoft.Insights/actionGroups@2023-01-01' existing = {
  name: 'ag-jobhunt-ss'
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: 'stjobhuntss'
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

resource function 'Microsoft.Web/sites@2023-12-01' existing = {
  name: 'func-jobhunt-ss'
}

resource mail 'Microsoft.Communication/communicationServices@2023-04-01' existing = {
  name: 'acs-theyard-ss'
  scope: resourceGroup('RG-THEYARD-SS')
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: 'plan-theyard-ss'
  scope: resourceGroup('RG-THEYARD-SS')
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: 'log-theyard-ss'
  scope: resourceGroup('RG-THEYARD-SS')
}

// One row a meter: what it watches, the metric by Azure's name and how Azure adds it up over the window (a count
// of requests for the mail service, which offers nothing else; a total for the rest), the line, and the email's words.
var meters = [
  {
    name: 'alert-jobhunt-blob-transactions'
    scope: blobService.id
    namespace: 'Microsoft.Storage/storageAccounts/blobServices'
    metric: 'Transactions'
    aggregation: 'Total'
    threshold: blobTransactionsPerHour
    dimensions: []
    what: 'More than ${blobTransactionsPerHour} blob transactions in an hour on stjobhuntss: something is reading or writing in a loop.'
  }
  {
    name: 'alert-jobhunt-blob-egress'
    scope: blobService.id
    namespace: 'Microsoft.Storage/storageAccounts/blobServices'
    metric: 'Egress'
    aggregation: 'Total'
    threshold: blobEgressPerHour
    dimensions: []
    what: 'More than ${blobEgressPerHour} bytes out of stjobhuntss\'s blobs in an hour: something is downloading in a loop.'
  }
  {
    name: 'alert-jobhunt-function-executions'
    scope: function.id
    namespace: 'Microsoft.Web/sites'
    metric: 'OnDemandFunctionExecutionCount'
    aggregation: 'Total'
    threshold: functionExecutionsPerHour
    dimensions: []
    what: 'More than ${functionExecutionsPerHour} executions of func-jobhunt-ss in an hour: a trigger is firing on itself.'
  }
  {
    name: 'alert-theyard-email-sends'
    scope: mail.id
    namespace: 'Microsoft.Communication/communicationServices'
    metric: 'ApiRequests'
    aggregation: 'Count'
    threshold: emailSendsPerHour
    dimensions: [
      {
        name: 'Operation'
        operator: 'Include'
        values: [
          'SendMail'
        ]
      }
    ]
    what: 'More than ${emailSendsPerHour} emails sent by acs-theyard-ss in an hour: half the free sender\'s limit.'
  }
  {
    name: 'alert-theyard-bytes-sent'
    scope: plan.id
    namespace: 'Microsoft.Web/serverfarms'
    metric: 'BytesSent'
    aggregation: 'Total'
    threshold: planBytesSentPerHour
    dimensions: []
    what: 'More than ${planBytesSentPerHour} bytes sent by plan-theyard-ss in an hour: a crawler or a loop is pulling the site.'
  }
]

// #region jobhunt-meters
resource meterAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [
  for meter in meters: {
    name: meter.name
    location: 'global'
    tags: tags
    properties: {
      description: meter.what
      severity: 2
      enabled: true
      scopes: [
        meter.scope
      ]
      evaluationFrequency: 'PT15M'
      windowSize: 'PT1H'
      criteria: {
        'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
        allOf: [
          {
            criterionType: 'StaticThresholdCriterion'
            name: 'meter'
            metricNamespace: meter.namespace
            metricName: meter.metric
            dimensions: meter.dimensions
            operator: 'GreaterThan'
            threshold: meter.threshold
            timeAggregation: meter.aggregation
          }
        ]
      }
      autoMitigate: true
      actions: [
        {
          actionGroupId: alertGroup.id
        }
      ]
    }
  }
]

// The workspace takes the site's telemetry under a daily cap of 0.1 GB, and stops taking it at the cap for the
// rest of the day. Checked hourly over the day so far, the email comes before the cap closes, not after.
resource ingestion 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-theyard-ingestion'
  location: location
  tags: tags
  properties: {
    displayName: 'The site\'s telemetry is near its daily cap'
    description: 'More than ${ingestionGbPerDay} billable GB taken by log-theyard-ss in the last day, against its 0.1 GB daily cap.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    scopes: [
      workspace.id
    ]
    criteria: {
      allOf: [
        {
          query: 'Usage | where TimeGenerated > ago(1d) and IsBillable == true | summarize gb = sum(Quantity) / 1000.0 | where gb > ${ingestionGbPerDay}'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [
        alertGroup.id
      ]
    }
  }
}
// #endregion jobhunt-meters

output meterAlertNames array = [for (meter, i) in meters: meterAlerts[i].name]
output ingestionAlertId string = ingestion.id
