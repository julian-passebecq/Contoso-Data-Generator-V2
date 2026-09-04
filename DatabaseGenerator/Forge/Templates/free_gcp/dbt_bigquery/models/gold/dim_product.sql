/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  product_key,
  product_name,
  category,
  brand,
  unit_price AS list_unit_price,
  unit_cost AS standard_unit_cost
FROM {{ ref('stg_products') }}
