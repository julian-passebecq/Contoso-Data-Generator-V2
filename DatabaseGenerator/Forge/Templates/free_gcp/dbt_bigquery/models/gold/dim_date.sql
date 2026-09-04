/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
WITH event_dates AS (
  SELECT
    CAST(order_date AS DATE) AS full_date
  FROM {{ ref('stg_orders') }}
  UNION DISTINCT
  SELECT
    CAST(shipped_at AS DATE)
  FROM {{ ref('stg_shipments') }}
  UNION DISTINCT
  SELECT
    CAST(promised_at AS DATE)
  FROM {{ ref('stg_shipments') }}
  UNION DISTINCT
  SELECT
    CAST(delivered_at AS DATE)
  FROM {{ ref('stg_shipments') }}
  UNION DISTINCT
  SELECT
    CAST(event_time AS DATE)
  FROM {{ ref('stg_shipment_events') }}
  UNION DISTINCT
  SELECT
    CAST(requested_at AS DATE)
  FROM {{ ref('stg_returns') }}
  UNION DISTINCT
  SELECT
    CAST(opened_at AS DATE)
  FROM {{ ref('stg_support_tickets') }}
  UNION DISTINCT
  SELECT
    CAST(closed_at AS DATE)
  FROM {{ ref('stg_support_tickets') }}
  WHERE
    NOT closed_at IS NULL
  UNION DISTINCT
  SELECT
    CAST(reviewed_at AS DATE)
  FROM {{ ref('stg_reviews') }}
)
SELECT
  CAST(FORMAT_DATE('%Y%m%d', DATE(full_date)) AS INT64) AS date_key,
  full_date,
  CAST(EXTRACT(YEAR FROM full_date) AS INT64) AS calendar_year,
  CAST(EXTRACT(QUARTER FROM full_date) AS INT64) AS calendar_quarter,
  CAST(EXTRACT(MONTH FROM full_date) AS INT64) AS calendar_month,
  FORMAT_DATE('%B', full_date) AS month_name,
  CAST(EXTRACT(DAY FROM full_date) AS INT64) AS day_of_month,
  CAST((MOD(EXTRACT(DAYOFWEEK FROM full_date) + 5, 7) + 1) AS INT64) AS day_of_week,
  FORMAT_DATE('%A', full_date) AS day_name,
  (MOD(EXTRACT(DAYOFWEEK FROM full_date) + 5, 7) + 1) IN (6, 7) AS is_weekend
FROM event_dates
WHERE
  NOT full_date IS NULL
