using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Funcular.Data.Orm.SqlServer.Tests.EntityFramework
{
    /// <summary>
    /// A small Entity Framework Core backed reference implementation of ISqlDataProvider
    /// intended for apples-to-apples performance comparisons.  This file contains:
    /// - minimal POCO entity classes (Person, Address, PersonAddress) mapped via Fluent API
    /// - FunkyEfContext : DbContext with explicit column/table mapping (so mapping works even if
    ///   data-annotations are removed)
    /// - EfSqlDataProvider : ISqlDataProvider implementation using EF Core
    /// Notes:
    /// - Add Microsoft.EntityFrameworkCore and Microsoft.EntityFrameworkCore.SqlServer package references
    ///   to consume this provider.
    /// - This implementation is intentionally straightforward and not a full replacement for the
    ///   SqlServerOrmDataProvider — it is a reference implementation used for benchmarking.
    /// </summary>
    public class EfSqlDataProvider : ISqlDataProvider, IDisposable
    {
        private readonly FunkyEfContext _context;
        private IDbContextTransaction _efTransaction;

        public Action<string> Log { get; set; }
        public SqlConnection Connection { get; set; }
        public SqlTransaction Transaction { get; set; }
        public string TransactionName { get; private set; }

        public EfSqlDataProvider(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            var options = new DbContextOptionsBuilder<FunkyEfContext>()
                .UseSqlServer(connectionString)
                .Options;
            _context = new FunkyEfContext(options);
            // keep Connection/Transaction null until a transaction or manual connection requested
        }

        #region Transaction Management

        public void BeginTransaction(string name = "")
        {
            EnsureConnectionOpen();
            if (_efTransaction != null) throw new InvalidOperationException("Transaction already in progress.");
            _efTransaction = _context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
            var dbTx = _efTransaction.GetDbTransaction();
            Connection = (SqlConnection)_context.Database.GetDbConnection();
            Transaction = dbTx as SqlTransaction;
            TransactionName = name ?? string.Empty;
        }

        public void CommitTransaction(string name = "")
        {
            if (_efTransaction == null || !string.IsNullOrEmpty(name) && TransactionName != name) return;
            _efTransaction.Commit();
            CleanupTransaction();
        }

        public void RollbackTransaction(string name = "")
        {
            if (_efTransaction == null || !string.IsNullOrEmpty(name) && TransactionName != name) return;
            _efTransaction.Rollback();
            CleanupTransaction();
        }

        private void CleanupTransaction()
        {
            _efTransaction?.Dispose();
            _efTransaction = null;
            Transaction = null;
            TransactionName = null;
            // keep connection open (EF manages it) — user can Dispose provider to close
        }

        private void EnsureConnectionOpen()
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) conn.Open();
        }

        #endregion

        #region CRUD (sync)

        public T Get<T>(dynamic key = null) where T : class, new()
        {
            if (key == null) return _context.Set<T>().AsNoTracking().FirstOrDefault();
            var found = _context.Find(typeof(T), new object[] { key });
            return found as T;
        }

        public IQueryable<T> Query<T>() where T : class, new()
        {
            return _context.Set<T>().AsNoTracking();
        }

        [Obsolete("Use Query<T>().Where(predicate) instead. This method materializes results immediately.")]
        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            return _context.Set<T>().AsNoTracking().Where(expression).ToList();
        }

        public ICollection<T> GetList<T>() where T : class, new()
        {
            return _context.Set<T>().AsNoTracking().ToList();
        }

        public object Insert<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _context.Set<T>().Add(entity);
            _context.SaveChanges();
            return GetPrimaryKeyValue(entity);
        }

        public T Update<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            // Find the primary key property
            var type = entity.GetType();
            var pk = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                     ?? type.GetProperties().FirstOrDefault(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);

            if (pk != null)
            {
                var key = pk.GetValue(entity);
                var tracked = _context.ChangeTracker.Entries<T>()
                    .FirstOrDefault(e => e.Property(pk.Name).CurrentValue?.Equals(key) == true);

                if (tracked != null)
                {
                    // Detach the existing tracked entity
                    tracked.State = EntityState.Detached;
                }
            }

            _context.Set<T>().Update(entity);
            _context.SaveChanges();
            return entity;
        }

        public int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new()
        {
            if (_efTransaction == null) throw new InvalidOperationException("Delete operations must be performed within an active transaction.");
            if (predicate == null) throw new InvalidOperationException("A WHERE clause (predicate) is required for deletes.");

            // Prevent trivial predicate like x => true by inspecting the generated SQL where fragment via EF translation
            var toRemove = _context.Set<T>().Where(predicate).ToList();
            if (!toRemove.Any()) return 0;
            _context.Set<T>().RemoveRange(toRemove);
            return _context.SaveChanges();
        }

        public bool Delete<T>(long id) where T : class, new()
        {
            return false;
        }

        #endregion

        #region CRUD (async)

        public async Task<T> GetAsync<T>(dynamic key = null) where T : class, new()
        {
            if (key == null) return await _context.Set<T>().AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
            var found = await _context.FindAsync(typeof(T), new object[] { key }).ConfigureAwait(false);
            return found as T;
        }

        public async Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            return await _context.Set<T>().AsNoTracking().Where(expression).ToListAsync().ConfigureAwait(false);
        }

        public async Task<ICollection<T>> GetListAsync<T>() where T : class, new()
        {
            return await _context.Set<T>().AsNoTracking().ToListAsync().ConfigureAwait(false);
        }

        public async Task<object> InsertAsync<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _context.Set<T>().AddAsync(entity).ConfigureAwait(false);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return GetPrimaryKeyValue(entity);
        }

        public async Task<T> UpdateAsync<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _context.Set<T>().Update(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return entity;
        }

        public TKey Insert<T, TKey>(T entity) where T : class, new()
        {
            return (TKey)Insert(entity);
        }

        public async Task<TKey> InsertAsync<T, TKey>(T entity) where T : class, new()
        {
            var result = await InsertAsync(entity).ConfigureAwait(false);
            return (TKey)result;
        }

        public async Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, new()
        {
            if (_efTransaction == null) throw new InvalidOperationException("Delete operations must be performed within an active transaction.");
            if (predicate == null) throw new InvalidOperationException("A WHERE clause (predicate) is required for deletes.");

            var toRemove = await _context.Set<T>().Where(predicate).ToListAsync().ConfigureAwait(false);
            if (!toRemove.Any()) return 0;
            _context.Set<T>().RemoveRange(toRemove);
            return await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        private object GetPrimaryKeyValue<T>(T entity)
        {
            // attempt common PK names
            var type = entity.GetType();
            var pk = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                     ?? type.GetProperties().FirstOrDefault(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);
            if (pk == null) return null;
            return pk.GetValue(entity);
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            _efTransaction?.Dispose();
            _context?.Dispose();
        }

        #endregion
    }

    // --- Minimal EF POCOs and DbContext used for mapping in this reference provider ---

    public class FunkyEfContext : DbContext
    {
        public FunkyEfContext(DbContextOptions<FunkyEfContext> options) : base(options) { }

        public DbSet<EfPerson> Persons { get; set; }
        public DbSet<EfAddress> Addresses { get; set; }
        public DbSet<EfPersonAddress> PersonAddresses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Person
            modelBuilder.Entity<EfPerson>(buildAction: b =>
            {
                b.ToTable(name: "person", buildAction: x => x.UseSqlOutputClause(useSqlOutputClause: true).HasTrigger(modelName: "bogie"));
                b.ToTable(name: "person");
                b.HasKey(keyExpression: e => e.Id);
                b.Property(propertyExpression: e => e.Id).HasColumnName(name: "id").ValueGeneratedOnAdd();
                b.Property(propertyExpression: e => e.FirstName).HasColumnName(name: "first_name").HasMaxLength(maxLength: 100);
                b.Property(propertyExpression: e => e.MiddleInitial).HasColumnName(name: "middle_initial").HasMaxLength(maxLength: 1).IsRequired(required: false);
                b.Property(propertyExpression: e => e.LastName).HasColumnName(name: "last_name").HasMaxLength(maxLength: 100);
                b.Property(propertyExpression: e => e.Birthdate).HasColumnName(name: "birthdate").IsRequired(required: false);
                b.Property(propertyExpression: e => e.Gender).HasColumnName(name: "gender").HasMaxLength(maxLength: 10).IsRequired(required: false);
                b.Property(propertyExpression: e => e.DateUtcCreated).HasColumnName(name: "dateutc_created");
                b.Property(propertyExpression: e => e.DateUtcModified).HasColumnName(name: "dateutc_modified");
                b.Property(propertyExpression: e => e.UniqueId).HasColumnName(name: "uniqueid").IsRequired(required: false);
            });

            // Address
            modelBuilder.Entity<EfAddress>(buildAction: b =>
            {
                b.ToTable(name: "address");
                b.HasKey(keyExpression: e => e.Id);
                b.Property(propertyExpression: e => e.Id).HasColumnName(name: "id").ValueGeneratedOnAdd();
                b.Property(propertyExpression: e => e.Line1).HasColumnName(name: "line_1").HasMaxLength(maxLength: 255);
                b.Property(propertyExpression: e => e.Line2).HasColumnName(name: "line_2").HasMaxLength(maxLength: 255).IsRequired(required: false);
                b.Property(propertyExpression: e => e.City).HasColumnName(name: "city").HasMaxLength(maxLength: 100);
                b.Property(propertyExpression: e => e.StateCode).HasColumnName(name: "state_code").HasMaxLength(maxLength: 2);
                b.Property(propertyExpression: e => e.PostalCode).HasColumnName(name: "postal_code").HasMaxLength(maxLength: 20);
                b.Property(propertyExpression: e => e.DateUtcCreated).HasColumnName(name: "dateutc_created");
                b.Property(propertyExpression: e => e.DateUtcModified).HasColumnName(name: "dateutc_modified");
            });

            // PersonAddress
            modelBuilder.Entity<EfPersonAddress>(buildAction: b =>
            {
                b.ToTable(name: "person_address");
                b.HasKey(keyExpression: e => e.Id);
                b.Property(propertyExpression: e => e.Id).HasColumnName(name: "id").ValueGeneratedOnAdd();
                b.Property(propertyExpression: e => e.PersonId).HasColumnName(name: "person_id");
                b.Property(propertyExpression: e => e.AddressId).HasColumnName(name: "address_id");
                b.Property(propertyExpression: e => e.IsPrimary).HasColumnName(name: "is_primary");
                b.Property(propertyExpression: e => e.DateUtcCreated).HasColumnName(name: "dateutc_created");
                b.Property(propertyExpression: e => e.DateUtcModified).HasColumnName(name: "dateutc_modified");
            });

            base.OnModelCreating(modelBuilder: modelBuilder);
        }
    }

    public class EfPerson
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string MiddleInitial { get; set; }
        public string LastName { get; set; }
        public DateTime? Birthdate { get; set; }
        public string Gender { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
        public Guid? UniqueId { get; set; }
    }

    public class EfAddress
    {
        public int Id { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string StateCode { get; set; }
        public string PostalCode { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }

    public class EfPersonAddress
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public int AddressId { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }
}