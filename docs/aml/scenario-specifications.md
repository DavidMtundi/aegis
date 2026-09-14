# AML Scenario Specifications

This document outlines the initial set of Anti-Money Laundering (AML) scenarios. The exact thresholds for these scenarios are currently open decisions.

## 1. High Velocity of Small Transactions (Structuring)
- **Code:** AML-001
- **Focus:** Detecting attempts to evade reporting thresholds by breaking down large sums.
- **Description:** Flags entities that conduct many transactions just below the regulatory reporting threshold within a short period.
- **Key Conditions Structure:**
  - `TransactionAmount` > `MinAmount` AND < `ReportingThreshold`
  - `TransactionCount` > `ThresholdCount` over `TimeWindow`
- **Key Exclusions:** Corporate payroll accounts.
- **Open Decisions:** DEC-STR-001, DEC-STR-002

## 2. Rapid Movement of Funds (Pass-Through)
- **Code:** AML-002
- **Focus:** Detecting accounts used merely as conduits for illicit funds.
- **Description:** Flags accounts that receive funds and quickly transfer a similar amount out to other accounts.
- **Key Conditions Structure:**
  - `IncomingAmount` ~ `OutgoingAmount` (within X% variance)
  - `TimeBetweenTransfers` < `MaxTimeWindow`
- **Key Exclusions:** Legitimate escrow accounts.
- **Open Decisions:** DEC-PASS-001

## 3. High Risk Jurisdiction Activity
- **Code:** AML-003
- **Focus:** Monitoring flows to and from jurisdictions with weak AML controls.
- **Description:** Flags significant transaction volume involving known high-risk countries.
- **Key Conditions Structure:**
  - `CounterpartyCountry` IN `HighRiskCountryList`
  - `TotalVolume` > `VolumeThreshold` over `TimeWindow`
- **Key Exclusions:** Pre-approved vendor payments.
- **Open Decisions:** DEC-JUR-001

## 4. Unexpected Crypto-Related Activity
- **Code:** AML-004
- **Focus:** Unregistered or high-risk crypto exchanges.
- **Description:** Flags significant interactions with Virtual Asset Service Providers (VASPs).
- **Key Conditions Structure:**
  - `CounterpartyCategory` == `VASP`
  - `TotalAmount` > `CryptoThreshold`
- **Key Exclusions:** Users explicitly onboarded and approved for crypto trading.
- **Open Decisions:** DEC-CRY-001

## 5. Early Account Surrender/Large Withdrawals
- **Code:** AML-005
- **Focus:** Flight risk or immediate liquidation of illicit funds.
- **Description:** Flags newly opened accounts that receive a large deposit and immediately withdraw or transfer it out.
- **Key Conditions Structure:**
  - `AccountAge` < `NewAccountWindow`
  - `WithdrawalAmount` > `PercentageOfBalance`
- **Key Exclusions:** None.
- **Open Decisions:** DEC-NEW-001

## 6. Significant Change in Behavior
- **Code:** AML-006
- **Focus:** Detecting sudden spikes in activity that deviate from historical norms.
- **Description:** Flags accounts where the current month's volume is drastically higher than the historical average.
- **Key Conditions Structure:**
  - `CurrentMonthVolume` > (`HistoricalAverageVolume` * `Multiplier`)
- **Key Exclusions:** Accounts with high expected volatility (e.g., seasonal businesses).
- **Open Decisions:** DEC-BEH-001

## 7. Dormant Account Reactivation
- **Code:** AML-007
- **Focus:** Takeover of old accounts for illicit purposes.
- **Description:** Flags accounts that have had no activity for a long period, followed by sudden large transactions.
- **Key Conditions Structure:**
  - `DaysSinceLastActivity` > `DormancyThreshold`
  - `NewTransactionAmount` > `ReactivationThreshold`
- **Key Exclusions:** None.
- **Open Decisions:** DEC-DORM-001

## 8. Round Dollar Transactions
- **Code:** AML-008
- **Focus:** Unusual human-driven transaction patterns.
- **Description:** Flags high volumes of transactions that are exactly round numbers (e.g., $10,000.00).
- **Key Conditions Structure:**
  - `TransactionAmount` modulo 100 == 0
  - `CountOfRoundTransactions` > `ThresholdCount` over `TimeWindow`
- **Key Exclusions:** Standard B2B invoice payments.
- **Open Decisions:** DEC-RND-001

## 9. Multiple Accounts Same IP/Device
- **Code:** AML-009
- **Focus:** Identity farming or coordinated syndicates.
- **Description:** Flags multiple accounts transacting from the same IP address or device fingerprint.
- **Key Conditions Structure:**
  - `DistinctAccounts` > `ThresholdCount` sharing `DeviceId` or `IpAddress`
- **Key Exclusions:** Corporate NAT IPs (requires manual whitelisting).
- **Open Decisions:** DEC-DEV-001
