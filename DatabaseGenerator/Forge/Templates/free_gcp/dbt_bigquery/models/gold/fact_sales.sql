/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  TO_HEX(MD5(CAST(o.order_key AS STRING) || '|' || CAST(r.line_number AS STRING))) AS sales_key,
  o.order_key,
  r.line_number,
  CAST(FORMAT_DATE('%Y%m%d', DATE(o.order_date)) AS INT64) AS order_date_key,
  c.customer_sk,
  r.product_key,
  o.store_key,
  o.currency_code,
  o.order_status,
  r.quantity,
  r.unit_price,
  r.net_price,
  r.unit_cost,
  CAST(r.net_price * r.quantity AS NUMERIC) AS sales_amount,
  CAST(r.unit_cost * r.quantity AS NUMERIC) AS cost_amount,
  CAST((
    r.net_price - r.unit_cost
  ) * r.quantity AS NUMERIC) AS margin_amount
FROM {{ ref('stg_orders') }} AS o
JOIN {{ ref('stg_order_rows') }} AS r
  ON r.order_key = o.order_key
JOIN {{ ref('dim_customer') }} AS c
  ON c.customer_key = o.customer_key
  AND o.order_date >= c.valid_from
  AND o.order_date < c.valid_to
