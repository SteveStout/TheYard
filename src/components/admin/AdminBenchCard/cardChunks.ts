/**
 * Every Admin card's code chunk, by the card's component name: the list the workbench loads a card from the
 * first time it is drawn, and the list the Admin tab fetches ahead of a click once the browser is idle
 * (src/lib/prefetch.ts). Its own file because which code each card is, is one job, and drawing it another.
 */
export const CARD_CHUNKS = {
  OverviewCard: () => import('../OverviewCard/OverviewCard'),
  HealthCard: () => import('../HealthCard/HealthCard'),
  AzureCard: () => import('../AzureCard/AzureCard'),
  PagesCard: () => import('../PagesCard/PagesCard'),
  TestsCard: () => import('../TestsCard/TestsCard'),
  TrafficSection: () => import('../TrafficCard/TrafficCard'),
  TelemetryCard: () => import('../TelemetryCard/TelemetryCard'),
  TimingCard: () => import('../TimingCard/TimingCard'),
  BackendsCard: () => import('../BackendsCard/BackendsCard'),
  ProofCard: () => import('../ProofCard/ProofCard'),
  SpendCard: () => import('../SpendCard/SpendCard'),
  MachinesCard: () => import('../MachinesCard/MachinesCard'),
  FollowsCard: () => import('../FollowsCard/FollowsCard'),
  PlanCard: () => import('../PlanCard/PlanCard'),
  CloudResourceCard: () => import('../CloudResourceCard/CloudResourceCard'),
  ExperimentCard: () => import('../ExperimentCard/ExperimentCard'),
  SqlCard: () => import('../SqlCard/SqlCard'),
  StoreCard: () => import('../StoreCard/StoreCard'),
  IncidentsCard: () => import('../IncidentsCard/IncidentsCard'),
  ForemanCard: () => import('../ForemanCard/ForemanCard'),
  ErrorsCard: () => import('../ErrorsCard/ErrorsCard'),
  LogCard: () => import('../LogCard/LogCard'),
  KeptLogsCard: () => import('../KeptLogsCard/KeptLogsCard'),
  ActivityCard: () => import('../ActivityCard/ActivityCard'),
  LinksCard: () => import('../LinksCard/LinksCard'),
  ReviewCard: () => import('../ReviewCard/ReviewCard'),
  BlogCard: () => import('../BlogCard/BlogCard'),
  OperatorCard: () => import('../OperatorCard/OperatorCard'),
  ResetLinkCard: () => import('../ResetLinkCard/ResetLinkCard'),
};
