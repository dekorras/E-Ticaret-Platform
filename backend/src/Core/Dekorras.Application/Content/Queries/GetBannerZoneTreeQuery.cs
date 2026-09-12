using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using MediatR;

namespace Dekorras.Application.Content.Queries;

public sealed record BannerContentDto(
    Guid Id,
    BannerContentType ContentType,
    int SortOrder,
    string? Title,
    string? Subtitle,
    string? Body,
    string? ImageUrl,
    string? ImageUrlMobile,
    string? AltText,
    string? LinkUrl,
    string? LinkTarget,
    string? ButtonText,
    string? SettingsJson,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc,
    bool IsActive);

public sealed record BannerNodeTreeDto(
    Guid Id,
    Guid? ParentId,
    BannerNodeType NodeType,
    int SortOrder,
    int Depth,
    string Path,
    bool IsActive,
    string? CustomCssClass,
    string? CustomId,
    string? SettingsJson,
    IReadOnlyCollection<BannerContentDto> Contents,
    IReadOnlyCollection<BannerNodeTreeDto> Children);

public sealed record BannerZoneTreeDto(Guid Id, string Key, string Name, bool IsActive, IReadOnlyCollection<BannerNodeTreeDto> Roots);

/// <summary>
/// Bir banner bölgesinin tüm ağacını (sınırsız derinlik) TEK sorguda düz çekip GetCategoryTreeQuery
/// ile BİREBİR AYNI yerel özyinelemeli Map(...) deseniyle hiyerarşiye dönüştürür. Hem Storefront
/// ViewComponent'i (IncludeInactive: false - yalnızca yayında olanlar) hem Admin builder ekranı
/// (IncludeInactive: true - düzenlenebilir her şey) BU sorguyu kullanır.
/// </summary>
public sealed record GetBannerZoneTreeQuery(string ZoneKey, bool IncludeInactive = false) : IRequest<BannerZoneTreeDto?>;

public sealed class GetBannerZoneTreeQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBannerZoneTreeQuery, BannerZoneTreeDto?>
{
    public Task<BannerZoneTreeDto?> Handle(GetBannerZoneTreeQuery request, CancellationToken cancellationToken)
    {
        var zone = unitOfWork.Repository<BannerZone>().Query().FirstOrDefault(z => z.Key == request.ZoneKey);
        if (zone is null || (!request.IncludeInactive && !zone.IsActive))
            return Task.FromResult<BannerZoneTreeDto?>(null);

        var now = DateTime.UtcNow;

        var nodesQuery = unitOfWork.Repository<BannerNode>().Query().Where(n => n.BannerZoneId == zone.Id);
        if (!request.IncludeInactive) nodesQuery = nodesQuery.Where(n => n.IsActive);

        var flatNodes = nodesQuery
            .Select(n => new
            {
                n.Id,
                n.ParentId,
                n.NodeType,
                n.SortOrder,
                n.Depth,
                n.Path,
                n.IsActive,
                n.CustomCssClass,
                n.CustomId,
                n.SettingsJson
            })
            .ToList();

        var nodeIds = flatNodes.Select(n => n.Id).ToList();
        var contentsQuery = unitOfWork.Repository<BannerContent>().Query().Where(c => nodeIds.Contains(c.BannerNodeId));
        if (!request.IncludeInactive)
            contentsQuery = contentsQuery.Where(c => c.IsActive
                && (c.StartDateUtc == null || c.StartDateUtc <= now)
                && (c.EndDateUtc == null || c.EndDateUtc >= now));

        var flatContents = contentsQuery
            .Select(c => new { c.BannerNodeId, Dto = new BannerContentDto(c.Id, c.ContentType, c.SortOrder, c.Title, c.Subtitle, c.Body,
                c.ImageUrl, c.ImageUrlMobile, c.AltText, c.LinkUrl, c.LinkTarget, c.ButtonText, c.SettingsJson,
                c.StartDateUtc, c.EndDateUtc, c.IsActive) })
            .ToList();

        var contentsByNode = flatContents.GroupBy(c => c.BannerNodeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).OrderBy(d => d.SortOrder).ToList());

        BannerNodeTreeDto Map(Guid id)
        {
            var n = flatNodes.First(x => x.Id == id);
            var contents = contentsByNode.TryGetValue(id, out var list) ? list : [];

            return new BannerNodeTreeDto(
                n.Id, n.ParentId, n.NodeType, n.SortOrder, n.Depth, n.Path, n.IsActive, n.CustomCssClass, n.CustomId, n.SettingsJson,
                contents,
                flatNodes.Where(c => c.ParentId == id)
                    .OrderBy(c => c.SortOrder)
                    .Select(c => Map(c.Id))
                    .ToList());
        }

        var roots = flatNodes.Where(n => n.ParentId is null)
            .OrderBy(n => n.SortOrder)
            .Select(n => Map(n.Id))
            .ToList();

        return Task.FromResult<BannerZoneTreeDto?>(new BannerZoneTreeDto(zone.Id, zone.Key, zone.Name, zone.IsActive, roots));
    }
}
