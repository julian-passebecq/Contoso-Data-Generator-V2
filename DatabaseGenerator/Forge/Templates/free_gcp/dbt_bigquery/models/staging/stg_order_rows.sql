/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`OrderKey` AS INT64) AS order_key,
  CAST(`LineNumber` AS INT64) AS line_number,
  CAST(`ProductKey` AS INT64) AS product_key,
  CAST(`Quantity` AS INT64) AS quantity,
  CAST(`UnitPrice` AS NUMERIC) AS unit_price,
  CAST(`NetPrice` AS NUMERIC) AS net_price,
  CAST(`UnitCost` AS NUMERIC) AS unit_cost
FROM {{ source('silver', 'order_rows') }}
