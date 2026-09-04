-- artifactStatus: validated
select
  cast("ShipmentKey" as bigint) as shipment_key,
  cast("OrderKey" as bigint) as order_key,
  cast("Carrier" as varchar) as carrier,
  cast("TrackingNumber" as varchar) as tracking_number,
  cast("ShippedAt" as timestamp) as shipped_at,
  cast("PromisedAt" as timestamp) as promised_at,
  cast("DeliveredAt" as timestamp) as delivered_at,
  cast("ShipmentStatus" as varchar) as shipment_status
from {{ source('silver', 'shipments') }}

