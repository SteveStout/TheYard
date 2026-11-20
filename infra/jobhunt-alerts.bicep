// A dead Function sends an email (1.0.4.42). Its own template so a
// deploy of it touches these resources and nothing else: a deploy of infra/jobhunt.bicep replaces the
// Function's whole settings list, and these need none of it. Deployed into RG-JOBHUNT-SS, Incremental.
//
// One action group that emails Steve, and one log alert over appi-jobhunt-ss, evaluated every ten minutes: no
// Merger invocation in the last thirty (the merger runs every minute, so half an hour without one is a stopped
// Function, which cannot write its own NOT WORKING line). A failed or blocked run, the silent campaign and the
// silent network pass no longer mail him: the watch's W12 reads them every quarter hour and its heal ladder
// fixes them or writes one NOT WORKING line (ADR-139; the year-end plan's 7c). The share's transactions alert
// is money and stays. The address is a parameter, so it is never written in this public repository.

@description('Where the Function app and its Application Insights component live.')
param location string = 'centralus'

@description('The Function app\'s Application Insights component.')
param appInsightsName string = 'appi-jobhunt-ss'

@description('The storage account behind the job-hunt folder, billed per transaction.')
param storageName string = 'stjobhuntss'

@description('Transactions in an hour on the share above which the alert emails: about four times a quiet hour.')
param transactionsPerHour int = 20000

@description('The address the alert emails; passed at deploy time, never committed.')
param alertEmail string

var tags = { owner: 'SS', lane: 'jobhunt-cloud-native' }

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageName
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

// #region jobhunt-alerts
resource alertGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-jobhunt-ss'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'jobhunt'
    enabled: true
    emailReceivers: [
      {
        name: 'steve'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

resource deadFunction 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-jobhunt-dead-function'
  location: location
  tags: tags
  properties: {
    displayName: 'The job hunt Function stopped: no Merger run in 30 minutes'
    description: 'No Merger invocation in func-jobhunt-ss in the last 30 minutes (it runs every minute): a stopped Function, which cannot write its own NOT WORKING line.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT10M'
    windowSize: 'PT30M'
    scopes: [
      appInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: 'requests | where timestamp > ago(30m) | summarize merged = countif(name == "Merger") | where merged == 0 | project process = "Merger" | summarize failures = count() by process'
          timeAggregation: 'Total'
          metricMeasureColumn: 'failures'
          dimensions: [
            {
              name: 'process'
              operator: 'Include'
              values: [
                '*'
              ]
            }
          ]
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
// The share bills every listing, read and write. On 20 to 23 October a walker on the laptop took it from a
// dollar a day to $24.99 before anyone saw the bill, because Cost Management reports a day late. This one
// reads the transactions themselves, every fifteen minutes over the last hour, so the email comes the hour
// it starts.
resource shareTransactions 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-jobhunt-share-transactions'
  location: 'global'
  tags: tags
  properties: {
    description: 'More than ${transactionsPerHour} transactions in an hour on the job-hunt share (about four times a quiet hour): something is walking J:.'
    severity: 2
    enabled: true
    scopes: [
      fileService.id
    ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'transactions'
          metricNamespace: 'Microsoft.Storage/storageAccounts/fileServices'
          metricName: 'Transactions'
          operator: 'GreaterThan'
          threshold: transactionsPerHour
          timeAggregation: 'Total'
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
// #endregion jobhunt-alerts

output actionGroupId string = alertGroup.id
output alertRuleId string = deadFunction.id
output transactionsAlertId string = shareTransactions.id
