/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  t.ticket_key,
  t.order_key,
  CAST(FORMAT_DATE('%Y%m%d', DATE(t.opened_at)) AS INT64) AS opened_date_key,
  CASE
    WHEN NOT t.closed_at IS NULL
    THEN CAST(FORMAT_DATE('%Y%m%d', DATE(t.closed_at)) AS INT64)
  END AS closed_date_key,
  c.customer_sk,
  o.store_key,
  t.opened_at,
  t.closed_at,
  t.channel,
  t.topic,
  t.priority,
  CASE
    WHEN NOT t.closed_at IS NULL
    THEN TIMESTAMP_DIFF(t.closed_at, t.opened_at, SECOND) / 3600.0
  END AS resolution_hours,
  t.satisfaction_score
FROM {{ ref('stg_support_tickets') }} AS t
JOIN {{ ref('stg_orders') }} AS o
  ON o.order_key = t.order_key
JOIN {{ ref('dim_customer') }} AS c
  ON c.customer_key = t.customer_key
  AND t.opened_at >= c.valid_from
  AND t.opened_at < c.valid_to
