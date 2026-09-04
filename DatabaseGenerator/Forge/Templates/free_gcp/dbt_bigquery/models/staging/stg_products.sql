/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`ProductKey` AS INT64) AS product_key,
  CAST(`ProductName` AS STRING) AS product_name,
  CAST(`Category` AS STRING) AS category,
  CAST(`Brand` AS STRING) AS brand,
  CAST(`UnitPrice` AS NUMERIC) AS unit_price,
  CAST(`UnitCost` AS NUMERIC) AS unit_cost
FROM {{ source('silver', 'products') }}
