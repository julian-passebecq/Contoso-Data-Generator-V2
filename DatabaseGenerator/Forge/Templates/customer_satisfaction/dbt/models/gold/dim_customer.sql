-- artifactStatus: validated
select
  md5(cast(customer_key as varchar) || '|' || cast(valid_from as varchar)) as customer_sk,
  customer_key,
  given_name,
  surname,
  given_name || ' ' || surname as full_name,
  email,
  city,
  country_code,
  loyalty_tier,
  valid_from,
  coalesce(valid_to, timestamp '9999-12-31 00:00:00') as valid_to,
  is_current,
  is_deleted,
  source_event_id
from {{ ref('stg_customer_scd2') }}

