using System.Security.Cryptography;
using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>"Duvarımda Dene" listesi (spec 1.6.2). Misafirde çerez anahtarıyla, üyede müşteriye bağlı;
/// girişte <see cref="MergeFrom"/> ile birleştirilir.</summary>
public class TryOnList : AuditableEntity
{
    public const int MaxItems = 12;

    public string OwnerKey { get; private set; } = default!;
    public string? ShareToken { get; private set; }

    private readonly List<TryOnListItem> _items = [];
    public IReadOnlyCollection<TryOnListItem> Items => _items.AsReadOnly();

    private TryOnList() { }

    public TryOnList(string ownerKey) => OwnerKey = ownerKey;

    /// <summary>Zaten listedeyse yalnızca konfigürasyonu güncellenir. Liste doluysa DomainException.</summary>
    public TryOnListItem Add(Guid productId, string? configurationJson = null)
    {
        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
        {
            if (configurationJson is not null) existing.SetConfiguration(configurationJson);
            return existing;
        }

        if (_items.Count >= MaxItems)
            throw new DomainException($"\"Duvarımda Dene\" listesine en fazla {MaxItems} poster eklenebilir.");

        var item = new TryOnListItem(Id, productId, _items.Count == 0 ? 0 : _items.Max(i => i.SortOrder) + 1, configurationJson);
        _items.Add(item);
        return item;
    }

    public void Remove(Guid productId)
    {
        _items.RemoveAll(i => i.ProductId == productId);
        Renumber();
    }

    /// <summary>Verilen sıraya göre yeniden sıralar; listede olmayan kimlikler yok sayılır, sırada
    /// belirtilmeyen öğeler sona eklenir.</summary>
    public void Reorder(IReadOnlyList<Guid> productIdsInOrder)
    {
        var order = productIdsInOrder.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var sorted = _items
            .OrderBy(i => order.TryGetValue(i.ProductId, out var idx) ? idx : int.MaxValue)
            .ThenBy(i => i.SortOrder)
            .ToList();
        for (var i = 0; i < sorted.Count; i++) sorted[i].SetSortOrder(i);
    }

    /// <summary>Misafir listesini üye listesine birleştirir; üye listesindeki öğeler önce gelir,
    /// kapasite dolunca kalan misafir öğeleri atılır.</summary>
    public int MergeFrom(TryOnList guestList)
    {
        var added = 0;
        foreach (var item in guestList.Items.OrderBy(i => i.SortOrder))
        {
            if (_items.Any(i => i.ProductId == item.ProductId)) continue;
            if (_items.Count >= MaxItems) break;
            Add(item.ProductId, item.ConfigurationJson);
            added++;
        }
        return added;
    }

    public string EnsureShareToken()
    {
        ShareToken ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        return ShareToken;
    }

    public void SetOwner(string ownerKey) => OwnerKey = ownerKey;

    private void Renumber()
    {
        var sorted = _items.OrderBy(i => i.SortOrder).ToList();
        for (var i = 0; i < sorted.Count; i++) sorted[i].SetSortOrder(i);
    }
}

public class TryOnListItem : BaseEntity
{
    public Guid TryOnListId { get; private set; }
    public Guid ProductId { get; private set; }
    public int SortOrder { get; private set; }

    /// <summary>İsteğe bağlı WallConfiguration JSON'u (malzeme, fit, ayna, filtre, kırpma).</summary>
    public string? ConfigurationJson { get; private set; }

    public DateTime AddedAtUtc { get; private set; } = DateTime.UtcNow;

    private TryOnListItem() { }

    public TryOnListItem(Guid tryOnListId, Guid productId, int sortOrder, string? configurationJson)
    {
        TryOnListId = tryOnListId;
        ProductId = productId;
        SortOrder = sortOrder;
        ConfigurationJson = configurationJson;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
    public void SetConfiguration(string? configurationJson) => ConfigurationJson = configurationJson;
}
