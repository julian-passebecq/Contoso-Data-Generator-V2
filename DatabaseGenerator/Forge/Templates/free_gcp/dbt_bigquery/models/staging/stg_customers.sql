/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`GivenName` AS STRING) AS given_name,
  CAST(`Surname` AS STRING) AS surname,
  CAST(`Email` AS STRING) AS email,
  CAST(`City` AS STRING) AS city,
  CAST(`CountryCode` AS STRING) AS country_code,
  CAST(`LoyaltyTier` AS STRING) AS loyalty_tier,
  CAST(`ValidFrom` AS TIMESTAMP) AS valid_from
FROM {{ source('silver', 'customers') }}
