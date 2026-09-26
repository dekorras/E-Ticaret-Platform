using System.Globalization;
using System.Net;
using System.Text.Json;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.WallCovering;
using MediatR;

namespace Dekorras.Application.WallCovering;

/// <summary>Sipariş sonrası akışın ortak yardımcıları (spec 1.7).</summary>
public static class WallOrderSupport
{
    /// <summary>PlaceOrderCommand içinde, sipariş kaydedilmeden AYNI işlemde çağrılır: her ölçüye özel kalem
    /// için bir onay önizlemesi (Beklemede) ve bir üretim dosyası (Pending) kaydı açılır. Önizleme görseli ve
    /// PDF arka plan işçisinde üretilir.</summary>
    public static async Task CreateProductionTasksAsync(IUnitOfWork unitOfWork, Order order, CancellationToken cancellationToken)
    {
        var settings = WallCoveringSettings.Load(unitOfWork);
        DateTime? autoApprove = settings.ProofAutoApproveHours > 0 ? DateTime.UtcNow.AddHours(settings.ProofAutoApproveHours) : null;
        foreach (var item in order.Items.Where(i => i.ConfigurationJson is not null))
        {
            await unitOfWork.Repository<ProductionProof>().AddAsync(new ProductionProof(order.Id, item.Id, autoApprove), cancellationToken);
            await unitOfWork.Repository<ProductionFile>().AddAsync(new ProductionFile(order.Id, item.Id), cancellationToken);
        }
    }

    public static ProductionRenderRequest? BuildRenderRequest(IUnitOfWork unitOfWork, Guid orderItemId, out string? customerEmail)
    {
        customerEmail = null;
        var row = unitOfWork.Repository<OrderItem>().Query().Where(i => i.Id == orderItemId)
            .Join(unitOfWork.Repository<Order>().Query(), i => i.OrderId, o => o.Id, (i, o) => new { i.ProductId, i.ProductName, i.ConfigurationJson, i.PricingSnapshotJson, o.OrderNumber, o.CustomerId })
            .FirstOrDefault();
        if (row?.ConfigurationJson is null) return null;

        customerEmail = unitOfWork.Repository<Customer>().Query().Where(c => c.Id == row.CustomerId).Select(c => c.Email).FirstOrDefault();
        var configuration = WallConfiguration.FromJson(row.ConfigurationJson)!;
        // Siparişte DONDURULMUŞ kırılım (malzeme, pay, panel eni) kullanılır - malzeme sonradan değişse bile.
        var frozen = row.PricingSnapshotJson is null ? null : JsonSerializer.Deserialize<WallpaperLinePrice>(row.PricingSnapshotJson);
        var poster = RenderWallPreviewQueryHandler.LoadPosterSource(unitOfWork, row.ProductId)?.Source;
        if (poster is null || frozen is null) return null;

        return new ProductionRenderRequest(poster, configuration, frozen.BleedCm, frozen.PanelWidthCm, row.OrderNumber,
            $"{configuration.WidthCm.ToString("0.#", CultureInfo.InvariantCulture)}x{configuration.HeightCm.ToString("0.#", CultureInfo.InvariantCulture)} {frozen.MaterialName}");
    }

    public static string BaseUrl(WallCoveringSettings settings) => settings.PublicBaseUrl ?? "";
}

public sealed record ProofPageDto(
    Guid ProofId,
    Guid OrderId,
    string OrderNumber,
    string ProductName,
    string? PreviewUrl,
    ProofStatus Status,
    string? CustomerNote,
    DateTime? AutoApproveAtUtc,
    decimal WidthCm,
    decimal HeightCm,
    string MaterialName,
    int PanelCount,
    string Token);

public sealed record GetProofByTokenQuery(string Token) : IRequest<ProofPageDto?>;

public sealed class GetProofByTokenQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProofByTokenQuery, ProofPageDto?>
{
    public Task<ProofPageDto?> Handle(GetProofByTokenQuery request, CancellationToken cancellationToken)
    {
        var proof = unitOfWork.Repository<ProductionProof>().Query().FirstOrDefault(p => p.Token == request.Token);
        return Task.FromResult(proof is null ? null : ToDto(unitOfWork, proof));
    }

    public static ProofPageDto ToDto(IUnitOfWork unitOfWork, ProductionProof proof)
    {
        var item = unitOfWork.Repository<OrderItem>().Query().First(i => i.Id == proof.OrderItemId);
        var orderNumber = unitOfWork.Repository<Order>().Query().Where(o => o.Id == proof.OrderId).Select(o => o.OrderNumber).First();
        var config = WallConfiguration.FromJson(item.ConfigurationJson)!;
        var frozen = item.PricingSnapshotJson is null ? null : JsonSerializer.Deserialize<WallpaperLinePrice>(item.PricingSnapshotJson);
        return new ProofPageDto(proof.Id, proof.OrderId, orderNumber, item.ProductName, proof.PreviewUrl, proof.Status, proof.CustomerNote, proof.AutoApproveAtUtc,
            config.WidthCm, config.HeightCm, frozen?.MaterialName ?? config.MaterialCode, frozen?.PanelCount ?? 0, proof.Token);
    }
}

/// <summary>Müşteri yanıtı (e-postadaki bağlantı; giriş gerekmez - token yeterli). Revizyon talebinde admin'e haber verilir.</summary>
public sealed record RespondToProofCommand(string Token, bool Approve, string? Note) : IRequest<ProofPageDto>;

public sealed class RespondToProofCommandHandler(IUnitOfWork unitOfWork, IEmailSender emailSender) : IRequestHandler<RespondToProofCommand, ProofPageDto>
{
    public async Task<ProofPageDto> Handle(RespondToProofCommand request, CancellationToken cancellationToken)
    {
        var proof = unitOfWork.Repository<ProductionProof>().Query().FirstOrDefault(p => p.Token == request.Token)
            ?? throw new KeyNotFoundException("Onay önizlemesi bulunamadı.");

        if (request.Approve) proof.Approve(request.Note);
        else proof.RequestRevision(request.Note ?? "");
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = GetProofByTokenQueryHandler.ToDto(unitOfWork, proof);
        var settings = WallCoveringSettings.Load(unitOfWork);
        if (!request.Approve && settings.AdminNotificationEmail is { } admin)
        {
            try
            {
                await emailSender.SendAsync(admin, $"Revizyon talebi - {dto.OrderNumber}",
                    $"<p>{WebUtility.HtmlEncode(dto.ProductName)} için müşteri revizyon istedi:</p><blockquote>{WebUtility.HtmlEncode(request.Note)}</blockquote>", cancellationToken);
            }
            catch { /* bildirim hatası yanıtı engellemez */ }
        }
        return dto;
    }
}

/// <summary>Arka plan: önizlemesi üretilmemiş onay kayıtları için ölçüsüne uyarlanmış önizleme üretir ve
/// müşteriye onay/revizyon bağlantılı e-posta gönderir. Dönüş: işlenen kayıt sayısı.</summary>
public sealed record GenerateProofPreviewsCommand(int BatchSize = 10) : IRequest<int>;

public sealed class GenerateProofPreviewsCommandHandler(IUnitOfWork unitOfWork, IProductionFileRenderer renderer, IWallImageStore store, IEmailSender emailSender)
    : IRequestHandler<GenerateProofPreviewsCommand, int>
{
    public async Task<int> Handle(GenerateProofPreviewsCommand request, CancellationToken cancellationToken)
    {
        var proofs = unitOfWork.Repository<ProductionProof>().Query()
            .Where(p => p.PreviewUrl == null && p.Status == ProofStatus.Beklemede)
            .OrderBy(p => p.CreatedAtUtc).Take(request.BatchSize).ToList();
        var settings = WallCoveringSettings.Load(unitOfWork);

        var done = 0;
        foreach (var proof in proofs)
        {
            var renderRequest = WallOrderSupport.BuildRenderRequest(unitOfWork, proof.OrderItemId, out var email);
            if (renderRequest is null) continue;

            var jpeg = await renderer.RenderProofPreviewAsync(renderRequest, 1600, cancellationToken);
            // Dosya adı tahmin edilemez token'dır: önizleme yalnızca bağlantıya sahip olana görünür.
            proof.SetPreview(await store.SavePublicAsync($"wall/proofs/{proof.Token}-{DateTime.UtcNow.Ticks:x}.jpg", jpeg, cancellationToken));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            done++;

            if (email is null) continue;
            var link = $"{WallOrderSupport.BaseUrl(settings)}/siparis-onay/{proof.Token}";
            var autoText = proof.AutoApproveAtUtc is { } at
                ? $"<p>{at.ToLocalTime():dd.MM.yyyy HH:mm} tarihine kadar yanıt vermezseniz tasarım onaylanmış sayılır.</p>" : "";
            try
            {
                await emailSender.SendAsync(email, $"Siparişiniz için onay önizlemesi - {renderRequest.OrderNumber}",
                    $"<p>Ölçünüze uyarlanmış baskı önizlemeniz hazır.</p><p><img src=\"{WallOrderSupport.BaseUrl(settings)}{proof.PreviewUrl}\" style=\"max-width:600px\" alt=\"Önizleme\"></p>" +
                    $"<p><a href=\"{link}\">Önizlemeyi onayla veya revizyon iste</a></p>{autoText}", cancellationToken);
            }
            catch { /* e-posta hatası önizlemeyi geri almaz; müşteri siparişlerim sayfasından da erişebilir */ }
        }
        return done;
    }
}

/// <summary>Arka plan: otomatik onay süresi dolan önizlemeleri onaylar.</summary>
public sealed record AutoApproveProofsCommand : IRequest<int>;

public sealed class AutoApproveProofsCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AutoApproveProofsCommand, int>
{
    public async Task<int> Handle(AutoApproveProofsCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var due = unitOfWork.Repository<ProductionProof>().Query()
            .Where(p => p.Status == ProofStatus.Beklemede && p.AutoApproveAtUtc != null && p.AutoApproveAtUtc <= now && p.PreviewUrl != null)
            .ToList();
        var count = due.Count(p => p.TryAutoApprove(now));
        if (count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);
        return count;
    }
}

/// <summary>Arka plan: bekleyen üretim dosyalarını (panellere bölünmüş PDF) üretir; hata olursa 3 denemeye kadar.</summary>
public sealed record GenerateProductionFilesCommand(int BatchSize = 3) : IRequest<int>;

public sealed class GenerateProductionFilesCommandHandler(IUnitOfWork unitOfWork, IProductionFileRenderer renderer, IWallImageStore store)
    : IRequestHandler<GenerateProductionFilesCommand, int>
{
    public const int MaxAttempts = 3;

    public async Task<int> Handle(GenerateProductionFilesCommand request, CancellationToken cancellationToken)
    {
        var files = unitOfWork.Repository<ProductionFile>().Query()
            .Where(f => f.Status == ProductionFileStatus.Pending || (f.Status == ProductionFileStatus.Failed && f.Attempts < MaxAttempts))
            .OrderBy(f => f.CreatedAtUtc).Take(request.BatchSize).ToList();

        foreach (var file in files)
        {
            file.MarkProcessing();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            try
            {
                var renderRequest = WallOrderSupport.BuildRenderRequest(unitOfWork, file.OrderItemId, out _)
                    ?? throw new InvalidOperationException("Sipariş kalemi veya poster görseli bulunamadı.");
                var pdf = await renderer.RenderPanelsPdfAsync(renderRequest, cancellationToken);
                var key = $"wall/production/{renderRequest.OrderNumber}/{file.OrderItemId:N}.pdf";
                await store.SavePrivateAsync(key, new MemoryStream(pdf.Content), cancellationToken);
                file.MarkReady(key, pdf.PanelCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                file.MarkFailed(ex.Message);
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return files.Count;
    }
}

/// <summary>Admin'in "yeniden üret" düğmesi: dosyayı tekrar kuyruğa alır.</summary>
public sealed record RequeueProductionFileCommand(Guid FileId) : IRequest<Unit>;

public sealed class RequeueProductionFileCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RequeueProductionFileCommand, Unit>
{
    public async Task<Unit> Handle(RequeueProductionFileCommand request, CancellationToken cancellationToken)
    {
        var file = unitOfWork.Repository<ProductionFile>().Query().FirstOrDefault(f => f.Id == request.FileId)
            ?? throw new KeyNotFoundException("Üretim dosyası bulunamadı.");
        file.Requeue();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Revizyon sonrası admin önizlemeyi yeniden gönderir: önizleme yeniden üretilir, müşteri yeniden onaylar.</summary>
public sealed record ReissueProofCommand(Guid ProofId) : IRequest<Unit>;

public sealed class ReissueProofCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<ReissueProofCommand, Unit>
{
    public async Task<Unit> Handle(ReissueProofCommand request, CancellationToken cancellationToken)
    {
        var proof = unitOfWork.Repository<ProductionProof>().Query().FirstOrDefault(p => p.Id == request.ProofId)
            ?? throw new KeyNotFoundException("Onay önizlemesi bulunamadı.");
        var settings = WallCoveringSettings.Load(unitOfWork);
        proof.ResetForReissue(settings.ProofAutoApproveHours > 0 ? DateTime.UtcNow.AddHours(settings.ProofAutoApproveHours) : null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record OrderWallItemDto(
    Guid OrderItemId,
    string ProductName,
    string ConfigurationSummary,
    Guid? ProofId,
    ProofStatus? ProofStatus,
    string? ProofPreviewUrl,
    string? ProofNote,
    bool AutoApproved,
    Guid? FileId,
    ProductionFileStatus? FileStatus,
    int PanelCount,
    string? FileError,
    string? ProofToken = null);

/// <summary>Bir siparişin ölçüye özel kalemleri + onay/üretim durumları (admin sipariş detayı ve müşteri "Siparişlerim").</summary>
public sealed record GetOrderWallItemsQuery(Guid OrderId) : IRequest<IReadOnlyList<OrderWallItemDto>>;

public sealed class GetOrderWallItemsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetOrderWallItemsQuery, IReadOnlyList<OrderWallItemDto>>
{
    public Task<IReadOnlyList<OrderWallItemDto>> Handle(GetOrderWallItemsQuery request, CancellationToken cancellationToken)
    {
        var items = unitOfWork.Repository<OrderItem>().Query().Where(i => i.OrderId == request.OrderId && i.ConfigurationJson != null).ToList();
        var proofs = unitOfWork.Repository<ProductionProof>().Query().Where(p => p.OrderId == request.OrderId).ToList().ToDictionary(p => p.OrderItemId);
        var files = unitOfWork.Repository<ProductionFile>().Query().Where(f => f.OrderId == request.OrderId).ToList().ToDictionary(f => f.OrderItemId);

        IReadOnlyList<OrderWallItemDto> result = items.Select(i =>
        {
            var c = WallConfiguration.FromJson(i.ConfigurationJson)!;
            proofs.TryGetValue(i.Id, out var p);
            files.TryGetValue(i.Id, out var f);
            var summary = string.Create(CultureInfo.GetCultureInfo("tr-TR"), $"{c.WidthCm:0.#}×{c.HeightCm:0.#} cm · {c.MaterialCode} · {(c.Fit == FitMode.Stretch ? "Esnet" : "Kırp")}{(c.Mirror ? " · Ayna" : "")}{(c.Filter != ImageFilter.None ? " · " + c.Filter : "")}");
            return new OrderWallItemDto(i.Id, i.ProductName, summary, p?.Id, p?.Status, p?.PreviewUrl, p?.CustomerNote, p?.AutoApproved ?? false,
                f?.Id, f?.Status, f?.PanelCount ?? 0, f?.Error, p?.Token);
        }).ToList();
        return Task.FromResult(result);
    }
}

public sealed record ProductionFileDownload(Stream Content, string FileName);

/// <summary>Admin: üretim dosyasını indir (özel depodan - herkese açık URL'si yoktur).</summary>
public sealed record OpenProductionFileQuery(Guid FileId) : IRequest<ProductionFileDownload?>;

public sealed class OpenProductionFileQueryHandler(IUnitOfWork unitOfWork, IWallImageStore store) : IRequestHandler<OpenProductionFileQuery, ProductionFileDownload?>
{
    public Task<ProductionFileDownload?> Handle(OpenProductionFileQuery request, CancellationToken cancellationToken)
    {
        var file = unitOfWork.Repository<ProductionFile>().Query().FirstOrDefault(f => f.Id == request.FileId && f.Status == ProductionFileStatus.Ready);
        var stream = file?.FileKey is null ? null : store.OpenPrivate(file.FileKey);
        return Task.FromResult(stream is null ? null : new ProductionFileDownload(stream, Path.GetFileName(file!.FileKey!)));
    }
}
