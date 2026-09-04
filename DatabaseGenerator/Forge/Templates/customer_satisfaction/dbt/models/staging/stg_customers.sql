-- artifactStatus: validated
select
  cast("CustomerKey" as integer) as customer_key,
  cast("GivenName" as varchar) as given_name,
  cast("Surname" as varchar) as surname,
  cast("Email" as varchar) as email,
  cast("City" as varchar) as city,
  cast("CountryCode" as varchar) as country_code,
  cast("LoyaltyTier" as varchar) as loyalty_tier,
  cast("ValidFrom" as timestamp) as valid_from
from {{ source('silver', 'customers') }}

