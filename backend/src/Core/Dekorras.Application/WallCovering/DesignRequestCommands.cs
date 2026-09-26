using System.Net;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.WallCovering;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.WallCovering;

public sealed record DesignRequestFile(Stream Content, string FileName, long Length);

/// <summary>Tasarım değişiklik talebi (spec 1.8): form + dosya ekleri (JPG/PNG/WebP, en fazla 10 MB, en fazla 5
/// dosya). Ekler içerikten doğrulanır, EXIF temizlenip yeniden kodlanır ve özel depoda tutulur. SLA iş günüyle
/// hesaplanır (varsayılan 2); müşteriye ve admin'e e-posta gider.</summary>
public sealed record CreateDesignRequestCommand(
    string FullName,
    string Email,
    string? ProductSlug,
    DesignRequestType RequestType,
    string Message,
    string? ConfigurationJson,
    IReadOnlyList<DesignRequestFile> Files) : IRequest<Guid>;

public sealed class CreateDesignRequestCommandValidator : AbstractValidator<CreateDesignRequestCommand>
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    public CreateDesignRequestCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Adınızı girin.").MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().WithMessage("E-posta adresinizi girin.").EmailAddress().WithMessage("Geçerli bir e-posta adresi girin.");
        RuleFor(x => x.Message).NotEmpty().WithMessage("Talebinizi açıklayın.").MaximumLength(4000);
        RuleFor(x => x.Files.Count).LessThanOrEqualTo(MaxFiles).WithMessage($"En fazla {MaxFiles} dosya ekleyebilirsiniz.");
        RuleForEach(x => x.Files).Must(f => f.Length <= MaxFileBytes).WithMessage("Her dosya en fazla 10 MB olabilir.");
    }
}

public sealed class CreateDesignRequestCommandHandler(IUnitOfWork unitOfWork, IImageDerivativeService images, IWallImageStore store, IEmailSender emailSender)
    : IRequestHandler<CreateDesignRequestCommand, Guid>
{
    public async Task<Guid> Handle(CreateDesignRequestCommand request, CancellationToken cancellationToken)
    {
        var settings = WallCoveringSettings.Load(unitOfWork);
        Guid? productId = request.ProductSlug is null ? null
            : unitOfWork.Repository<Product>().Query().Where(p => p.Slug == request.ProductSlug).Select(p => (Guid?)p.Id).FirstOrDefault();
        var configurationJson = request.ConfigurationJson is null ? null : WallConfiguration.FromJson(request.ConfigurationJson)?.ToJson();
        var due = DeliveryEstimator.AddBusinessDays(DateTime.UtcNow, settings.DesignRequestSlaBusinessDays, settings.ExtraHolidays);

        var designRequest = new DesignRequest(request.FullName.Trim(), request.Email.Trim(), productId, request.RequestType, request.Message.Trim(), due, configurationJson);

        // Önce tüm dosyalar doğrulanır: biri geçersizse hiçbir şey kaydedilmez.
        var sanitized = new List<(byte[] Bytes, string Name)>();
        foreach (var file in request.Files)
        {
            using var buffer = new MemoryStream();
            await file.Content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            var inspection = images.Inspect(buffer);
            if (!inspection.IsValid)
                throw new WallConfigurationException(new Dictionary<string, string> { ["files"] = $"'{file.FileName}': {inspection.Error}" });
            buffer.Position = 0;
            var (bytes, _, _) = await images.SanitizeAsync(buffer, 4000, cancellationToken);
            sanitized.Add((bytes, Path.GetFileNameWithoutExtension(file.FileName)));
        }

        for (var i = 0; i < sanitized.Count; i++)
        {
            var key = $"wall/design-requests/{designRequest.Id:N}/{i + 1}.jpg";
            await store.SavePrivateAsync(key, new MemoryStream(sanitized[i].Bytes), cancellationToken);
            var safeName = new string(sanitized[i].Name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ').Take(80).ToArray());
            designRequest.AddAttachment(key, $"{(safeName.Length > 0 ? safeName : "ek")}.jpg", "image/jpeg", sanitized[i].Bytes.LongLength);
        }

        await unitOfWork.Repository<DesignRequest>().AddAsync(designRequest, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dueText = due.ToLocalTime().ToString("dd.MM.yyyy");
        await TrySendAsync(designRequest.Email, "Tasarım talebiniz alındı",
            $"<p>Sayın {WebUtility.HtmlEncode(designRequest.FullName)},</p><p>Tasarım değişiklik talebiniz alındı. En geç <strong>{dueText}</strong> tarihine kadar size dönüş yapacağız.</p>", cancellationToken);
        if (settings.AdminNotificationEmail is { } admin)
            await TrySendAsync(admin, $"Yeni tasarım talebi ({request.RequestType})",
                $"<p>{WebUtility.HtmlEncode(designRequest.FullName)} ({WebUtility.HtmlEncode(designRequest.Email)})</p><p>{WebUtility.HtmlEncode(designRequest.Message)}</p><p>Ek: {sanitized.Count} · SLA: {dueText}</p>", cancellationToken);

        return designRequest.Id;
    }

    private async Task TrySendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try { await emailSender.SendAsync(to, subject, body, cancellationToken); }
        catch { /* e-posta hatası talebi geri almaz */ }
    }
}

public sealed record DesignRequestListItemDto(Guid Id, string FullName, string Email, string? ProductName, DesignRequestType RequestType,
    DesignRequestStatus Status, DateTime CreatedAtUtc, DateTime DueAtUtc, bool IsOverdue, int AttachmentCount);

public sealed record DesignRequestAttachmentDto(Guid Id, string FileName, long SizeBytes);

public sealed record DesignRequestDetailDto(Guid Id, string FullName, string Email, Guid? ProductId, string? ProductName, string? ProductSlug,
    DesignRequestType RequestType, string Message, string? ConfigurationJson, DesignRequestStatus Status, DateTime CreatedAtUtc, DateTime DueAtUtc,
    bool IsOverdue, string? AdminNote, DateTime? RespondedAtUtc, IReadOnlyList<DesignRequestAttachmentDto> Attachments);

public sealed record GetDesignRequestsQuery(DesignRequestStatus? Status = null, bool OnlyOverdue = false) : IRequest<IReadOnlyList<DesignRequestListItemDto>>;

public sealed record GetDesignRequestQuery(Guid Id) : IRequest<DesignRequestDetailDto?>;

public sealed record UpdateDesignRequestStatusCommand(Guid Id, DesignRequestStatus Status, string? AdminNote) : IRequest<Unit>;

public sealed record OpenDesignRequestAttachmentQuery(Guid AttachmentId) : IRequest<ProductionFileDownload?>;

public sealed class DesignRequestAdminHandler(IUnitOfWork unitOfWork, IWallImageStore store) :
    IRequestHandler<GetDesignRequestsQuery, IReadOnlyList<DesignRequestListItemDto>>,
    IRequestHandler<GetDesignRequestQuery, DesignRequestDetailDto?>,
    IRequestHandler<UpdateDesignRequestStatusCommand, Unit>,
    IRequestHandler<OpenDesignRequestAttachmentQuery, ProductionFileDownload?>
{
    public Task<IReadOnlyList<DesignRequestListItemDto>> Handle(GetDesignRequestsQuery request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = unitOfWork.Repository<DesignRequest>().Query();
        if (request.Status is { } s) query = query.Where(r => r.Status == s);
        if (request.OnlyOverdue) query = query.Where(r => (r.Status == DesignRequestStatus.New || r.Status == DesignRequestStatus.InProgress) && r.DueAtUtc < now);

        IReadOnlyList<DesignRequestListItemDto> list = query.OrderBy(r => r.Status).ThenBy(r => r.DueAtUtc)
            .Select(r => new
            {
                r.Id, r.FullName, r.Email, r.RequestType, r.Status, r.CreatedAtUtc, r.DueAtUtc,
                ProductName = r.ProductId == null ? null
                    : unitOfWork.Repository<ProductTranslation>().Query().Where(t => t.ProductId == r.ProductId && t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault(),
                Attachments = r.Attachments.Count
            })
            .ToList()
            .Select(r => new DesignRequestListItemDto(r.Id, r.FullName, r.Email, r.ProductName, r.RequestType, r.Status, r.CreatedAtUtc, r.DueAtUtc,
                r.Status is DesignRequestStatus.New or DesignRequestStatus.InProgress && now > r.DueAtUtc, r.Attachments))
            .ToList();
        return Task.FromResult(list);
    }

    public Task<DesignRequestDetailDto?> Handle(GetDesignRequestQuery request, CancellationToken cancellationToken)
    {
        var r = unitOfWork.Repository<DesignRequest>().Query().FirstOrDefault(x => x.Id == request.Id);
        if (r is null) return Task.FromResult<DesignRequestDetailDto?>(null);
        var attachments = unitOfWork.Repository<DesignRequestAttachment>().Query().Where(a => a.DesignRequestId == r.Id)
            .Select(a => new DesignRequestAttachmentDto(a.Id, a.FileName, a.SizeBytes)).ToList();
        var product = r.ProductId is null ? null : unitOfWork.Repository<Product>().Query().Where(p => p.Id == r.ProductId)
            .Select(p => new { p.Slug, Name = p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault() }).FirstOrDefault();
        return Task.FromResult<DesignRequestDetailDto?>(new DesignRequestDetailDto(r.Id, r.FullName, r.Email, r.ProductId, product?.Name, product?.Slug,
            r.RequestType, r.Message, r.ConfigurationJson, r.Status, r.CreatedAtUtc, r.DueAtUtc, r.IsOverdue(DateTime.UtcNow), r.AdminNote, r.RespondedAtUtc, attachments));
    }

    public async Task<Unit> Handle(UpdateDesignRequestStatusCommand request, CancellationToken cancellationToken)
    {
        var r = unitOfWork.Repository<DesignRequest>().Query().FirstOrDefault(x => x.Id == request.Id)
            ?? throw new KeyNotFoundException("Talep bulunamadı.");
        r.SetStatus(request.Status, request.AdminNote);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public Task<ProductionFileDownload?> Handle(OpenDesignRequestAttachmentQuery request, CancellationToken cancellationToken)
    {
        var a = unitOfWork.Repository<DesignRequestAttachment>().Query().FirstOrDefault(x => x.Id == request.AttachmentId);
        var stream = a is null ? null : store.OpenPrivate(a.FileKey);
        return Task.FromResult(stream is null ? null : new ProductionFileDownload(stream, a!.FileName));
    }
}
