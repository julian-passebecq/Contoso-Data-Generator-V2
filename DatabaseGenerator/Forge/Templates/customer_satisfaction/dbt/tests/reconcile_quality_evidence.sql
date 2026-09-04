-- artifactStatus: validated
with expected(evidence_id) as (
  values ('EV-QUALITY-NULL'), ('EV-QUALITY-RANGE')
),
actual as (
  select evidence_id, count(*) as copies
  from {{ ref('stg_quality_issues') }}
  group by evidence_id
),
missing_or_duplicated as (
  select expected.evidence_id, coalesce(actual.copies, 0) as copies
  from expected
  left join actual using (evidence_id)
  where coalesce(actual.copies, 0) <> 1
),
unexpected as (
  select actual.evidence_id, actual.copies
  from actual
  left join expected using (evidence_id)
  where expected.evidence_id is null
)
select * from missing_or_duplicated
union all
select * from unexpected

