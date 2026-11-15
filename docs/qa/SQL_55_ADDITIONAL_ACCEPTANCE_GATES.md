# SQL acceptance coverage gap expansion

The `sqlserver-migration.yml` disposable SQL Server 2022 gate now executes
**55 additional SQL integration test classes**, previously omitted from its
regular exact-SHA CI smoke. They are divided into **five independently run
TRX-producing gates**: V1 Fee/Student/Refund; V2 Library/LMS; V2 Guardian
Privacy/Portals; V2 Transport Trip Dispatch/Concurrency; V3 Inventory Physical
Count/Multi-Store/Procurement/Asset accounting.

All tests run against **EduOS_MigrationSmoke** on loopback-only Developer SQL;
no real production data or tenant may be connected. Tests previously included
in the V1/V2/V3 base gate remain intact. Every group runs even when a different
group fails, provided migrations succeeded, preserving exact failure evidence.

This is a substantial increase in real database acceptance **but not a live
institutional UAT or complete per-page CRUD**. Tests that internally opt out
because they require additional environment flags must be distinguished from
actually exercised tests in TRX, and capacity 40K/AP loads remain separate
explicitly flagged rehearsals.
