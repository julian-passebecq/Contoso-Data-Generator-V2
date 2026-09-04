-- artifactStatus: validated
select
  (select count(*) from {{ ref('fact_customer_experience') }}) as order_count,
  (select cast(sum(sales_amount) as decimal(38, 2)) from {{ ref('fact_sales') }}) as gross_sales_amount,
  (
    select round(
      cast(sum(case when is_on_time then 1 else 0 end) as double) / nullif(count(*), 0),
      6
    )
    from {{ ref('fact_shipment') }}
  ) as on_time_delivery_rate,
  (
    select round(
      cast((select count(*) from {{ ref('fact_return') }}) as double) /
      nullif((select count(*) from {{ ref('fact_customer_experience') }}), 0),
      6
    )
  ) as return_rate,
  (select round(avg(rating), 6) from {{ ref('stg_reviews') }}) as average_review_rating

