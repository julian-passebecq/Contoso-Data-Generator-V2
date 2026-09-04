/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`StoreKey` AS INT64) AS store_key,
  CAST(`StoreName` AS STRING) AS store_name,
  CAST(`Channel` AS STRING) AS channel,
  CAST(`CountryCode` AS STRING) AS country_code
FROM {{ source('silver', 'stores') }}
