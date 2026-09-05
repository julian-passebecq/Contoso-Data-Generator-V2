-- Business/label logic belongs in dbt. Consumers use this Gold feature mart.
with base as (
  select f.order_key, f.order_date, f.delivered_at as prediction_time,
    f.delivered_at + interval 14 day as label_timestamp,
    f.sales_amount, f.item_quantity, s.channel as store_channel, s.country_code,
    c.loyalty_tier as customer_loyalty_tier_as_of_order,
    epoch(f.promised_at - f.shipped_at) / 3600.0 as promised_transit_hours,
    epoch(f.delivered_at - f.shipped_at) / 3600.0 as actual_transit_hours,
    f.delivery_delay_hours, cast(f.is_on_time as integer) as is_on_time,
    (select count(*) from {{ ref('stg_shipment_events') }} e
       join {{ ref('stg_shipments') }} sh on sh.shipment_key=e.shipment_key
       where sh.order_key=f.order_key and e.event_time<=f.delivered_at and e.ingested_at<=f.delivered_at
    ) as shipment_event_count_at_delivery,
    case when exists (select 1 from {{ ref('stg_reviews') }} r
         where r.order_key=f.order_key and r.rating<=2 and r.reviewed_at>f.delivered_at
           and r.reviewed_at<=f.delivered_at+interval 14 day)
      or exists (select 1 from {{ ref('stg_support_tickets') }} t
         where t.order_key=f.order_key and t.satisfaction_score<=2 and t.closed_at>f.delivered_at
           and t.closed_at<=f.delivered_at+interval 14 day)
      then 1 else 0 end as is_dissatisfied_14d
  from {{ ref('fact_customer_experience') }} f
  join {{ ref('dim_store') }} s on s.store_key=f.store_key
  join {{ ref('dim_customer') }} c on c.customer_sk=f.customer_sk
  where f.delivered_at is not null
    and (c.source_event_id like 'BASE-%' or exists (
      select 1 from {{ ref('stg_customer_cdc') }} cd
      where cd.event_id=c.source_event_id and cd.event_time<=f.order_date and cd.ingested_at<=f.order_date))
)
select * from base
-- Explicit snapshot cutoff passed by runtime; immature rows never become negative training labels.
where label_timestamp <= cast('{{ env_var("FORGE_LABEL_AS_OF", "2025-02-01T00:00:00Z") }}' as timestamp)
