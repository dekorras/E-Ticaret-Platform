namespace Dekorras.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Bu entity hiç kaydedilmedi mi (henüz veritabanında bir satırı yok mu)? Id'ler burada
    /// (BaseEntity'de) her zaman İSTEMCİ TARAFINDA (Guid.NewGuid() ile) üretildiğinden, EF Core'un
    /// "anahtar değeri zaten atanmış görünüyorsa muhtemelen zaten var" sezgisi, ALREADY TRACKED
    /// bir aggregate'in koleksiyonuna eklenen YENİ bir çocuğu (ör. yeni bir çeviri satırı) yanlışlıkla
    /// "Modified" sanıp var olmayan bir satırı UPDATE etmeye çalışır (DbUpdateConcurrencyException).
    /// Bu bayrak, gerçek durumu (yeni mi, değil mi) EF Core'un anahtar-tabanlı tahminine
    /// bırakmadan açıkça taşır; ApplicationDbContext.SaveChangesAsync bunu okuyup entity durumunu
    /// buna göre düzeltir - bkz. o dosyadaki yorum. Kalıcı değildir (veritabanı sütunu değildir).
    /// </summary>
    public bool IsTransient { get; private set; } = true;

    public void MarkPersisted() => IsTransient = false;

    private readonly List<DomainEvent> _domainEvents = [];
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
}
