-- artifactStatus: validated
select
  cast("Entity" as varchar) as entity,
  cast("RecordKey" as varchar) as record_key,
  cast("Rule" as varchar) as rule,
  cast("BadValue" as varchar) as bad_value,
  cast("EvidenceId" as varchar) as evidence_id
from {{ source('silver', 'quality_issues') }}

