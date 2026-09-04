-- artifactStatus: generated-reference
select order_key, line_number, count(*) as copies
from {{ ref('stg_order_rows') }}
group by order_key, line_number
having count(*) <> 1

