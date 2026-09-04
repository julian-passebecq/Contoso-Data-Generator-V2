/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
WITH sales AS (
  SELECT
    order_key,
    SUM(sales_amount) AS sales_amount,
    SUM(quantity) AS item_quantity
  FROM {{ ref('fact_sales') }}
  GROUP BY
    order_key
), shipment AS (
  SELECT
    order_key,
    MAX(shipped_at) AS shipped_at,
    MAX(promised_at) AS promised_at,
    MAX(delivered_at) AS delivered_at,
    MAX(delivery_delay_hours) AS delivery_delay_hours,
    LOGICAL_AND(is_on_time) AS is_on_time,
    SUM(late_arrival_event_count) AS late_arrival_event_count
  FROM {{ ref('fact_shipment') }}
  GROUP BY
    order_key
), returns AS (
  SELECT
    order_key,
    COUNT(*) AS return_count,
    SUM(refund_amount) AS refund_amount
  FROM {{ ref('fact_return') }}
  GROUP BY
    order_key
), support AS (
  SELECT
    order_key,
    COUNT(*) AS support_ticket_count,
    AVG(satisfaction_score) AS average_support_satisfaction
  FROM {{ ref('fact_support') }}
  GROUP BY
    order_key
), reviews AS (
  SELECT
    order_key,
    COUNT(*) AS review_count,
    AVG(rating) AS average_review_rating
  FROM {{ ref('stg_reviews') }}
  GROUP BY
    order_key
)
SELECT
  o.order_key,
  CAST(FORMAT_DATE('%Y%m%d', DATE(o.order_date)) AS INT64) AS order_date_key,
  c.customer_sk,
  o.customer_key,
  o.store_key,
  o.order_date,
  o.currency_code,
  o.order_status,
  COALESCE(sales.sales_amount, 0) AS sales_amount,
  COALESCE(sales.item_quantity, 0) AS item_quantity,
  shipment.shipped_at,
  shipment.promised_at,
  shipment.delivered_at,
  shipment.delivery_delay_hours,
  shipment.is_on_time,
  COALESCE(shipment.late_arrival_event_count, 0) AS late_arrival_event_count,
  COALESCE(returns.return_count, 0) > 0 AS returned_flag,
  COALESCE(returns.return_count, 0) AS return_count,
  COALESCE(returns.refund_amount, 0) AS refund_amount,
  COALESCE(support.support_ticket_count, 0) AS support_ticket_count,
  support.average_support_satisfaction,
  COALESCE(reviews.review_count, 0) AS review_count,
  reviews.average_review_rating,
  CASE
    WHEN reviews.average_review_rating <= 2 OR support.average_support_satisfaction <= 2
    THEN 'Dissatisfied'
    WHEN reviews.review_count IS NULL AND support.support_ticket_count IS NULL
    THEN 'Unobserved'
    WHEN reviews.average_review_rating >= 4 OR support.average_support_satisfaction >= 4
    THEN 'Satisfied'
    ELSE 'Neutral'
  END AS satisfaction_outcome
FROM {{ ref('stg_orders') }} AS o
JOIN {{ ref('dim_customer') }} AS c
  ON c.customer_key = o.customer_key
  AND o.order_date >= c.valid_from
  AND o.order_date < c.valid_to
LEFT JOIN sales
  ON sales.order_key = o.order_key
LEFT JOIN shipment
  ON shipment.order_key = o.order_key
LEFT JOIN returns
  ON returns.order_key = o.order_key
LEFT JOIN support
  ON support.order_key = o.order_key
LEFT JOIN reviews
  ON reviews.order_key = o.order_key
