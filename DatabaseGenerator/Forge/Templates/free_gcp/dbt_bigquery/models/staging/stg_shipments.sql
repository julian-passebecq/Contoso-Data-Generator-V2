/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`ShipmentKey` AS INT64) AS shipment_key,
  CAST(`OrderKey` AS INT64) AS order_key,
  CAST(`Carrier` AS STRING) AS carrier,
  CAST(`TrackingNumber` AS STRING) AS tracking_number,
  CAST(`ShippedAt` AS TIMESTAMP) AS shipped_at,
  CAST(`PromisedAt` AS TIMESTAMP) AS promised_at,
  CAST(`DeliveredAt` AS TIMESTAMP) AS delivered_at,
  CAST(`ShipmentStatus` AS STRING) AS shipment_status
FROM {{ source('silver', 'shipments') }}
