-- artifactStatus: validated
select
  t.ticket_key,
  t.order_key,
  cast(strftime(t.opened_at, '%Y%m%d') as integer) as opened_date_key,
  case when t.closed_at is not null then cast(strftime(t.closed_at, '%Y%m%d') as integer) end as closed_date_key,
  c.customer_sk,
  o.store_key,
  t.opened_at,
  t.closed_at,
  t.channel,
  t.topic,
  t.priority,
  case when t.closed_at is not null then date_diff('second', t.opened_at, t.closed_at) / 3600.0 end as resolution_hours,
  t.satisfaction_score
from {{ ref('stg_support_tickets') }} t
join {{ ref('stg_orders') }} o
  on o.order_key = t.order_key
join {{ ref('dim_customer') }} c
  on c.customer_key = t.customer_key
 and t.opened_at >= c.valid_from
 and t.opened_at < c.valid_to

