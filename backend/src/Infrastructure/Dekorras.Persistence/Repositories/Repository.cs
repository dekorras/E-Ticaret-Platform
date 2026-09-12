using System.Linq.Expressions;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.Persistence.Repositories;

public class Repository<T>(ApplicationDbContext dbContext) : IRepository<T> where T : BaseEntity
{
    private readonly DbSet<T> _dbSet = dbContext.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbSet.FindAsync([id], cancellationToken);

    public IQueryable<T> Query() => _dbSet.AsQueryable();

    public async Task AddAsync(T entity, CancellationToken cancellationToken) =>
        await _dbSet.AddAsync(entity, cancellationToken);

    public void Update(T entity) => _dbSet.Update(entity);

    public void Remove(T entity) => _dbSet.Remove(entity);

    public async Task LoadCollectionAsync<TProperty>(T entity, Expression<Func<T, IEnumerable<TProperty>>> navigationProperty, CancellationToken cancellationToken)
        where TProperty : class
    {
        var entry = dbContext.Entry(entity).Collection(navigationProperty);
        if (!entry.IsLoaded)
            await entry.LoadAsync(cancellationToken);
    }
}
