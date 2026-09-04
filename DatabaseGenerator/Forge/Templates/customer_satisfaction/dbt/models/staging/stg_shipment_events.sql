-- artifactStatus: validated
select
  cast("ShipmentEventKey" as bigint) as shipment_event_key,
  cast("ShipmentKey" as bigint) as shipment_key,
  cast("EventType" as varchar) as event_type,
  cast("EventTime" as timestamp) as event_time,
  cast("IngestedAt" as timestamp) as ingested_at,
  cast("Location" as varchar) as location,
  cast("IngestionLagHours" as double) as ingestion_lag_hours,
  cast("IsLateArrival" as boolean) as is_late_arrival
from {{ source('silver', 'shipment_events') }}

