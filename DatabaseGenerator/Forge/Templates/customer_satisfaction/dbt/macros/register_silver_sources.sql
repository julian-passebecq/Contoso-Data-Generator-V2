-- artifactStatus: validated
{% macro register_silver_sources() %}
  {% if execute %}
    {% set silver_root = var('silver_root') %}
    {% set silver_tables = [
      'customers',
      'customer_cdc',
      'customer_scd2',
      'products',
      'stores',
      'orders',
      'order_rows',
      'shipments',
      'shipment_events',
      'returns',
      'support_tickets',
      'reviews',
      'quality_issues'
    ] %}

    {% do run_query('create schema if not exists silver') %}
    {% for table_name in silver_tables %}
      {% set register_sql %}
        create or replace view silver."{{ table_name }}" as
        select *
        from read_parquet(
          '{{ silver_root }}/{{ table_name }}/*.parquet',
          union_by_name = true
        )
      {% endset %}
      {% do run_query(register_sql) %}
    {% endfor %}
  {% endif %}
  {{ return('') }}
{% endmacro %}

