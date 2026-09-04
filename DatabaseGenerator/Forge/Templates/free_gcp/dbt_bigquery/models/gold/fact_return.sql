/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  r.return_key,
  r.order_key,
  CAST(FORMAT_DATE('%Y%m%d', DATE(r.requested_at)) AS INT64) AS requested_date_key,
  c.customer_sk,
  o.store_key,
  r.requested_at,
  r.reason,
  r.return_status,
  r.refund_amount
FROM {{ ref('stg_returns') }} AS r
JOIN {{ ref('stg_orders') }} AS o
  ON o.order_key = r.order_key
JOIN {{ ref('dim_customer') }} AS c
  ON c.customer_key = r.customer_key
  AND r.requested_at >= c.valid_from
  AND r.requested_at < c.valid_to
