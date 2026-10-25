// A dead Function sends an email (1.0.4.42). Its own template so a
// deploy of it touches these two resources and nothing else: a deploy of infra/jobhunt.bicep replaces the
// Function's whole settings list, and these two need none of it. Deployed into RG-JOBHUNT-SS, Incremental.
//
// One action group that emails Steve, and one log alert over appi-jobhunt-ss, evaluated every ten minutes:
// any function's failed invocation in the last thirty minutes, or no Merger invocation in the last ten (the
// merger runs every minute, so ten minutes without one is a stopped Function). The address is a parameter, so
// it is never written in this public repository.

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
    displayName: 'A job hunt Function failed, or the Merger stopped'
    description: 'Any failed invocation of any function in func-jobhunt-ss in the last 30 minutes, or no Merger invocation in the last 10 (it runs every minute).'
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
          query: 'let failed = toscalar(requests | where timestamp > ago(30m) and success == false | count); let merged = toscalar(requests | where timestamp > ago(10m) and name == "Merger" | count); print failed = failed, merged = merged | where failed > 0 or merged == 0'
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
// The email campaign's timer (ADR-118; the review, F13): a timer that stops fails nothing and the Merger keeps
// running, so its silence would show only as a day with no emails. Evaluated hourly, it can fire only in the
// 15:00 UTC hour, when the 8 o'clock hour Central has passed under either clock (13:00 UTC in daylight time,
// 14:00 in standard time): no CampaignSend invocation in the three hours before is a dead campaign. One signal
// at the hourly rate, the lowest Azure prices log alerts at short of a day.
resource campaignSilent 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-jobhunt-campaign-silent'
  location: location
  tags: tags
  properties: {
    displayName: 'The email campaign did not run at 8 AM Central'
    description: 'No CampaignSend invocation in func-jobhunt-ss between 12:00 and 15:00 UTC, checked in the 15:00 UTC hour.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT1H'
    windowSize: 'PT3H'
    scopes: [
      appInsights.id
    ]
    criteria: {
      allOf: [
        {
          query: 'let runs = toscalar(requests | where timestamp > ago(3h) and name == "CampaignSend" | count); print runs = runs, hour = datetime_part("hour", now()) | where hour == 15 and runs == 0'
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
// #endregion jobhunt-alerts

output actionGroupId string = alertGroup.id
output alertRuleId string = deadFunction.id
output transactionsAlertId string = shareTransactions.id
output campaignAlertId string = campaignSilent.id
