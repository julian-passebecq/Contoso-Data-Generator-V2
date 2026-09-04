-- artifactStatus: validated
select
  cast("ProductKey" as integer) as product_key,
  cast("ProductName" as varchar) as product_name,
  cast("Category" as varchar) as category,
  cast("Brand" as varchar) as brand,
  cast("UnitPrice" as decimal(18, 2)) as unit_price,
  cast("UnitCost" as decimal(18, 2)) as unit_cost
from {{ source('silver', 'products') }}

