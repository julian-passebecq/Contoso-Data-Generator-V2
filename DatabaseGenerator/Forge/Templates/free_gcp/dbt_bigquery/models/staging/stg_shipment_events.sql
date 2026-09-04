/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`ShipmentEventKey` AS INT64) AS shipment_event_key,
  CAST(`ShipmentKey` AS INT64) AS shipment_key,
  CAST(`EventType` AS STRING) AS event_type,
  CAST(`EventTime` AS TIMESTAMP) AS event_time,
  CAST(`IngestedAt` AS TIMESTAMP) AS ingested_at,
  CAST(`Location` AS STRING) AS location,
  CAST(`IngestionLagHours` AS FLOAT64) AS ingestion_lag_hours,
  CAST(`IsLateArrival` AS BOOL) AS is_late_arrival
FROM {{ source('silver', 'shipment_events') }}
