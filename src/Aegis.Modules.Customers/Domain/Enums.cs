namespace Aegis.Modules.Customers.Domain;

public enum CustomerType
{
    INDIVIDUAL = 1,
    BUSINESS = 2
}

public enum CustomerStatus
{
    ACTIVE = 1,
    INACTIVE = 2,
    CLOSED = 3
}

public enum AccountType
{
    BANK_ACCOUNT = 1,
    MOBILE_WALLET = 2,
    LOAN_ACCOUNT = 3,
    SACCO_ACCOUNT = 4,
    MERCHANT_ACCOUNT = 5,
    OTHER = 6
}

public enum AccountStatus
{
    ACTIVE = 1,
    CLOSED = 2,
    FROZEN = 3
}
