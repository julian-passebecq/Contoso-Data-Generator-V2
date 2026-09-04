-- artifactStatus: starter/reference
-- project: __PROJECT_NAME__
-- scenario: __SCENARIO__
-- Dialect: DuckDB SQL. Spark owns Raw/Bronze/Silver; dbt is the executable Gold implementation.
-- This standalone reference shows the same Silver contract and KPI definitions for exploration.

create schema if not exists silver;
create schema if not exists gold_reference;

create or replace view silver.customers as
select * from read_parquet('/workspace/lake/silver/customers/*.parquet', union_by_name = true);

create or replace view silver.customer_scd2 as
select * from read_parquet('/workspace/lake/silver/customer_scd2/*.parquet', union_by_name = true);

create or replace view silver.products as
select * from read_parquet('/workspace/lake/silver/products/*.parquet', union_by_name = true);

create or replace view silver.stores as
select * from read_parquet('/workspace/lake/silver/stores/*.parquet', union_by_name = true);

create or replace view silver.orders as
select * from read_parquet('/workspace/lake/silver/orders/*.parquet', union_by_name = true);

create or replace view silver.order_rows as
select * from read_parquet('/workspace/lake/silver/order_rows/*.parquet', union_by_name = true);

create or replace view silver.shipments as
select * from read_parquet('/workspace/lake/silver/shipments/*.parquet', union_by_name = true);

create or replace view silver.shipment_events as
select * from read_parquet('/workspace/lake/silver/shipment_events/*.parquet', union_by_name = true);

create or replace view silver.returns as
select * from read_parquet('/workspace/lake/silver/returns/*.parquet', union_by_name = true);

create or replace view silver.support_tickets as
select * from read_parquet('/workspace/lake/silver/support_tickets/*.parquet', union_by_name = true);

create or replace view silver.reviews as
select * from read_parquet('/workspace/lake/silver/reviews/*.parquet', union_by_name = true);

create or replace view silver.quality_issues as
select * from read_parquet('/workspace/lake/silver/quality_issues/*.parquet', union_by_name = true);

create or replace view gold_reference.dim_customer as
select
  md5(cast("CustomerKey" as varchar) || '|' || cast("ValidFrom" as varchar)) as customer_sk,
  cast("CustomerKey" as integer) as customer_key,
  cast("GivenName" as varchar) as given_name,
  cast("Surname" as varchar) as surname,
  cast("Email" as varchar) as email,
  cast("City" as varchar) as city,
  cast("CountryCode" as varchar) as country_code,
  cast("LoyaltyTier" as varchar) as loyalty_tier,
  cast("ValidFrom" as timestamp) as valid_from,
  coalesce(cast("ValidTo" as timestamp), timestamp '9999-12-31 00:00:00') as valid_to,
  cast("IsCurrent" as boolean) as is_current,
  cast("IsDeleted" as boolean) as is_deleted
from silver.customer_scd2;

create or replace view gold_reference.fact_sales as
select
  cast(o."OrderKey" as bigint) as order_key,
  cast(r."LineNumber" as integer) as line_number,
  customer.customer_sk,
  cast(o."StoreKey" as integer) as store_key,
  cast(r."ProductKey" as integer) as product_key,
  cast(r."Quantity" as integer) as quantity,
  cast(r."NetPrice" as decimal(18, 2)) as net_price,
  cast(r."NetPrice" * r."Quantity" as decimal(20, 2)) as sales_amount,
  cast(r."UnitCost" * r."Quantity" as decimal(20, 2)) as cost_amount
from silver.orders o
join silver.order_rows r on r."OrderKey" = o."OrderKey"
join gold_reference.dim_customer customer
  on customer.customer_key = o."CustomerKey"
 and o."OrderDate" >= customer.valid_from
 and o."OrderDate" < customer.valid_to;

create or replace view gold_reference.fact_shipment as
select
  cast(s."ShipmentKey" as bigint) as shipment_key,
  cast(s."OrderKey" as bigint) as order_key,
  cast(s."DeliveredAt" as timestamp) <= cast(s."PromisedAt" as timestamp) as is_on_time,
  date_diff('second', cast(s."PromisedAt" as timestamp), cast(s."DeliveredAt" as timestamp)) / 3600.0 as delivery_delay_hours
from silver.shipments s;

create or replace view gold_reference.kpi_customer_satisfaction as
select
  (select count(*) from silver.orders) as order_count,
  (select cast(sum(sales_amount) as decimal(38, 2)) from gold_reference.fact_sales) as gross_sales_amount,
  (
    select round(cast(sum(case when is_on_time then 1 else 0 end) as double) / nullif(count(*), 0), 6)
    from gold_reference.fact_shipment
  ) as on_time_delivery_rate,
  (
    select round(cast((select count(*) from silver.returns) as double) / nullif((select count(*) from silver.orders), 0), 6)
  ) as return_rate,
  (select round(avg(cast("Rating" as integer)), 6) from silver.reviews) as average_review_rating;

-- The result must be empty. It uses the same tolerance as the generated dbt singular test.
with expected as (
  select
    cast("expectedKpis"."order_count" as double) as order_count,
    cast("expectedKpis"."gross_sales_amount" as double) as gross_sales_amount,
    cast("expectedKpis"."on_time_delivery_rate" as double) as on_time_delivery_rate,
    cast("expectedKpis"."return_rate" as double) as return_rate,
    cast("expectedKpis"."average_review_rating" as double) as average_review_rating
  from read_json_auto('/workspace/out/truth_manifest.json')
),
actual as (
  select * from gold_reference.kpi_customer_satisfaction
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
where expected_value is null
   or actual_value is null
   or abs(expected_value - actual_value) > 0.000001;

-- Generated sample contract: the default project expects __EXPECTED_ORDER_COUNT__ orders.

