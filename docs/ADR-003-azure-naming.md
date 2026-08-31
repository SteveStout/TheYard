# ADR-003: Azure resource naming

Date: 2026-08-31
Status: Accepted (standard approved by the author the same day)

## The pattern

TYPE-WORKLOAD-OWNER(-SUFFIX), uppercase. Rendered for this project:

| Resource            | Name                          |
|---------------------|-------------------------------|
| Resource group      | RG-THEYARD-SS                 |
| App Service plan    | PLAN-THEYARD-SS               |
| Web app             | APP-THEYARD-SS-(SUFFIX)       |
| Front Door profile  | FD-THEYARD-SS                 |
| Container registry  | crtheyardss(suffix)           |
| Front Door endpoint | fde-theyard-ss-(suffix)       |
| Container Apps env  | cae-theyard-ss                |
| Container App       | ca-theyard-ss-(suffix)        |

## The rules

1. Type prefixes in the Cloud Adoption Framework style: RG, PLAN, APP, CR, FD,
   FDE, CAE, CA.
2. Uppercase with hyphens wherever Azure allows it. Platform-forced exceptions
   go lowercase and are limited to two classes: the container registry
   (alphanumeric only, DNS-bound) and hostname-bearing names (Front Door
   endpoints, Container Apps resources), because DNS lowercases them anyway.
3. The workload token (THEYARD) sits second; the owner tag (SS) third.
4. A deterministic uniqueString(resourceGroup().id) suffix appears ONLY on
   names that need global uniqueness. Internal names stay clean.
5. No region and no environment token today. One environment exists, and
   region-free names are what allowed same-day region fallbacks without a
   single rename; that was exercised repeatedly on day one. When a second
   environment appears, an environment token slots in before the suffix.

## Consequences

Azure resources cannot be renamed, only recreated, so this standard applies
from creation; anything born before it gets recreated, which the one-command
teardown makes cheap. Browser-visible hostnames render lowercase regardless of
the resource name casing.