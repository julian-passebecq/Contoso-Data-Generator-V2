/* artifactStatus: generated-reference; GoogleSQL port of validated V1 grain */
SELECT
  (
    SELECT
      COUNT(*)
    FROM {{ ref('fact_customer_experience') }}
  ) AS order_count,
  (
    SELECT
      CAST(SUM(sales_amount) AS NUMERIC)
    FROM {{ ref('fact_sales') }}
  ) AS gross_sales_amount,
  (
    SELECT
      ROUND(
        CAST(SUM(CASE WHEN is_on_time THEN 1 ELSE 0 END) AS FLOAT64) / NULLIF(COUNT(*), 0),
        6
      )
    FROM {{ ref('fact_shipment') }}
  ) AS on_time_delivery_rate,
  (
    SELECT
      ROUND(
        CAST((
          SELECT
            COUNT(*)
          FROM {{ ref('fact_return') }}
        ) AS FLOAT64) / NULLIF((
          SELECT
            COUNT(*)
          FROM {{ ref('fact_customer_experience') }}
        ), 0),
        6
      )
  ) AS return_rate,
  (
    SELECT
      ROUND(AVG(rating), 6)
    FROM {{ ref('stg_reviews') }}
  ) AS average_review_rating
