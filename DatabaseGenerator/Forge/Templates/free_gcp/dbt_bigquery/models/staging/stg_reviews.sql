/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`ReviewKey` AS INT64) AS review_key,
  CAST(`OrderKey` AS INT64) AS order_key,
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`ProductKey` AS INT64) AS product_key,
  CAST(`ReviewedAt` AS TIMESTAMP) AS reviewed_at,
  CAST(`Rating` AS INT64) AS rating,
  CAST(`ReviewText` AS STRING) AS review_text,
  CAST(`VerifiedPurchase` AS BOOL) AS verified_purchase
FROM {{ source('silver', 'reviews') }}
