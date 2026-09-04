-- artifactStatus: validated
with raw_evidence as (
  select unnest(evidence) as evidence
  from read_json_auto('{{ var("truth_manifest_path") }}')
),
evidence as (
  select
    json_extract_string(to_json(evidence), '$.evidenceId') as evidence_id,
    json_extract_string(to_json(evidence), '$.recordKeys[0]') as first_key,
    json_extract_string(to_json(evidence), '$.recordKeys[1]') as second_key,
    json_extract_string(to_json(evidence), '$.details.customerKey') as customer_key,
    json_extract_string(to_json(evidence), '$.details.operation') as operation,
    json_extract_string(to_json(evidence), '$.details.ingestionLagHours') as ingestion_lag_hours
  from raw_evidence
),
failed_checks as (
  select 'cdc_insert_exactly_once' as check_name
  where (
    select count(*)
    from {{ ref('stg_customer_cdc') }} c
    join evidence e on e.evidence_id = 'EV-CDC-I' and c.event_id = e.first_key
    where c.operation = e.operation
  ) <> 1

  union all

  select 'cdc_update_deduplicated_exactly_once'
  where (
    select count(*)
    from {{ ref('stg_customer_cdc') }} c
    join evidence e on e.evidence_id = 'EV-DUP-CDC' and c.event_id = e.first_key
    where c.operation = 'U'
  ) <> 1

  union all

  select 'cdc_delete_exactly_once'
  where (
    select count(*)
    from {{ ref('stg_customer_cdc') }} c
    join evidence e on e.evidence_id = 'EV-CDC-D' and c.event_id = e.first_key
    where c.operation = e.operation
  ) <> 1

  union all

  select 'shipment_event_duplicate_removed'
  where (
    select count(*)
    from {{ ref('stg_shipment_events') }} s
    join evidence e
      on e.evidence_id = 'EV-DUP-SHIPMENT-EVENT'
     and s.shipment_event_key = cast(e.first_key as bigint)
  ) <> 1

  union all

  select 'late_arrival_flag_and_lag_preserved'
  where (
    select count(*)
    from {{ ref('stg_shipment_events') }} s
    join evidence e
      on e.evidence_id = 'EV-LATE-ARRIVAL'
     and s.shipment_event_key = cast(e.first_key as bigint)
    where s.is_late_arrival
      and abs(s.ingestion_lag_hours - cast(e.ingestion_lag_hours as double)) < 0.000001
  ) <> 1

  union all

  select 'scd2_prior_version_closed_at_update'
  where (
    select count(*)
    from {{ ref('stg_customer_scd2') }} prior
    join {{ ref('stg_customer_scd2') }} updated
      on prior.customer_key = updated.customer_key
     and prior.valid_to = updated.valid_from
    join evidence e
      on e.evidence_id = 'EV-SCD2'
     and updated.customer_key = cast(e.first_key as integer)
     and updated.source_event_id = e.second_key
    where prior.source_event_id = 'BASE-' || e.first_key
      and not prior.is_current
      and updated.is_current
      and not updated.is_deleted
      and updated.city = 'Basel'
      and updated.loyalty_tier = 'Platinum'
  ) <> 1

  union all

  select 'cdc_deleted_customer_closed_and_marked'
  where (
    select count(*)
    from {{ ref('stg_customer_scd2') }} d
    join evidence e
      on e.evidence_id = 'EV-CDC-D'
     and d.customer_key = cast(e.customer_key as integer)
    where d.is_deleted
      and not d.is_current
      and d.valid_to is not null
  ) <> 1
)
select * from failed_checks
