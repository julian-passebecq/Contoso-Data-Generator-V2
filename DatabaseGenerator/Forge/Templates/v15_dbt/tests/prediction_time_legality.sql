select order_key from {{ ref('ml_customer_dissatisfaction') }}
where prediction_time < order_date
   or label_timestamp <> prediction_time + interval 14 day
   or promised_transit_hours < 0 or actual_transit_hours < 0
