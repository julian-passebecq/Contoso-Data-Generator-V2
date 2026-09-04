-- artifactStatus: validated
select
  cast("ReviewKey" as bigint) as review_key,
  cast("OrderKey" as bigint) as order_key,
  cast("CustomerKey" as integer) as customer_key,
  cast("ProductKey" as integer) as product_key,
  cast("ReviewedAt" as timestamp) as reviewed_at,
  cast("Rating" as integer) as rating,
  cast("ReviewText" as varchar) as review_text,
  cast("VerifiedPurchase" as boolean) as verified_purchase
from {{ source('silver', 'reviews') }}

