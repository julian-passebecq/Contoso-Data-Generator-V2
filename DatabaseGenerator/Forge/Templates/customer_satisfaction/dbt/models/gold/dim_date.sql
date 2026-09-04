-- artifactStatus: validated
with event_dates as (
  select cast(order_date as date) as full_date from {{ ref('stg_orders') }}
  union
  select cast(shipped_at as date) from {{ ref('stg_shipments') }}
  union
  select cast(promised_at as date) from {{ ref('stg_shipments') }}
  union
  select cast(delivered_at as date) from {{ ref('stg_shipments') }}
  union
  select cast(event_time as date) from {{ ref('stg_shipment_events') }}
  union
  select cast(requested_at as date) from {{ ref('stg_returns') }}
  union
  select cast(opened_at as date) from {{ ref('stg_support_tickets') }}
  union
  select cast(closed_at as date) from {{ ref('stg_support_tickets') }} where closed_at is not null
  union
  select cast(reviewed_at as date) from {{ ref('stg_reviews') }}
)
select
  cast(strftime(full_date, '%Y%m%d') as integer) as date_key,
  full_date,
  cast(date_part('year', full_date) as integer) as calendar_year,
  cast(date_part('quarter', full_date) as integer) as calendar_quarter,
  cast(date_part('month', full_date) as integer) as calendar_month,
  strftime(full_date, '%B') as month_name,
  cast(date_part('day', full_date) as integer) as day_of_month,
  cast(date_part('isodow', full_date) as integer) as day_of_week,
  strftime(full_date, '%A') as day_name,
  date_part('isodow', full_date) in (6, 7) as is_weekend
from event_dates
where full_date is not null

