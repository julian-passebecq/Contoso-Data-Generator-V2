-- artifactStatus: validated
select
  cast("TicketKey" as bigint) as ticket_key,
  cast("OrderKey" as bigint) as order_key,
  cast("CustomerKey" as integer) as customer_key,
  cast("OpenedAt" as timestamp) as opened_at,
  cast("ClosedAt" as timestamp) as closed_at,
  cast("Channel" as varchar) as channel,
  cast("Topic" as varchar) as topic,
  cast("Priority" as varchar) as priority,
  cast("SatisfactionScore" as integer) as satisfaction_score
from {{ source('silver', 'support_tickets') }}

