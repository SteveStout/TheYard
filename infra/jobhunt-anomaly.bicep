// Cost Management's anomaly alert on the subscription (ADR: What Azure charges, the addendum on the spike guard):
// Azure's own model reads each day's cost against the days before it and emails when one stands out, the rule
// the cost card applies by hand, kept as a second pair of eyes. Subscription scope, like the budget, so its own
// template: az deployment sub create --location centralus. The address is a parameter, never committed.
targetScope = 'subscription'

@description('The address the anomaly alert emails; passed at deploy time, never committed.')
param alertEmail string

@description('The first day the alert runs, an ISO date.')
param startDate string = '2026-11-08T00:00:00Z'

@description('The last day it runs: Cost Management asks for an end, so it is set a year out.')
param endDate string = '2027-11-08T00:00:00Z'

// #region jobhunt-anomaly
resource anomaly 'Microsoft.CostManagement/scheduledActions@2023-11-01' = {
  name: 'anomaly-subscription-daily'
  kind: 'InsightAlert'
  properties: {
    displayName: 'A day cost more than the days before it'
    status: 'Enabled'
    viewId: subscriptionResourceId('Microsoft.CostManagement/views', 'ms:DailyAnomalyByResourceGroup')
    scope: subscription().id
    notificationEmail: alertEmail
    notification: {
      to: [
        alertEmail
      ]
      subject: 'A cost anomaly on the subscription'
    }
    schedule: {
      frequency: 'Daily'
      startDate: startDate
      endDate: endDate
    }
  }
}
// #endregion jobhunt-anomaly

output anomalyId string = anomaly.id
