/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  store_key,
  store_name,
  channel,
  country_code
FROM {{ ref('stg_stores') }}
