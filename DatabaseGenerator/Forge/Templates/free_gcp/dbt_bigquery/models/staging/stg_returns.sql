/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`ReturnKey` AS INT64) AS return_key,
  CAST(`OrderKey` AS INT64) AS order_key,
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`RequestedAt` AS TIMESTAMP) AS requested_at,
  CAST(`Reason` AS STRING) AS reason,
  CAST(`ReturnStatus` AS STRING) AS return_status,
  CAST(`RefundAmount` AS NUMERIC) AS refund_amount
FROM {{ source('silver', 'returns') }}
