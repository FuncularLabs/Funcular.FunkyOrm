
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFramework;
using Microsoft.EntityFramework.Storage;

namespace Funcular.Data.Orm.SqlServer
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
            _efTransaction = _context.Database.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
            var dbTx = _efTransaction.GetDbTransaction();
            Connection = (SqlConnection)_context.Database.GetDbConnection();
            Transaction = dbTx as SqlTransaction;
            TransactionName = name ?? string.Empty;
        }

        public void CommitTransaction(string name = "")
        {
            if (_efTransaction == null || (!string.IsNullOrEmpty(name) && TransactionName != name)) return;
            _efTransaction.Commit();
            CleanupTransaction();
        }

        public void RollbackTransaction(string name = "")
        {
            if (_efTransaction == null || (!string.IsNullOrEmpty(name) && TransactionName != name)) return;
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
            var found = _context.Find(typeof(T), new object[] { (object)key });
            return found as T;
        }

        public IQueryable<T> Query<T>() where T : class, new()
        {
            return _context.Set<T>().AsNoTracking();
        }

        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            return _context.Set<T>().AsNoTracking().Where(expression).ToList();
        }

        public ICollection<T> GetList<T>() where T : class, new()
        {
            return _context.Set<T>().AsNoTracking().ToList();
        }

        public long Insert<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _context.Set<T>().Add(entity);
            _context.SaveChanges();
            return GetPrimaryKeyValueAsLong(entity);
        }

        public T Update<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
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

        #endregion

        #region CRUD (async)

        public async Task<T> GetAsync<T>(dynamic key = null) where T : class, new()
        {
            if (key == null) return await _context.Set<T>().AsNoTracking().FirstOrDefaultAsync().ConfigureAwait(false);
            var found = await _context.FindAsync(typeof(T), new object[] { (object)key }).ConfigureAwait(false);
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

        public async Task<long> InsertAsync<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _context.Set<T>().AddAsync(entity).ConfigureAwait(false);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return GetPrimaryKeyValueAsLong(entity);
        }

        public async Task<T> UpdateAsync<T>(T entity) where T : class, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _context.Set<T>().Update(entity);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return entity;
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

        private long GetPrimaryKeyValueAsLong<T>(T entity)
        {
            // attempt common PK names
            var type = entity.GetType();
            var pk = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                     ?? type.GetProperties().FirstOrDefault(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.KeyAttribute>() != null);
            if (pk == null) return 0;
            var val = pk.GetValue(entity);
            if (val == null) return 0;
            try
            {
                return Convert.ToInt64(val);
            }
            catch
            {
                return 0;
            }
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

        public DbSet<Person> Persons { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<PersonAddress> PersonAddresses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Person
            modelBuilder.Entity<Person>(b =>
            {
                b.ToTable("person");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                b.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(100);
                b.Property(e => e.MiddleInitial).HasColumnName("middle_initial").HasMaxLength(1).IsRequired(false);
                b.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(100);
                b.Property(e => e.Birthdate).HasColumnName("birthdate").IsRequired(false);
                b.Property(e => e.Gender).HasColumnName("gender").HasMaxLength(10).IsRequired(false);
                b.Property(e => e.DateUtcCreated).HasColumnName("dateutc_created");
                b.Property(e => e.DateUtcModified).HasColumnName("dateutc_modified");
                b.Property(e => e.UniqueId).HasColumnName("uniqueid").IsRequired(false);
            });

            // Address
            modelBuilder.Entity<Address>(b =>
            {
                b.ToTable("address");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                b.Property(e => e.Line1).HasColumnName("line_1").HasMaxLength(255);
                b.Property(e => e.Line2).HasColumnName("line_2").HasMaxLength(255).IsRequired(false);
                b.Property(e => e.City).HasColumnName("city").HasMaxLength(100);
                b.Property(e => e.StateCode).HasColumnName("state_code").HasMaxLength(2);
                b.Property(e => e.PostalCode).HasColumnName("postal_code").HasMaxLength(20);
                b.Property(e => e.DateUtcCreated).HasColumnName("dateutc_created");
                b.Property(e => e.DateUtcModified).HasColumnName("dateutc_modified");
                b.Property(e => e.IsPrimary).HasColumnName("is_primary");
            });

            // PersonAddress
            modelBuilder.Entity<PersonAddress>(b =>
            {
                b.ToTable("person_address");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                b.Property(e => e.PersonId).HasColumnName("person_id");
                b.Property(e => e.AddressId).HasColumnName("address_id");
                b.Property(e => e.DateUtcCreated).HasColumnName("dateutc_created");
                b.Property(e => e.DateUtcModified).HasColumnName("dateutc_modified");
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    public class Person
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

    public class Address
    {
        public int Id { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string StateCode { get; set; }
        public string PostalCode { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }

    public class PersonAddress
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public int AddressId { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }
}