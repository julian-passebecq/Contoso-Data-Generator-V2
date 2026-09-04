-- artifactStatus: validated
with event_aggregate as (
  select
    shipment_key,
    count(*) as shipment_event_count,
    sum(case when is_late_arrival then 1 else 0 end) as late_arrival_event_count
  from {{ ref('stg_shipment_events') }}
  group by shipment_key
)
select
  s.shipment_key,
  s.order_key,
  cast(strftime(s.shipped_at, '%Y%m%d') as integer) as shipped_date_key,
  cast(strftime(s.promised_at, '%Y%m%d') as integer) as promised_date_key,
  cast(strftime(s.delivered_at, '%Y%m%d') as integer) as delivered_date_key,
  c.customer_sk,
  o.store_key,
  carrier.carrier_key,
  s.tracking_number,
  s.shipment_status,
  s.shipped_at,
  s.promised_at,
  s.delivered_at,
  date_diff('second', s.shipped_at, s.delivered_at) / 3600.0 as transit_hours,
  date_diff('second', s.promised_at, s.delivered_at) / 3600.0 as delivery_delay_hours,
  s.delivered_at <= s.promised_at as is_on_time,
  case when s.delivered_at <= s.promised_at then 1 else 0 end as is_on_time_int,
  coalesce(events.shipment_event_count, 0) as shipment_event_count,
  coalesce(events.late_arrival_event_count, 0) as late_arrival_event_count
from {{ ref('stg_shipments') }} s
join {{ ref('stg_orders') }} o
  on o.order_key = s.order_key
join {{ ref('dim_customer') }} c
  on c.customer_key = o.customer_key
 and o.order_date >= c.valid_from
 and o.order_date < c.valid_to
join {{ ref('dim_carrier') }} carrier
  on carrier.carrier_name = s.carrier
left join event_aggregate events
  on events.shipment_key = s.shipment_key

