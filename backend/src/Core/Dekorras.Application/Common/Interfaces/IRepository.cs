using System.Linq.Expressions;
using Dekorras.Domain.Common;

namespace Dekorras.Application.Common.Interfaces;

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    IQueryable<T> Query();
    Task AddAsync(T entity, CancellationToken cancellationToken);
    void Update(T entity);
    void Remove(T entity);

    /// <summary>
    /// GetByIdAsync/Query().FirstOrDefault() ile getirilen bir entity'nin gezinme koleksiyonlarını
    /// (ör. Translations, ConfigFields, Items) açıkça yükler. Bu ÇAĞRILMADAN entity üzerinde
    /// koleksiyonun MEVCUT içeriğini okuyup karar veren bir domain metodu (ör. "bu dilde çeviri
    /// var mı, varsa güncelle") çağrılırsa, koleksiyon sessizce boş görünür ve yeni bir yinelenen
    /// satır eklenir - var olan güncellenmez. Yalnızca ekleme yapan (var olanı okumayan) domain
    /// metodları için bu yükleme gerekmez.
    /// </summary>
    Task LoadCollectionAsync<TProperty>(T entity, Expression<Func<T, IEnumerable<TProperty>>> navigationProperty, CancellationToken cancellationToken)
        where TProperty : class;
}

public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : BaseEntity;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
