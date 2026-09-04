-- artifactStatus: validated
select
  store_key,
  store_name,
  channel,
  country_code
from {{ ref('stg_stores') }}

