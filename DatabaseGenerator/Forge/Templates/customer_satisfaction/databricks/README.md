# Databricks exporter handoff

Artifact status: **starter/reference**

Project: `__PROJECT_NAME__`  
Scenario: `__SCENARIO__`

This directory reserves notebook/job-bundle generation for a later Databricks
exporter. Databricks Free Edition may be used interactively, but it is
serverless-only and subject to changing quotas and feature limits. It is not a
V1 acceptance dependency and no workspace URL, token, OAuth session or cloud
credential is required.

Docker Spark is the V1 Spark engine. A future exporter should preserve the
source/Silver/Gold contracts and label each generated Databricks artifact with
its real validation status.

