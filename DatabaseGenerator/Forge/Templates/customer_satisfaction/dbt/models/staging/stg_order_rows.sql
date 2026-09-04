-- artifactStatus: validated
select
  cast("OrderKey" as bigint) as order_key,
  cast("LineNumber" as integer) as line_number,
  cast("ProductKey" as integer) as product_key,
  cast("Quantity" as integer) as quantity,
  cast("UnitPrice" as decimal(18, 2)) as unit_price,
  cast("NetPrice" as decimal(18, 2)) as net_price,
  cast("UnitCost" as decimal(18, 2)) as unit_cost
from {{ source('silver', 'order_rows') }}

