{% macro generate_alias_name(custom_alias_name=none, node=none) -%}
  {{ return(env_var('FORGE_BQ_PREFIX') ~ (custom_alias_name if custom_alias_name else node.name)) }}
{%- endmacro %}
