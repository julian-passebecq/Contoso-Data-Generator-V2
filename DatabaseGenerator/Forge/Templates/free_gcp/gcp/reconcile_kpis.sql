-- artifact-status: generated-reference
-- GoogleSQL adaptation of the validated V1 customer-satisfaction fact grains.
-- Tokens are replaced only after identifier validation in bigquery_runtime.py.
WITH dim_customer AS (
  SELECT CustomerKey, ValidFrom,
    COALESCE(ValidTo, TIMESTAMP '9999-12-31 00:00:00+00') AS ValidTo
  FROM `{{dataset}}.{{prefix}}customer_scd2`
), fact_sales AS (
  SELECT o.OrderKey, CAST(r.NetPrice * r.Quantity AS NUMERIC) AS sales_amount
  FROM `{{dataset}}.{{prefix}}orders` o
  JOIN `{{dataset}}.{{prefix}}order_rows` r ON r.OrderKey = o.OrderKey
  JOIN dim_customer c ON c.CustomerKey = o.CustomerKey
    AND o.OrderDate >= c.ValidFrom AND o.OrderDate < c.ValidTo
), fact_shipment AS (
  SELECT s.ShipmentKey, s.DeliveredAt <= s.PromisedAt AS is_on_time
  FROM `{{dataset}}.{{prefix}}shipments` s
  JOIN `{{dataset}}.{{prefix}}orders` o ON o.OrderKey = s.OrderKey
  JOIN dim_customer c ON c.CustomerKey = o.CustomerKey
    AND o.OrderDate >= c.ValidFrom AND o.OrderDate < c.ValidTo
), fact_return AS (
  SELECT r.ReturnKey
  FROM `{{dataset}}.{{prefix}}returns` r
  JOIN `{{dataset}}.{{prefix}}orders` o ON o.OrderKey = r.OrderKey
  JOIN dim_customer c ON c.CustomerKey = r.CustomerKey
    AND r.RequestedAt >= c.ValidFrom AND r.RequestedAt < c.ValidTo
), fact_customer_experience AS (
  SELECT o.OrderKey
  FROM `{{dataset}}.{{prefix}}orders` o
  JOIN dim_customer c ON c.CustomerKey = o.CustomerKey
    AND o.OrderDate >= c.ValidFrom AND o.OrderDate < c.ValidTo
)
SELECT
  (SELECT COUNT(*) FROM fact_customer_experience) AS order_count,
  (SELECT ROUND(SUM(sales_amount), 2) FROM fact_sales) AS gross_sales_amount,
  (SELECT ROUND(SAFE_DIVIDE(COUNTIF(is_on_time), COUNT(*)), 6) FROM fact_shipment) AS on_time_delivery_rate,
  ROUND(SAFE_DIVIDE((SELECT COUNT(*) FROM fact_return),
    (SELECT COUNT(*) FROM fact_customer_experience)), 6) AS return_rate,
  (SELECT ROUND(AVG(CAST(Rating AS NUMERIC)), 6) FROM `{{dataset}}.{{prefix}}reviews`) AS average_review_rating
