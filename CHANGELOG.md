# Changelog

## Build Fixes — 2026-03-13

### Summary
Resolved all build errors to get the solution compiling successfully (9/9 projects, 0 warnings, 0 errors).

### Changes

#### 1. Target Framework Update (`Directory.Build.props`)
- Updated `TargetFramework` from `net8.0` to `net10.0` to match the installed .NET 10 SDK.

#### 2. Paynow NuGet Compatibility (`MRP.Infrastructure.csproj`)
- Added `NoWarn="NU1701"` to the `Paynow 1.2.0` package reference. This package only targets .NET Framework 4.5, causing a restore failure when `TreatWarningsAsErrors` is enabled.

#### 3. Npgsql EF Core Update (`MRP.Infrastructure.csproj`)
- Updated `Npgsql.EntityFrameworkCore.PostgreSQL` from `8.0.4` to `10.0.0-preview.3`. The old version pulled in `Microsoft.Extensions.Caching.Memory 8.0.0`, which has a known high-severity vulnerability ([GHSA-qj66-m88j-hmgj](https://github.com/advisories/GHSA-qj66-m88j-hmgj)).

#### 4. PaynowGatewayWrapper Rewrite (`MRP.Infrastructure/Paynow/PaynowGatewayWrapper.cs`)
The wrapper was written against an assumed Paynow SDK API that didn't match the actual `Paynow 1.2.0` package. Fixed to use the real API:

| Issue | Before (broken) | After (fixed) |
|---|---|---|
| Namespace conflict | `new Paynow(...)` collided with `MRP.Infrastructure.Paynow` namespace | Fully qualified as `new Webdev.Payments.Paynow(...)` |
| Constructor signature | `Paynow(string, string)` | `Paynow(string integrationId, string integrationKey, string resultUrl)` |
| Poll method | Sync `PollTransaction()` wrapped in `Task.Run` | Native `PollTransactionAsync()` |
| Send method | Sync `Send()` wrapped in `Task.Run` | Native `SendAsync()` |
| Mobile send | `SendMobile()` (doesn't exist) | `SendMobileAsync()` |
| StatusResponse.Status | `response.Status` (doesn't exist) | `response.WasPaid` (bool) mapped to `TransactionStatus` |
| StatusResponse.PaidOn | `response.PaidOn` (doesn't exist) | Derived from `WasPaid` flag |
| StatusResponse.PaynowReference | `response.PaynowReference` (doesn't exist) | Falls back to `response.Reference` |
| InitResponse.Instructions | `response.Instructions()` (doesn't exist) | Set to `null` |

#### 5. Missing Blazor Imports (`MRP.Dashboard/_Imports.razor`)
- Created `_Imports.razor` with `@using Microsoft.AspNetCore.Components.Routing` and `@using Microsoft.AspNetCore.Components.Web` to resolve `NavLink` component errors in `MainLayout.razor`.

#### 6. Missing Hosting Package (`MRP.Agents.csproj`)
- Added `Microsoft.Extensions.Hosting.Abstractions 10.0.0-preview.3.25171.5` to resolve `BackgroundService` base class not found in `AgentBase.cs`.
