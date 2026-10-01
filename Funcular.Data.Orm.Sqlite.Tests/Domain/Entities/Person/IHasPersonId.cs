namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person
{
    /// <summary>
    /// Implemented by the person entities so tests can query through an interface-typed source
    /// (<c>IQueryable&lt;IHasPersonId&gt;</c>). Exposes only <see cref="Id"/>: over an interface source a member
    /// resolves to its property name, which matches a column only for <c>Id</c>.
    /// </summary>
    public interface IHasPersonId
    {
        int Id { get; }
    }
}
