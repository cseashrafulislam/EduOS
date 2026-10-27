# Institutional manual journal reversal approval

The Finance journal workspace uses the existing canonical `Journal` and `JournalLine` aggregates, not SaaS billing. A posted manual journal is immutable.

A reversal request now creates a **Submitted** compensating journal with an idempotency key, the original journal ID, and exact debit/credit inversions. This is **not** a ledger posting. A different TenantAdmin/Principal must approve it using the existing review endpoint; an approved reversal can then be posted through the existing post endpoint, within an open accounting period. The service revalidates source provenance, exact line inversion, tenant, BDT, and duplicate active reversals inside the transaction before approval and posting. Rejected proposals remain auditable and can be replaced with a new request key. Exact-key retries return the existing proposal.

Previously posted reversals remain untouched. The original journal is never modified. No Hostel/Transport reservation change is authorized by this workflow: issues #45, #47, #48 and #51 still require Platform-owned durable reservation/invoice provenance and a separate Finance adjustment approval contract.

Platform follow-up: a tenant-scoped filtered unique constraint for active reversal lineage, if supported by canonical state mapping, would strengthen concurrent SQL Server protection; shared DbContext and migrations remain Platform-owned.
