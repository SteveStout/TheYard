// The subscription's monthly budget: $50, emailing at 80 % and 100 % of the actual spend and at 100 % of the
// forecast, through the job hunt's action group (infra/jobhunt-alerts.bicep) and to the address itself.
// Subscription scope, so its own template: az deployment sub create --location centralus. The address is a
// parameter passed at deploy time, never committed.
targetScope = 'subscription'

@description('What the subscription may cost in a month, in the billing currency.')
param amount int = 50

@description('The first day the budget counts from, the first of a month.')
param startDate string = '2026-10-01'

@description('The action group that emails (ag-jobhunt-ss in RG-JOBHUNT-SS).')
param actionGroupId string

@description('The address the budget emails; passed at deploy time, never committed.')
param alertEmail string

var notice = {
  enabled: true
  operator: 'GreaterThanOrEqualTo'
  contactEmails: [
    alertEmail
  ]
  contactGroups: [
    actionGroupId
  ]
}

// #region jobhunt-budget
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-subscription-monthly'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      actual80: union(notice, { threshold: 80, thresholdType: 'Actual' })
      actual100: union(notice, { threshold: 100, thresholdType: 'Actual' })
      forecast100: union(notice, { threshold: 100, thresholdType: 'Forecasted' })
    }
  }
}
// #endregion jobhunt-budget

output budgetId string = budget.id
