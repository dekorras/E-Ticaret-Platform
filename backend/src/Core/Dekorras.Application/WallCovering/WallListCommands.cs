using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

public sealed record TryOnItemDto(Guid ProductId, int SortOrder, string? ConfigurationJson, WallCardDto Card);

public sealed record TryOnListDto(IReadOnlyList<TryOnItemDto> Items, string? ShareToken, int MaxItems);

/// <summary>"Duvarımda Dene" listesi (spec 1.6.2). OwnerKey: bkz. <see cref="WallOwner"/>.</summary>
public sealed record GetTryOnListQuery(string OwnerKey) : IRequest<TryOnListDto>;

/// <summary>Paylaşım bağlantısından (salt okunur) liste.</summary>
public sealed record GetSharedTryOnListQuery(string ShareToken) : IRequest<TryOnListDto?>;

public sealed class TryOnListQueryHandler(IUnitOfWork unitOfWork, ISender sender)
    : IRequestHandler<GetTryOnListQuery, TryOnListDto>, IRequestHandler<GetSharedTryOnListQuery, TryOnListDto?>
{
    public async Task<TryOnListDto> Handle(GetTryOnListQuery request, CancellationToken cancellationToken)
    {
        var list = unitOfWork.Repository<TryOnList>().Query().FirstOrDefault(l => l.OwnerKey == request.OwnerKey);
        return list is null ? new TryOnListDto([], null, TryOnList.MaxItems) : await ToDtoAsync(list, cancellationToken);
    }

    public async Task<TryOnListDto?> Handle(GetSharedTryOnListQuery request, CancellationToken cancellationToken)
    {
        var list = unitOfWork.Repository<TryOnList>().Query().FirstOrDefault(l => l.ShareToken == request.ShareToken);
        return list is null ? null : await ToDtoAsync(list, cancellationToken);
    }

    private async Task<TryOnListDto> ToDtoAsync(TryOnList list, CancellationToken cancellationToken)
    {
        // Items gezinmesi pasif yüklemeyle boş gelir (bkz. README) - satırlar ayrı sorguyla okunur.
        var rows = unitOfWork.Repository<TryOnListItem>().Query().Where(i => i.TryOnListId == list.Id).OrderBy(i => i.SortOrder).ToList();
        if (rows.Count == 0) return new TryOnListDto([], list.ShareToken, TryOnList.MaxItems);

        var cards = (await sender.Send(new GetWallCatalogQuery(OnlyProductIds: rows.Select(r => r.ProductId).ToList(), PageSize: TryOnList.MaxItems), cancellationToken))
            .Items.ToDictionary(c => c.ProductId);

        // Pasife alınmış/silinmiş ürünler listede sessizce görünmez.
        var items = rows.Where(r => cards.ContainsKey(r.ProductId))
            .Select(r => new TryOnItemDto(r.ProductId, r.SortOrder, r.ConfigurationJson, cards[r.ProductId]))
            .ToList();
        return new TryOnListDto(items, list.ShareToken, TryOnList.MaxItems);
    }
}

public sealed record AddTryOnItemCommand(string OwnerKey, Guid ProductId, string? ConfigurationJson = null) : IRequest<Unit>;
public sealed record RemoveTryOnItemCommand(string OwnerKey, Guid ProductId) : IRequest<Unit>;
public sealed record ReorderTryOnListCommand(string OwnerKey, IReadOnlyList<Guid> ProductIds) : IRequest<Unit>;
public sealed record ShareTryOnListCommand(string OwnerKey) : IRequest<string>;

/// <summary>Girişte misafir listesini üye listesine birleştirir (spec 1.6.2), misafir listesini siler.</summary>
public sealed record MergeTryOnListCommand(string GuestOwnerKey, string CustomerOwnerKey) : IRequest<int>;

public sealed class TryOnListCommandHandler(IUnitOfWork unitOfWork, IPricingService pricingService) :
    IRequestHandler<AddTryOnItemCommand, Unit>,
    IRequestHandler<RemoveTryOnItemCommand, Unit>,
    IRequestHandler<ReorderTryOnListCommand, Unit>,
    IRequestHandler<ShareTryOnListCommand, string>,
    IRequestHandler<MergeTryOnListCommand, int>
{
    public async Task<Unit> Handle(AddTryOnItemCommand request, CancellationToken cancellationToken)
    {
        if (!pricingService.IsConfigurable(request.ProductId))
            throw new KeyNotFoundException("Ürün bulunamadı veya duvarda denenemez.");

        // Konfigürasyon JSON'u saklanmadan önce doğrulanır (bozuk/yabancı JSON listeye yazılmasın).
        var configurationJson = request.ConfigurationJson is null ? null : WallConfiguration.FromJson(request.ConfigurationJson)?.ToJson();

        var list = await LoadOrCreateAsync(request.OwnerKey, cancellationToken);
        list.Add(request.ProductId, configurationJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(RemoveTryOnItemCommand request, CancellationToken cancellationToken)
    {
        var list = await LoadAsync(request.OwnerKey, cancellationToken);
        if (list is null) return Unit.Value;
        list.Remove(request.ProductId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(ReorderTryOnListCommand request, CancellationToken cancellationToken)
    {
        var list = await LoadAsync(request.OwnerKey, cancellationToken);
        if (list is null) return Unit.Value;
        list.Reorder(request.ProductIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<string> Handle(ShareTryOnListCommand request, CancellationToken cancellationToken)
    {
        var list = await LoadOrCreateAsync(request.OwnerKey, cancellationToken);
        var token = list.EnsureShareToken();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<int> Handle(MergeTryOnListCommand request, CancellationToken cancellationToken)
    {
        if (request.GuestOwnerKey == request.CustomerOwnerKey) return 0;
        var guest = await LoadAsync(request.GuestOwnerKey, cancellationToken);
        if (guest is null) return 0;

        var member = await LoadAsync(request.CustomerOwnerKey, cancellationToken);
        int added;
        if (member is null)
        {
            // Üyenin listesi yoksa misafir listesi doğrudan üyeye devredilir (paylaşım anahtarı korunur).
            guest.SetOwner(request.CustomerOwnerKey);
            added = guest.Items.Count;
        }
        else
        {
            added = member.MergeFrom(guest);
            unitOfWork.Repository<TryOnList>().Remove(guest);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return added;
    }

    private async Task<TryOnList?> LoadAsync(string ownerKey, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<TryOnList>();
        var list = repository.Query().FirstOrDefault(l => l.OwnerKey == ownerKey);
        if (list is not null) await repository.LoadCollectionAsync(list, l => l.Items, cancellationToken);
        return list;
    }

    private async Task<TryOnList> LoadOrCreateAsync(string ownerKey, CancellationToken cancellationToken)
    {
        var list = await LoadAsync(ownerKey, cancellationToken);
        if (list is not null) return list;
        list = new TryOnList(ownerKey);
        await unitOfWork.Repository<TryOnList>().AddAsync(list, cancellationToken);
        return list;
    }
}

/// <summary>Üyenin favori (Wishlist) ürün kimlikleri - katalog kartlarındaki ♡ durumu için hafif sorgu.</summary>
public sealed record GetFavoriteProductIdsQuery(string IdentityUserId) : IRequest<IReadOnlyList<Guid>>;

public sealed class GetFavoriteProductIdsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetFavoriteProductIdsQuery, IReadOnlyList<Guid>>
{
    public Task<IReadOnlyList<Guid>> Handle(GetFavoriteProductIdsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = unitOfWork.Repository<Customer>().Query()
            .Where(c => c.IdentityUserId == request.IdentityUserId)
            .SelectMany(c => c.Wishlist.Select(w => w.ProductId))
            .ToList();
        return Task.FromResult(ids);
    }
}

/// <summary>Misafirin çerezde tuttuğu favorileri girişte üyenin Wishlist'ine taşır.</summary>
public sealed record MergeGuestFavoritesCommand(string IdentityUserId, IReadOnlyList<Guid> ProductIds) : IRequest<int>;

public sealed class MergeGuestFavoritesCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MergeGuestFavoritesCommand, int>
{
    public async Task<int> Handle(MergeGuestFavoritesCommand request, CancellationToken cancellationToken)
    {
        if (request.ProductIds.Count == 0) return 0;

        var repository = unitOfWork.Repository<Customer>();
        var customer = repository.Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);
        if (customer is null) return 0;
        await repository.LoadCollectionAsync(customer, c => c.Wishlist, cancellationToken);

        var existingProducts = unitOfWork.Repository<Product>().Query().Where(p => request.ProductIds.Contains(p.Id)).Select(p => p.Id).ToHashSet();
        var before = customer.Wishlist.Count;
        foreach (var id in request.ProductIds.Where(existingProducts.Contains))
            customer.AddToWishlist(id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return customer.Wishlist.Count - before;
    }
}
