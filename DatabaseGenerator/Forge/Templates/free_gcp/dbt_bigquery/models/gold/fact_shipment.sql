/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
WITH event_aggregate AS (
  SELECT
    shipment_key,
    COUNT(*) AS shipment_event_count,
    SUM(CASE WHEN is_late_arrival THEN 1 ELSE 0 END) AS late_arrival_event_count
  FROM {{ ref('stg_shipment_events') }}
  GROUP BY
    shipment_key
)
SELECT
  s.shipment_key,
  s.order_key,
  CAST(FORMAT_DATE('%Y%m%d', DATE(s.shipped_at)) AS INT64) AS shipped_date_key,
  CAST(FORMAT_DATE('%Y%m%d', DATE(s.promised_at)) AS INT64) AS promised_date_key,
  CAST(FORMAT_DATE('%Y%m%d', DATE(s.delivered_at)) AS INT64) AS delivered_date_key,
  c.customer_sk,
  o.store_key,
  carrier.carrier_key,
  s.tracking_number,
  s.shipment_status,
  s.shipped_at,
  s.promised_at,
  s.delivered_at,
  TIMESTAMP_DIFF(s.delivered_at, s.shipped_at, SECOND) / 3600.0 AS transit_hours,
  TIMESTAMP_DIFF(s.delivered_at, s.promised_at, SECOND) / 3600.0 AS delivery_delay_hours,
  s.delivered_at <= s.promised_at AS is_on_time,
  CASE WHEN s.delivered_at <= s.promised_at THEN 1 ELSE 0 END AS is_on_time_int,
  COALESCE(events.shipment_event_count, 0) AS shipment_event_count,
  COALESCE(events.late_arrival_event_count, 0) AS late_arrival_event_count
FROM {{ ref('stg_shipments') }} AS s
JOIN {{ ref('stg_orders') }} AS o
  ON o.order_key = s.order_key
JOIN {{ ref('dim_customer') }} AS c
  ON c.customer_key = o.customer_key
  AND o.order_date >= c.valid_from
  AND o.order_date < c.valid_to
JOIN {{ ref('dim_carrier') }} AS carrier
  ON carrier.carrier_name = s.carrier
LEFT JOIN event_aggregate AS events
  ON events.shipment_key = s.shipment_key
