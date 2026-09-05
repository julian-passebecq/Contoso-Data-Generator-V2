-- Presentation-ready trend mart. Report code never repeats these business aggregations.
select cast(order_date as date) as order_day, store_key,
  count(*) as order_count, cast(sum(sales_amount) as decimal(38,2)) as sales_amount,
  avg(average_support_satisfaction) as average_support_satisfaction,
  sum(support_ticket_count) as support_ticket_count,
  avg(average_review_rating) as average_order_review_rating,
  sum(return_count) as return_count
from {{ ref('fact_customer_experience') }}
group by cast(order_date as date), store_key
