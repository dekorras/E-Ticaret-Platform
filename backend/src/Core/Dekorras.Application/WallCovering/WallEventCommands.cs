using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

/// <summary>"Duvarında Gör" ölçüm olayı (spec 1.6.6-E). Bilinmeyen olay türleri sessizce yok sayılır
/// (istemciden gelen serbest metin tabloya yazılmaz).</summary>
public sealed record TrackWallPreviewEventCommand(string EventType, string? Source, Guid? ProductId, string? VisitorKey) : IRequest<bool>;

public sealed class TrackWallPreviewEventCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<TrackWallPreviewEventCommand, bool>
{
    public async Task<bool> Handle(TrackWallPreviewEventCommand request, CancellationToken cancellationToken)
    {
        if (!WallPreviewEvent.KnownTypes.Contains(request.EventType)) return false;
        var source = request.Source is not null && WallPreviewEvent.KnownSources.Contains(request.Source) ? request.Source : null;

        await unitOfWork.Repository<WallPreviewEvent>().AddAsync(new WallPreviewEvent(request.EventType, source, request.ProductId, request.VisitorKey), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record WallPreviewFunnelRow(string Source, int LinkClicks, int AddToCarts, decimal ConversionRate);

public sealed record WallPreviewReportDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int LinkClicks,
    int SceneChanges,
    int ProductSwaps,
    int AddToCarts,
    decimal ConversionRate,
    IReadOnlyList<WallPreviewFunnelRow> BySource,
    IReadOnlyList<WallPreviewDailyRow> Daily);

public sealed record WallPreviewDailyRow(DateOnly Day, int LinkClicks, int AddToCarts);

/// <summary>Admin raporu: "Duvarında Gör → sepete ekleme dönüşüm oranı". Dönüşüm = sepete ekleme olayı
/// sayısı / bağlantı tıklaması sayısı (aynı dönem, olay bazlı).
/// VARSAYIM: ziyaretçi bazlı eşleştirme yerine olay oranı kullanılır (ölçüm olayları kişisel veri tutmaz).</summary>
public sealed record GetWallPreviewReportQuery(DateTime FromUtc, DateTime ToUtc) : IRequest<WallPreviewReportDto>;

public sealed class GetWallPreviewReportQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetWallPreviewReportQuery, WallPreviewReportDto>
{
    public Task<WallPreviewReportDto> Handle(GetWallPreviewReportQuery request, CancellationToken cancellationToken)
    {
        var rows = unitOfWork.Repository<WallPreviewEvent>().Query()
            .Where(e => e.OccurredAtUtc >= request.FromUtc && e.OccurredAtUtc < request.ToUtc)
            .GroupBy(e => new { e.EventType, e.Source, Day = e.OccurredAtUtc.Date })
            .Select(g => new { g.Key.EventType, g.Key.Source, g.Key.Day, Count = g.Count() })
            .ToList();

        int Count(string type) => rows.Where(r => r.EventType == type).Sum(r => r.Count);
        static decimal Rate(int adds, int clicks) => clicks == 0 ? 0m : Math.Round(adds * 100m / clicks, 1);

        var clicks = Count("wall_preview_link_click");
        var adds = Count("wall_preview_add_to_cart");

        var bySource = rows.GroupBy(r => r.Source ?? "bilinmiyor")
            .Select(g =>
            {
                var c = g.Where(r => r.EventType == "wall_preview_link_click").Sum(r => r.Count);
                var a = g.Where(r => r.EventType == "wall_preview_add_to_cart").Sum(r => r.Count);
                return new WallPreviewFunnelRow(g.Key, c, a, Rate(a, c));
            })
            .OrderByDescending(r => r.LinkClicks)
            .ToList();

        var daily = rows.GroupBy(r => DateOnly.FromDateTime(r.Day)).OrderBy(g => g.Key)
            .Select(g => new WallPreviewDailyRow(g.Key, g.Where(r => r.EventType == "wall_preview_link_click").Sum(r => r.Count), g.Where(r => r.EventType == "wall_preview_add_to_cart").Sum(r => r.Count)))
            .ToList();

        return Task.FromResult(new WallPreviewReportDto(request.FromUtc, request.ToUtc, clicks, Count("wall_preview_scene_change"),
            Count("wall_preview_product_swap"), adds, Rate(adds, clicks), bySource, daily));
    }
}
