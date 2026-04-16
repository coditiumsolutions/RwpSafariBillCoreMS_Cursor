# Billing Rules - Billing Management System

**System**: Billing Management System (BMS)  
**Technology**: .NET Core + SQL Server  
**Scope**: Maintenance Billing (Phase 1), Electricity Billing (future phase)  
**RulesVersion**: v1.1  
**Status**: Approved baseline for implementation and testing

## Table of Contents

1. [Purpose](#purpose)
2. [Scope](#scope)
3. [Billing Components](#billing-components)
4. [Core Formulas](#core-formulas)
5. [Order of Execution](#order-of-execution)
6. [Step-by-Step Calculation Flow](#step-by-step-calculation-flow)
7. [Rounding Rules](#rounding-rules)
8. [Validation Rules](#validation-rules)
9. [Real-World Examples](#real-world-examples)
10. [Versioning and Change Log](#versioning-and-change-log)
11. [Developer Notes](#developer-notes)

---

## Purpose

This document defines the official billing rules for bill generation in BMS.  
It is intended for:

- Business users (to review and approve charging logic)
- Developers (to implement consistent logic in code)
- QA teams (to verify expected billing outcomes)

---

## Scope

### Phase 1: Maintenance Billing

- Generate monthly maintenance bills
- Include taxes, previous balance, surcharge (for late payment), and adjustments
- Prevent invalid bill generation (duplicate/disconnected/inconsistent state)

### Future: Electricity Billing

- Electricity billing will reuse the same rule framework where applicable
- Electricity-specific formulas will be introduced in a later version

---

## Billing Components

| Component | Description | Source |
|---|---|---|
| Base Amount | Core service/maintenance charge before tax/surcharge | Customer profile/rate table |
| Tax (%) | Percentage applied to base amount | Tax configuration |
| Late Payment Surcharge | Penalty for late payment | Rule: percentage on base+tax |
| Previous Balance | Unpaid balance from prior period(s) | Previous bill history |
| Adjustments | Manual add/deduct (waiver, correction, misc charges) | Authorized manual entry |

### Adjustment Sign Convention

- Positive adjustment (`+`) increases payable amount.
- Negative adjustment (`-`) decreases payable amount.

---

## Core Formulas

### 1) Service Tax Govt Calculation

```text
TaxAmount = Cdbl(maint)
ServiceTaxGovt = Round((Cdbl(TaxAmount * 40 / 100) * 16 / 100), 0)
```

### 2) Total Bill Calculation

```text
TotalBill = maint + misc + ServiceTaxGovt
```

### 3) Surcharge Calculation

```text
Surcharge = Round(TotalBill * 10 / 100, 0)
```

### 4) BillInDueDate Calculation

```text
BillInDueDate = TotalBill
```

### 5) BillAfterDate Calculation

```text
BillAfterDate = BillInDueDate + Surcharge
```

> Current runtime source fields: `CustomersMaintenance.maint` and `CustomersMaintenance.misc`  
> Current DB mapping: `TaxAmount` is saved in `MaintenanceBills.current_gst`

---

## Order of Execution

Order is mandatory. Do not reorder these steps in implementation.

1. Validate request and customer eligibility
2. Check duplicate bill (`Customer + Month + Year`)
3. Read `maint` and `misc` from `CustomersMaintenance`
4. Calculate `ServiceTaxGovt`
5. Set `TaxAmount = ServiceTaxGovt`
6. Calculate `TotalBill = maint + misc + ServiceTaxGovt`
7. Calculate `Surcharge = TotalBill * 10%`
8. Set `BillInDueDate = TotalBill`
9. Set `BillAfterDate = BillInDueDate + Surcharge`
10. Persist bill + update bill generation status + audit log

---

## Step-by-Step Calculation Flow

1. Read input context:
   - Customer
   - Billing month/year
   - `maint` and `misc` from `CustomersMaintenance`

2. Perform validation checks:
   - Duplicate bill prevention
   - Customer connection status
   - Billing period consistency

3. Compute amounts in strict order:
   - `TaxAmount = maint`
   - `ServiceTaxGovt = Round((TaxAmount * 40 / 100) * 16 / 100, 0)`
   - `TotalBill = maint + misc + ServiceTaxGovt`
   - `Surcharge = Round(TotalBill * 10 / 100, 0)`
   - `BillInDueDate = TotalBill`
   - `BillAfterDate = BillInDueDate + Surcharge`

5. Apply rounding policy:
   - Round all computed monetary outputs to nearest integer

6. Persist bill:
   - Save values + billing metadata + rules version
   - Save reason/status for skipped/failed records

---

## Rounding Rules

To ensure consistent values across API/UI/DB:

1. All final bill amounts must be stored as integers.
2. Round to nearest integer for:
   - `ServiceTaxGovt`
   - `Surcharge`
   - Any computed monetary output that is decimal
3. Use one standard rounding behavior system-wide (`MidpointRounding.AwayFromZero` recommended).
4. Do not mix rounding methods between services.

---

## Validation Rules

### 1) Duplicate Bill Prevention

- Do not generate a second bill for the same combination:
  - `CustomerIdentifier + BillingMonth + BillingYear`
- Action:
  - Skip generation
  - Return status: `Skipped`
  - Reason: `Already generated for <Month-Year>`

### 2) Missing Previous Bill Handling

Recommended operational rule:

- If previous month bill is missing **and** customer has historical bills:
  - Skip generation
  - Reason: `Previous bill not found`
- If customer has no prior bill history (new customer):
  - Allow generation with `PreviousBalance = 0`

### 3) Disconnected Customer Handling

- If customer status is `Disconnected`:
  - Skip generation
  - Reason: `Disconnected customer`

---

## Real-World Examples

Assumptions used:

- `SurchargeRate = 10%`
- Rounding: nearest integer

### Example A: Normal Bill

- maint = 2,000
- misc = 300

Calculation:

1. TaxAmount = maint = 2,000  
2. ServiceTaxGovt = Round((2000 * 40 / 100) * 16 / 100, 0) = 128  
3. TotalBill = 2000 + 300 + 128 = 2428  
4. Surcharge = Round(2428 * 10 / 100, 0) = 243  
5. BillInDueDate = 2428  
6. BillAfterDate = 2428 + 243 = 2671

Result:

- In due date: **2,428**
- After due date: **2,671**

### Example B: Late Payment Bill (Surcharge Applied)

- maint = 3,500
- misc = 0

Calculation:

1. TaxAmount = 3500  
2. ServiceTaxGovt = Round((3500 * 40 / 100) * 16 / 100, 0) = 224  
3. TotalBill = 3500 + 0 + 224 = 3724  
4. Surcharge = Round(3724 * 10 / 100, 0) = 372  
5. BillAfterDate = 3724 + 372 = 4096

Result:

- In due date: **3,724**
- After due date: **4,096**

### Example C: Bill with Different Misc Value

- maint = 2,500
- misc = 900

Calculation:

1. TaxAmount = 2500  
2. ServiceTaxGovt = Round((2500 * 40 / 100) * 16 / 100, 0) = 160  
3. TotalBill = 2500 + 900 + 160 = 3560  
4. Surcharge = Round(3560 * 10 / 100, 0) = 356  
5. BillAfterDate = 3560 + 356 = 3916

Result:

- In due date: **3,560**
- After due date: **3,916**

### Example D: Bill with Lower Maint

- maint = 1,800
- misc = 120

Calculation:

1. TaxAmount = 1800  
2. ServiceTaxGovt = Round((1800 * 40 / 100) * 16 / 100, 0) = 115  
3. TotalBill = 1800 + 120 + 115 = 2035  
4. Surcharge = Round(2035 * 10 / 100, 0) = 204  
5. BillAfterDate = 2035 + 204 = 2239

Result:

- In due date: **2,035**
- After due date: **2,239**

---

## Versioning and Change Log

## RulesVersion Policy

- Every formula or execution-order change must update `RulesVersion`.
- `RulesVersion` must be persisted with bill/audit metadata.
- QA test suite must map expected outcomes to a specific version.

## Change Log

| Version | Date | Author | Summary |
|---|---|---|---|
| v1.1 | 2026-04-15 | Billing Team | Updated runtime formula: use `CustomersMaintenance.maint/misc`, add `ServiceTaxGovt`, set `TaxAmount = ServiceTaxGovt`, and calculate totals/surcharge from new flow |
| v1.0 | 2026-04-15 | Billing Team | Initial professional baseline for Maintenance Billing formulas, flow, and validations |

---

## Developer Notes

1. Calculations must be deterministic.
2. Same input must always produce the same output.
3. Keep one authoritative billing engine per module (avoid duplicate logic paths).
4. API/UI/Batch jobs must call the same formula methods.
5. Log rule outcomes (`Generated`, `Skipped`, `Failed`) with reason and `RulesVersion`.
6. Add automated tests for:
   - Duplicate prevention
   - Disconnected customer
   - Missing previous bill
   - Rounding behavior
   - Surcharge and adjustment scenarios

---

## Implementation Checklist

- [ ] Formulas implemented exactly as defined
- [ ] Execution order preserved
- [ ] Rounding policy enforced consistently
- [ ] Validation rules implemented
- [ ] `RulesVersion` persisted and visible in logs/reports
- [ ] Test cases mapped to all examples in this document

