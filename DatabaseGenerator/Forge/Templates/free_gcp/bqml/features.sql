-- One completed order; prediction time is delivery. Label maturity is explicit.
-- Post-delivery reviews/support are labels only. Ingestion timestamps are enforced where available.
WITH base AS (
  SELECT f.order_key, f.order_date, f.delivered_at AS prediction_time,
    f.sales_amount, f.item_quantity, s.channel AS store_channel, s.country_code,
    c.loyalty_tier AS customer_loyalty_tier_as_of_order,
    TIMESTAMP_DIFF(f.promised_at, f.shipped_at, SECOND) / 3600.0 AS promised_transit_hours,
    TIMESTAMP_DIFF(f.delivered_at, f.shipped_at, SECOND) / 3600.0 AS actual_transit_hours,
    f.delivery_delay_hours, f.is_on_time,
    (SELECT COUNT(*) FROM `{{dataset}}.{{prefix}}stg_shipment_events` e
      JOIN `{{dataset}}.{{prefix}}stg_shipments` sh ON sh.shipment_key=e.shipment_key
      WHERE sh.order_key=f.order_key AND e.event_time <= f.delivered_at AND e.ingested_at <= f.delivered_at
    ) AS shipment_event_count_at_delivery,
    CASE WHEN EXISTS (SELECT 1 FROM `{{dataset}}.{{prefix}}stg_reviews` r
         WHERE r.order_key=f.order_key AND r.rating <= 2 AND r.reviewed_at > f.delivered_at
           AND r.reviewed_at <= TIMESTAMP_ADD(f.delivered_at, INTERVAL 14 DAY))
      OR EXISTS (SELECT 1 FROM `{{dataset}}.{{prefix}}stg_support_tickets` t
         WHERE t.order_key=f.order_key AND t.satisfaction_score <= 2 AND t.closed_at > f.delivered_at
           AND t.closed_at <= TIMESTAMP_ADD(f.delivered_at, INTERVAL 14 DAY))
      THEN 1 ELSE 0 END AS is_dissatisfied_14d
  FROM `{{dataset}}.{{prefix}}fact_customer_experience` f
  JOIN `{{dataset}}.{{prefix}}dim_store` s ON s.store_key=f.store_key
  JOIN `{{dataset}}.{{prefix}}dim_customer` c ON c.customer_sk=f.customer_sk
  WHERE f.delivered_at IS NOT NULL
    AND TIMESTAMP_ADD(f.delivered_at, INTERVAL 14 DAY) <= TIMESTAMP('{{label_as_of}}')
    AND (c.source_event_id LIKE 'BASE-%' OR EXISTS (
      SELECT 1 FROM `{{dataset}}.{{prefix}}stg_customer_cdc` cd
      WHERE cd.event_id=c.source_event_id AND cd.event_time <= f.order_date AND cd.ingested_at <= f.order_date))
), ranked AS (
  SELECT *, ROW_NUMBER() OVER (ORDER BY order_date, order_key) AS sequence_number,
    COUNT(*) OVER () AS total_rows FROM base
), split AS (
  SELECT * EXCEPT(sequence_number, total_rows),
    CASE WHEN sequence_number <= FLOOR(total_rows * 0.70) THEN 'train'
         WHEN sequence_number <= FLOOR(total_rows * 0.85) THEN 'validation' ELSE 'test' END AS split_name
  FROM ranked
), boundaries AS (
  SELECT MIN(IF(split_name='validation', prediction_time, NULL)) AS validation_start,
         MIN(IF(split_name='test', prediction_time, NULL)) AS test_start FROM split
)
SELECT split.* FROM split CROSS JOIN boundaries
WHERE (split_name='train' AND TIMESTAMP_ADD(prediction_time, INTERVAL 14 DAY) < validation_start)
   OR (split_name='validation' AND TIMESTAMP_ADD(prediction_time, INTERVAL 14 DAY) < test_start)
   OR split_name='test'
