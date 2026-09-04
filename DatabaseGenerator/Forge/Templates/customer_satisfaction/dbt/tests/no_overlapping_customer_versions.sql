-- artifactStatus: validated
select
  left_version.customer_key,
  left_version.customer_sk as left_customer_sk,
  right_version.customer_sk as right_customer_sk
from {{ ref('dim_customer') }} left_version
join {{ ref('dim_customer') }} right_version
  on left_version.customer_key = right_version.customer_key
 and left_version.customer_sk < right_version.customer_sk
 and left_version.valid_from < right_version.valid_to
 and right_version.valid_from < left_version.valid_to

