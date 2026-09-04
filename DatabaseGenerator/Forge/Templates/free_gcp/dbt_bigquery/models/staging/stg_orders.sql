/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`OrderKey` AS INT64) AS order_key,
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`StoreKey` AS INT64) AS store_key,
  CAST(`OrderDate` AS TIMESTAMP) AS order_date,
  CAST(`CurrencyCode` AS STRING) AS currency_code,
  CAST(`OrderStatus` AS STRING) AS order_status
FROM {{ source('silver', 'orders') }}
