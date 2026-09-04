-- artifactStatus: validated
select
  order_count as actual_order_count,
  cast('{{ var("expected_order_count") }}' as bigint) as expected_order_count
from {{ ref('kpi_customer_satisfaction') }}
where order_count <> cast('{{ var("expected_order_count") }}' as bigint)

