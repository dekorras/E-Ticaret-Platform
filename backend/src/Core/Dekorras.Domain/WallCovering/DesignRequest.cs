using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>Tasarım değişiklik talebi (spec 1.8). SLA: 2 iş günü - <see cref="DueAtUtc"/> oluşturulurken
/// DeliveryEstimator ile aynı iş günü kurallarıyla hesaplanıp dışarıdan verilir.</summary>
public class DesignRequest : AuditableEntity
{
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public Guid? ProductId { get; private set; }
    public DesignRequestType RequestType { get; private set; }
    public string Message { get; private set; } = default!;
    public DesignRequestStatus Status { get; private set; } = DesignRequestStatus.New;
    public DateTime DueAtUtc { get; private set; }
    public string? AdminNote { get; private set; }
    public DateTime? RespondedAtUtc { get; private set; }

    /// <summary>Ürün sayfasındaki o anki konfigürasyon (varsa) - tasarımcı bağlamı görsün diye.</summary>
    public string? ConfigurationJson { get; private set; }

    private readonly List<DesignRequestAttachment> _attachments = [];
    public IReadOnlyCollection<DesignRequestAttachment> Attachments => _attachments.AsReadOnly();

    private DesignRequest() { }

    public DesignRequest(string fullName, string email, Guid? productId, DesignRequestType requestType, string message, DateTime dueAtUtc, string? configurationJson)
    {
        FullName = fullName;
        Email = email;
        ProductId = productId;
        RequestType = requestType;
        Message = message;
        DueAtUtc = dueAtUtc;
        ConfigurationJson = configurationJson;
    }

    public void AddAttachment(string fileKey, string fileName, string contentType, long sizeBytes) =>
        _attachments.Add(new DesignRequestAttachment(Id, fileKey, fileName, contentType, sizeBytes));

    public void SetStatus(DesignRequestStatus status, string? adminNote)
    {
        Status = status;
        AdminNote = adminNote;
        if (status is DesignRequestStatus.Answered or DesignRequestStatus.Closed)
            RespondedAtUtc ??= DateTime.UtcNow;
    }

    public bool IsOverdue(DateTime nowUtc) => Status is DesignRequestStatus.New or DesignRequestStatus.InProgress && nowUtc > DueAtUtc;
}

public class DesignRequestAttachment : BaseEntity
{
    public Guid DesignRequestId { get; private set; }
    public string FileKey { get; private set; } = default!;
    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    private DesignRequestAttachment() { }

    public DesignRequestAttachment(Guid designRequestId, string fileKey, string fileName, string contentType, long sizeBytes)
    {
        DesignRequestId = designRequestId;
        FileKey = fileKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }
}
