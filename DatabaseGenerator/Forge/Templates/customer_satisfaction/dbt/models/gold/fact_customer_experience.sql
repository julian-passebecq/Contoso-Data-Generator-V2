-- artifactStatus: validated
with sales as (
  select
    order_key,
    sum(sales_amount) as sales_amount,
    sum(quantity) as item_quantity
  from {{ ref('fact_sales') }}
  group by order_key
),
shipment as (
  select
    order_key,
    max(shipped_at) as shipped_at,
    max(promised_at) as promised_at,
    max(delivered_at) as delivered_at,
    max(delivery_delay_hours) as delivery_delay_hours,
    bool_and(is_on_time) as is_on_time,
    sum(late_arrival_event_count) as late_arrival_event_count
  from {{ ref('fact_shipment') }}
  group by order_key
),
returns as (
  select
    order_key,
    count(*) as return_count,
    sum(refund_amount) as refund_amount
  from {{ ref('fact_return') }}
  group by order_key
),
support as (
  select
    order_key,
    count(*) as support_ticket_count,
    avg(satisfaction_score) as average_support_satisfaction
  from {{ ref('fact_support') }}
  group by order_key
),
reviews as (
  select
    order_key,
    count(*) as review_count,
    avg(rating) as average_review_rating
  from {{ ref('stg_reviews') }}
  group by order_key
)
select
  o.order_key,
  cast(strftime(o.order_date, '%Y%m%d') as integer) as order_date_key,
  c.customer_sk,
  o.customer_key,
  o.store_key,
  o.order_date,
  o.currency_code,
  o.order_status,
  coalesce(sales.sales_amount, 0) as sales_amount,
  coalesce(sales.item_quantity, 0) as item_quantity,
  shipment.shipped_at,
  shipment.promised_at,
  shipment.delivered_at,
  shipment.delivery_delay_hours,
  shipment.is_on_time,
  coalesce(shipment.late_arrival_event_count, 0) as late_arrival_event_count,
  coalesce(returns.return_count, 0) > 0 as returned_flag,
  coalesce(returns.return_count, 0) as return_count,
  coalesce(returns.refund_amount, 0) as refund_amount,
  coalesce(support.support_ticket_count, 0) as support_ticket_count,
  support.average_support_satisfaction,
  coalesce(reviews.review_count, 0) as review_count,
  reviews.average_review_rating,
  case
    when reviews.average_review_rating <= 2 or support.average_support_satisfaction <= 2 then 'Dissatisfied'
    when reviews.review_count is null and support.support_ticket_count is null then 'Unobserved'
    when reviews.average_review_rating >= 4 or support.average_support_satisfaction >= 4 then 'Satisfied'
    else 'Neutral'
  end as satisfaction_outcome
from {{ ref('stg_orders') }} o
join {{ ref('dim_customer') }} c
  on c.customer_key = o.customer_key
 and o.order_date >= c.valid_from
 and o.order_date < c.valid_to
left join sales on sales.order_key = o.order_key
left join shipment on shipment.order_key = o.order_key
left join returns on returns.order_key = o.order_key
left join support on support.order_key = o.order_key
left join reviews on reviews.order_key = o.order_key

