# Microsoft Fabric exporter handoff

Artifact status: **starter/reference**

Project: `__PROJECT_NAME__`  
Scenario: `__SCENARIO__`

This directory reserves the generated Fabric handoff. The source, Gold, KPI and
semantic contracts in `../models/` are the authoritative inputs for a later
Lakehouse/Warehouse and semantic-model exporter. The local V1 acceptance path
does not create a Fabric workspace, upload data, or require Azure credentials.

A future exporter should translate the neutral `../pipeline/pipeline.json`, map
the canonical `raw/bronze/silver/gold` zones without changing their business
logic, and record which Fabric artifacts were actually validated.

