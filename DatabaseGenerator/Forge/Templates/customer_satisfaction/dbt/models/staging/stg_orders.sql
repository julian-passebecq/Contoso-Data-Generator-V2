-- artifactStatus: validated
select
  cast("OrderKey" as bigint) as order_key,
  cast("CustomerKey" as integer) as customer_key,
  cast("StoreKey" as integer) as store_key,
  cast("OrderDate" as timestamp) as order_date,
  cast("CurrencyCode" as varchar) as currency_code,
  cast("OrderStatus" as varchar) as order_status
from {{ source('silver', 'orders') }}

