-- artifactStatus: validated
select
  cast("ReturnKey" as bigint) as return_key,
  cast("OrderKey" as bigint) as order_key,
  cast("CustomerKey" as integer) as customer_key,
  cast("RequestedAt" as timestamp) as requested_at,
  cast("Reason" as varchar) as reason,
  cast("ReturnStatus" as varchar) as return_status,
  cast("RefundAmount" as decimal(18, 2)) as refund_amount
from {{ source('silver', 'returns') }}

