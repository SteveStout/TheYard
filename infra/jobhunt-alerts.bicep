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

@description('The address the alert emails; passed at deploy time, never committed.')
param alertEmail string

var tags = { owner: 'SS', lane: 'jobhunt-cloud-native' }

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
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
// #endregion jobhunt-alerts

output actionGroupId string = alertGroup.id
output alertRuleId string = deadFunction.id
