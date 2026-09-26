using System.Security.Cryptography;
using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>Sipariş sonrası müşteriye gönderilen, ölçüsüne uyarlanmış onay önizlemesi (spec 1.7).
/// Bir siparişin TÜM proof'ları onaylanmadan sipariş Preparing (üretim) durumuna geçemez.
/// <see cref="Token"/>, e-postadaki onay bağlantısının giriş gerektirmeden çalışması içindir.</summary>
public class ProductionProof : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public string? PreviewUrl { get; private set; }
    public ProofStatus Status { get; private set; } = ProofStatus.Beklemede;
    public string Token { get; private set; } = default!;
    public string? CustomerNote { get; private set; }
    public DateTime? RespondedAtUtc { get; private set; }
    public DateTime? AutoApproveAtUtc { get; private set; }
    public bool AutoApproved { get; private set; }

    private ProductionProof() { }

    public ProductionProof(Guid orderId, Guid orderItemId, DateTime? autoApproveAtUtc)
    {
        OrderId = orderId;
        OrderItemId = orderItemId;
        AutoApproveAtUtc = autoApproveAtUtc;
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    public void SetPreview(string previewUrl) => PreviewUrl = previewUrl;

    public void Approve(string? note = null)
    {
        EnsurePending();
        Status = ProofStatus.Onaylandi;
        CustomerNote = note;
        RespondedAtUtc = DateTime.UtcNow;
    }

    public void RequestRevision(string note)
    {
        EnsurePending();
        if (string.IsNullOrWhiteSpace(note)) throw new DomainException("Revizyon talebinde açıklama zorunludur.");
        Status = ProofStatus.RevizyonIstendi;
        CustomerNote = note;
        RespondedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Otomatik onay süresi dolmuşsa onaylar; aksi halde false.</summary>
    public bool TryAutoApprove(DateTime nowUtc)
    {
        if (Status != ProofStatus.Beklemede || AutoApproveAtUtc is null || nowUtc < AutoApproveAtUtc) return false;
        Status = ProofStatus.Onaylandi;
        AutoApproved = true;
        RespondedAtUtc = nowUtc;
        return true;
    }

    /// <summary>Revizyon sonrası admin yeniden gönderir: önizleme temizlenir (arka plan işçisi yeniden üretip
    /// müşteriye tekrar e-posta atar), onay beklemeye döner.</summary>
    public void ResetForReissue(DateTime? autoApproveAtUtc)
    {
        PreviewUrl = null;
        Status = ProofStatus.Beklemede;
        CustomerNote = null;
        RespondedAtUtc = null;
        AutoApproved = false;
        AutoApproveAtUtc = autoApproveAtUtc;
    }

    private void EnsurePending()
    {
        if (Status != ProofStatus.Beklemede) throw new DomainException("Bu önizleme için zaten yanıt verilmiş.");
    }
}

/// <summary>Bir sipariş kalemi için üretilen baskı dosyası (panellere bölünmüş PDF).</summary>
public class ProductionFile : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public ProductionFileStatus Status { get; private set; } = ProductionFileStatus.Pending;
    public string? FileKey { get; private set; }
    public int PanelCount { get; private set; }
    public string? Error { get; private set; }
    public int Attempts { get; private set; }

    private ProductionFile() { }

    public ProductionFile(Guid orderId, Guid orderItemId)
    {
        OrderId = orderId;
        OrderItemId = orderItemId;
    }

    public void MarkProcessing()
    {
        Status = ProductionFileStatus.Processing;
        Attempts++;
    }

    public void MarkReady(string fileKey, int panelCount)
    {
        Status = ProductionFileStatus.Ready;
        FileKey = fileKey;
        PanelCount = panelCount;
        Error = null;
    }

    public void Requeue()
    {
        Status = ProductionFileStatus.Pending;
        Attempts = 0;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Status = ProductionFileStatus.Failed;
        Error = error.Length > 2000 ? error[..2000] : error;
    }
}
