/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  TO_HEX(MD5(CAST(customer_key AS STRING) || '|' || CAST(valid_from AS STRING))) AS customer_sk,
  customer_key,
  given_name,
  surname,
  given_name || ' ' || surname AS full_name,
  email,
  city,
  country_code,
  loyalty_tier,
  valid_from,
  COALESCE(valid_to, CAST('9999-12-31 00:00:00' AS TIMESTAMP)) AS valid_to,
  is_current,
  is_deleted,
  source_event_id
FROM {{ ref('stg_customer_scd2') }}
