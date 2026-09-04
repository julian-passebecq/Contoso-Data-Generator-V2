-- artifactStatus: validated
select
  cast("EventId" as varchar) as event_id,
  cast("Operation" as varchar) as operation,
  cast("Sequence" as integer) as sequence_number,
  cast("CustomerKey" as integer) as customer_key,
  cast("EventTime" as timestamp) as event_time,
  cast("IngestedAt" as timestamp) as ingested_at,
  cast("GivenName" as varchar) as given_name,
  cast("Surname" as varchar) as surname,
  cast("Email" as varchar) as email,
  cast("City" as varchar) as city,
  cast("CountryCode" as varchar) as country_code,
  cast("LoyaltyTier" as varchar) as loyalty_tier
from {{ source('silver', 'customer_cdc') }}

