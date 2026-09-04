/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`EventId` AS STRING) AS event_id,
  CAST(`Operation` AS STRING) AS operation,
  CAST(`Sequence` AS INT64) AS sequence_number,
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`EventTime` AS TIMESTAMP) AS event_time,
  CAST(`IngestedAt` AS TIMESTAMP) AS ingested_at,
  CAST(`GivenName` AS STRING) AS given_name,
  CAST(`Surname` AS STRING) AS surname,
  CAST(`Email` AS STRING) AS email,
  CAST(`City` AS STRING) AS city,
  CAST(`CountryCode` AS STRING) AS country_code,
  CAST(`LoyaltyTier` AS STRING) AS loyalty_tier
FROM {{ source('silver', 'customer_cdc') }}
