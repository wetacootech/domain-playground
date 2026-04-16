namespace WeTacoo.Domain.Commercial.ValueObjects;
using WeTacoo.Domain.Common;
public record Personal(string FirstName, string LastName, string Email, string Phone) : ValueObject;
public record Address(string ZipCode, string? AreaId = null) : ValueObject;
public record PaymentCondition(decimal VatRate, int PaymentDays) : ValueObject;
public record CustomerData(bool IsCustomer, string? CustomerId = null) : ValueObject;
