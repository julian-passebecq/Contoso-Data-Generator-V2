/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(ROW_NUMBER() OVER (ORDER BY carrier NULLS LAST) AS INT64) AS carrier_key,
  carrier AS carrier_name
FROM (
  SELECT DISTINCT
    carrier
  FROM {{ ref('stg_shipments') }}
) AS carriers
