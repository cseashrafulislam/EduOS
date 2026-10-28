# HR / Payroll V2: Bangladesh BDT operational controls

This implementation is institution-scoped HR/payroll, **not** platform billing.
It preserves the five independent V1 hourly workflows. It does not execute a
live bank transfer, apply an assumed national tax table, or assert that a PDF
contains a cryptographically valid electronic signature.

## 1. Effective-dated statutory withholding (manual, configurable)

1. A payroll accountant confirms the relevant tax, provident-fund and other
   obligations, exemptions and monthly BDT amounts for the institution's staff.
2. Tenant administrator adds an **active deduction** salary component with a
   stable code starting `STAT_`, e.g. `STAT_TAX`. Mark `IsTaxable=false`.
3. In each employee's effective-dated salary structure, enter **one fixed BDT
   monthly amount** for every active `STAT_` code. Enter **0.00** for a
   professionally reviewed exemption. Keep supporting evidence securely.
4. The salary structure save and payroll calculation validate that no eligible
   employee is missing an active statutory component. The run fails closed
   before producing snapshots if the withholding coverage is incomplete.
5. Review payroll employee lines, net pay, accounting mapping and GL before a
   different approver approves and posts the payroll run.

**Limits:** EduOS currently does not derive monthly tax obligations from annual
thresholds, taxable exemptions, investment rebates or evolving Bangladesh law.
The accountant supplies and signs off the fixed amounts, updates effective
dates as required and independently checks the generated payslips. Never use
the `STAT_` code as an assertion that national tax compliance is automatic.

## 2. Pending bank-transfer review worksheet

1. Record a bank-transfer payroll payment request for an employee with an
   active BDT destination bank account; the source institution bank must be
   active and scoped to the payroll campus.
2. Open **Payroll payments -> Institutional GL**, find a pending bank-transfer
   row and select **Bank review CSV**.
3. Only tenant administrators can export. The API reloads the complete pending
   register for that posted run, checks reconciled salary-payable provenance,
   employee and bank ownership, BDT amount and payment request state. Unhealthy
   or changed records fail closed. An audit event is persisted.
4. The downloaded file is a *bank-neutral review worksheet*. It contains
   decrypted employee destination account numbers, Excel-safe CSV quoting and
   `REVIEW_ONLY` on every row. Store and transmit under a restricted approved
   process. Verify against the bank's *separately approved* instruction format.
5. An actual transfer is performed outside EduOS. Importing this worksheet
   into any bank is **not supported**. The export does not change payment state;
   settlement must still be independently verified and posted to the GL by an
   authorized administrator different from the original maker.

## 3. Manually signed PDF contract evidence

1. Register the employee contract terms and obtain approval from a different
   tenant administrator, using existing effective-dated contract workflow.
2. On the approved contract row choose **Attach signed PDF** and upload a PDF
   (up to 5 MB). The existing private-document scanner must report clean with
   matching SHA-256, or upload fails closed.
3. The service binds this private asset to the contract, ensures only one active
   signed document, requires proof of the same tenant/uploader, verifies PDF
   content and checksum, registers a sensitive verification-required document
   type and records an audit trail.
4. A **different tenant administrator** opens the private reviewer preview,
   checks the signatures and identity evidence *offline*, then approves or
   rejects the PDF. Rejected evidence remains in history and may be replaced.
5. Only verified PDFs are available for normal private download. Every review
   and download is audited; missing, modified or duplicated attachments block.

**Limits:** The manual verification confirms an administrator's review of a
scanned PDF. It is not a Qualified Electronic Signature (QES), PKI signature
validation, third-party identity check or a legal determination of enforceability.

## 4. Production release gates

- Release branch CI, SQL migration/model check and regression tests green.
- Bangladesh payroll specialist validates BDT withholding decisions and
  statutory reporting obligations against current law.
- Bank operations approves its own format, four-eyes controls and independent
  payment acknowledgment/reconciliation procedure; no live bank API exists.
- Legal/HR reviews signature and retention rules for employment contracts.
- Tenant/campus isolation, private malware scanning and encryption keys checked
  in the target Windows Server environment.
- Representative multi-employee payroll UAT, account-mismatch, concurrency,
  account-key rotation, failure recovery and operator sign-off completed.

No automatic production deployment or real salary disbursement is authorized
by this document.
