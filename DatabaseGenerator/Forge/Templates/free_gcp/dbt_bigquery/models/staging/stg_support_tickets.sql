/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  CAST(`TicketKey` AS INT64) AS ticket_key,
  CAST(`OrderKey` AS INT64) AS order_key,
  CAST(`CustomerKey` AS INT64) AS customer_key,
  CAST(`OpenedAt` AS TIMESTAMP) AS opened_at,
  CAST(`ClosedAt` AS TIMESTAMP) AS closed_at,
  CAST(`Channel` AS STRING) AS channel,
  CAST(`Topic` AS STRING) AS topic,
  CAST(`Priority` AS STRING) AS priority,
  CAST(`SatisfactionScore` AS INT64) AS satisfaction_score
FROM {{ source('silver', 'support_tickets') }}
