-- artifactStatus: validated
select
  md5(cast(o.order_key as varchar) || '|' || cast(r.line_number as varchar)) as sales_key,
  o.order_key,
  r.line_number,
  cast(strftime(o.order_date, '%Y%m%d') as integer) as order_date_key,
  c.customer_sk,
  r.product_key,
  o.store_key,
  o.currency_code,
  o.order_status,
  r.quantity,
  r.unit_price,
  r.net_price,
  r.unit_cost,
  cast(r.net_price * r.quantity as decimal(20, 2)) as sales_amount,
  cast(r.unit_cost * r.quantity as decimal(20, 2)) as cost_amount,
  cast((r.net_price - r.unit_cost) * r.quantity as decimal(20, 2)) as margin_amount
from {{ ref('stg_orders') }} o
join {{ ref('stg_order_rows') }} r
  on r.order_key = o.order_key
join {{ ref('dim_customer') }} c
  on c.customer_key = o.customer_key
 and o.order_date >= c.valid_from
 and o.order_date < c.valid_to

