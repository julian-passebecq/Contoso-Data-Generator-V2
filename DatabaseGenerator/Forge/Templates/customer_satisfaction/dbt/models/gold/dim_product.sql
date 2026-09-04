-- artifactStatus: validated
select
  product_key,
  product_name,
  category,
  brand,
  unit_price as list_unit_price,
  unit_cost as standard_unit_cost
from {{ ref('stg_products') }}

