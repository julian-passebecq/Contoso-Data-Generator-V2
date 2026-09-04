-- artifactStatus: validated
with expected as (
  select
    cast("expectedKpis"."order_count" as double) as order_count,
    cast("expectedKpis"."gross_sales_amount" as double) as gross_sales_amount,
    cast("expectedKpis"."on_time_delivery_rate" as double) as on_time_delivery_rate,
    cast("expectedKpis"."return_rate" as double) as return_rate,
    cast("expectedKpis"."average_review_rating" as double) as average_review_rating
  from read_json_auto('{{ var("truth_manifest_path") }}')
),
actual as (
  select
    cast(order_count as double) as order_count,
    cast(gross_sales_amount as double) as gross_sales_amount,
    cast(on_time_delivery_rate as double) as on_time_delivery_rate,
    cast(return_rate as double) as return_rate,
    cast(average_review_rating as double) as average_review_rating
  from {{ ref('kpi_customer_satisfaction') }}
),
comparisons as (
  select 'order_count' as metric, expected.order_count as expected_value, actual.order_count as actual_value from expected cross join actual
  union all
  select 'gross_sales_amount', expected.gross_sales_amount, actual.gross_sales_amount from expected cross join actual
  union all
  select 'on_time_delivery_rate', expected.on_time_delivery_rate, actual.on_time_delivery_rate from expected cross join actual
  union all
  select 'return_rate', expected.return_rate, actual.return_rate from expected cross join actual
  union all
  select 'average_review_rating', expected.average_review_rating, actual.average_review_rating from expected cross join actual
)
select *
from comparisons
where actual_value is null
   or expected_value is null
   or abs(actual_value - expected_value) > 0.000001

