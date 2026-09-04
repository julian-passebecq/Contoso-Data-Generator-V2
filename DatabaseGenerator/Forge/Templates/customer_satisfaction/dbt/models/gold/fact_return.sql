-- artifactStatus: validated
select
  r.return_key,
  r.order_key,
  cast(strftime(r.requested_at, '%Y%m%d') as integer) as requested_date_key,
  c.customer_sk,
  o.store_key,
  r.requested_at,
  r.reason,
  r.return_status,
  r.refund_amount
from {{ ref('stg_returns') }} r
join {{ ref('stg_orders') }} o
  on o.order_key = r.order_key
join {{ ref('dim_customer') }} c
  on c.customer_key = r.customer_key
 and r.requested_at >= c.valid_from
 and r.requested_at < c.valid_to

