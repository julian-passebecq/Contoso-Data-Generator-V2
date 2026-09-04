/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`GivenName` AS STRING) AS given_name,
  CAST(`Surname` AS STRING) AS surname,
  CAST(`Email` AS STRING) AS email,
  CAST(`City` AS STRING) AS city,
  CAST(`CountryCode` AS STRING) AS country_code,
  CAST(`LoyaltyTier` AS STRING) AS loyalty_tier,
  CAST(`ValidFrom` AS TIMESTAMP) AS valid_from,
  CAST(`SourceEventId` AS STRING) AS source_event_id,
  CAST(`ValidTo` AS TIMESTAMP) AS valid_to,
  CAST(`IsCurrent` AS BOOL) AS is_current,
  COALESCE(CAST(`IsDeleted` AS BOOL), FALSE) AS is_deleted
FROM {{ source('silver', 'customer_scd2') }}
