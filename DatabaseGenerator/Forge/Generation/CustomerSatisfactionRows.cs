#nullable enable

using System;

namespace DatabaseGenerator.Forge.Generation;

internal sealed record CustomerRow(
    int CustomerKey,
    string GivenName,
    string Surname,
    string Email,
    string City,
    string CountryCode,
    string LoyaltyTier,
    DateTimeOffset ValidFrom);

internal sealed record ProductRow(
    int ProductKey,
    string ProductName,
    string Category,
    string Brand,
    decimal UnitPrice,
    decimal UnitCost);

internal sealed record StoreRow(
    int StoreKey,
    string StoreName,
    string Channel,
    string CountryCode);

internal sealed record OrderRow(
    long OrderKey,
    int CustomerKey,
    int StoreKey,
    DateTimeOffset OrderDate,
    string CurrencyCode,
    string OrderStatus);

internal sealed record OrderLineRow(
    long OrderKey,
    int LineNumber,
    int ProductKey,
    int Quantity,
    decimal UnitPrice,
    decimal NetPrice,
    decimal UnitCost);

internal sealed record ShipmentRow(
    long ShipmentKey,
    long OrderKey,
    string Carrier,
    string? TrackingNumber,
    DateTimeOffset ShippedAt,
    DateTimeOffset PromisedAt,
    DateTimeOffset DeliveredAt,
    string ShipmentStatus);

internal sealed record ShipmentEventRow(
    long ShipmentEventKey,
    long ShipmentKey,
    string EventType,
    DateTimeOffset EventTime,
    DateTimeOffset IngestedAt,
    string Location);

internal sealed record ReturnRow(
    long ReturnKey,
    long OrderKey,
    int CustomerKey,
    DateTimeOffset RequestedAt,
    string Reason,
    string ReturnStatus,
    decimal RefundAmount);

internal sealed record SupportTicketRow(
    long TicketKey,
    long OrderKey,
    int CustomerKey,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string Channel,
    string Topic,
    string Priority,
    int SatisfactionScore);

internal sealed record ReviewRow(
    long ReviewKey,
    long OrderKey,
    int CustomerKey,
    int ProductKey,
    DateTimeOffset ReviewedAt,
    int Rating,
    string ReviewText,
    bool VerifiedPurchase);

internal sealed record CustomerCdcRow(
    string EventId,
    string Operation,
    int Sequence,
    int CustomerKey,
    DateTimeOffset EventTime,
    DateTimeOffset IngestedAt,
    string GivenName,
    string Surname,
    string Email,
    string City,
    string CountryCode,
    string LoyaltyTier);
