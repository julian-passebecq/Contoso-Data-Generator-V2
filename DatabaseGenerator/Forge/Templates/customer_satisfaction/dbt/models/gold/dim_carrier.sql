-- artifactStatus: validated
select
  cast(row_number() over (order by carrier) as integer) as carrier_key,
  carrier as carrier_name
from (
  select distinct carrier
  from {{ ref('stg_shipments') }}
) carriers

