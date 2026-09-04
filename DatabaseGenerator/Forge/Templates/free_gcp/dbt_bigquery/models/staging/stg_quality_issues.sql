/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`Entity` AS STRING) AS entity,
  CAST(`RecordKey` AS STRING) AS record_key,
  CAST(`Rule` AS STRING) AS rule,
  CAST(`BadValue` AS STRING) AS bad_value,
  CAST(`EvidenceId` AS STRING) AS evidence_id
FROM {{ source('silver', 'quality_issues') }}
