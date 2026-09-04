-- artifactStatus: validated
select
  cast("StoreKey" as integer) as store_key,
  cast("StoreName" as varchar) as store_name,
  cast("Channel" as varchar) as channel,
  cast("CountryCode" as varchar) as country_code
from {{ source('silver', 'stores') }}

