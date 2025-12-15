namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Enums
{
    [Flags]
    public enum AddressType
    {
        Unspecified = 0,
        Billing = 1,
        Shipping = 2,
        Home = 4,
        Work = 8
    }
}
