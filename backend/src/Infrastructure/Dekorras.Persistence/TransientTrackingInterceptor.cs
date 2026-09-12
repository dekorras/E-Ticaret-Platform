using Dekorras.Domain.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Dekorras.Persistence;

/// <summary>
/// BaseEntity.Id her zaman istemci tarafında (Guid.NewGuid()) üretildiğinden, EF Core sorgudan
/// materyalize ettiği (yani ZATEN veritabanında var olan) bir entity ile uygulama kodunun
/// "new Foo(...)" ile AZ ÖNCE oluşturduğu (henüz hiç kaydedilmemiş) bir entity'yi salt anahtar
/// değerine bakarak ayırt edemez. Bu interceptor, EF Core bir entity'yi sorgudan materyalize
/// ettiği ANDA BaseEntity.IsTransient bayrağını false'a çeker - böylece
/// ApplicationDbContext.SaveChangesAsync, "bu gerçekten yeni mi?" sorusunu güvenilir şekilde
/// cevaplayabilir (bkz. oradaki yorum ve BaseEntity.IsTransient dokümantasyonu).
/// </summary>
public sealed class TransientTrackingInterceptor : IMaterializationInterceptor
{
    public object InitializedInstance(MaterializationInterceptionData materializationData, object instance)
    {
        if (instance is BaseEntity entity)
            entity.MarkPersisted();

        return instance;
    }
}
