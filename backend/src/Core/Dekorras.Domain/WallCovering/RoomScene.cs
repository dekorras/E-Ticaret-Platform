using Dekorras.Domain.Common;

namespace Dekorras.Domain.WallCovering;

/// <summary>Oda sahnesi. <see cref="OwnerKey"/> boşsa admin'in tanımladığı hazır sahnedir; doluysa
/// kullanıcının kendi yüklediği oda fotoğrafıdır (spec 1.6.4 - "özel sahne"), yalnızca sahibine görünür.</summary>
public class RoomScene : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public RoomType RoomType { get; private set; }
    public string BaseImageUrl { get; private set; } = default!;
    public string? ShadowMapUrl { get; private set; }
    public string? ForegroundMaskUrl { get; private set; }
    public int ImageWidthPx { get; private set; }
    public int ImageHeightPx { get; private set; }

    public double TopLeftX { get; private set; }
    public double TopLeftY { get; private set; }
    public double TopRightX { get; private set; }
    public double TopRightY { get; private set; }
    public double BottomRightX { get; private set; }
    public double BottomRightY { get; private set; }
    public double BottomLeftX { get; private set; }
    public double BottomLeftY { get; private set; }

    public decimal RealWallWidthCm { get; private set; }
    public decimal RealWallHeightCm { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsDefault { get; private set; }

    /// <summary>"c:{customerId}" veya "g:{misafirAnahtarı}" - bkz. <see cref="WallOwner"/>.</summary>
    public string? OwnerKey { get; private set; }

    /// <summary>Sahne görseli/köşeleri değiştikçe artar - render önbellek anahtarına girer, eski mockup'lar geçersizleşir.</summary>
    public int Version { get; private set; } = 1;

    private RoomScene() { }

    public RoomScene(string name, RoomType roomType, string baseImageUrl, int imageWidthPx, int imageHeightPx, WallQuad wallQuad,
        decimal realWallWidthCm, decimal realWallHeightCm, string? ownerKey = null)
    {
        Name = name;
        RoomType = roomType;
        OwnerKey = ownerKey;
        SetImages(baseImageUrl, imageWidthPx, imageHeightPx, null, null);
        SetWall(wallQuad, realWallWidthCm, realWallHeightCm);
    }

    public WallQuad WallQuad => new(
        new Point2(TopLeftX, TopLeftY), new Point2(TopRightX, TopRightY),
        new Point2(BottomRightX, BottomRightY), new Point2(BottomLeftX, BottomLeftY));

    public bool IsUserScene => OwnerKey is not null;

    public void SetImages(string baseImageUrl, int imageWidthPx, int imageHeightPx, string? shadowMapUrl, string? foregroundMaskUrl)
    {
        if (imageWidthPx <= 0 || imageHeightPx <= 0) throw new DomainException("Sahne görsel boyutu geçersiz.");
        BaseImageUrl = baseImageUrl;
        ImageWidthPx = imageWidthPx;
        ImageHeightPx = imageHeightPx;
        ShadowMapUrl = shadowMapUrl;
        ForegroundMaskUrl = foregroundMaskUrl;
        Version++;
    }

    public void SetForegroundMask(string? foregroundMaskUrl)
    {
        ForegroundMaskUrl = foregroundMaskUrl;
        Version++;
    }

    public void SetWall(WallQuad quad, decimal realWallWidthCm, decimal realWallHeightCm)
    {
        if (!quad.IsConvex()) throw new DomainException("Duvar köşeleri dışbükey bir dörtgen oluşturmalıdır.");
        if (realWallWidthCm < WallDimensions.MinSideCm || realWallHeightCm < WallDimensions.MinSideCm)
            throw new DomainException("Gerçek duvar ölçüsü en az 10 cm olmalıdır.");

        foreach (var p in quad.Corners)
        {
            if (p.X < 0 || p.Y < 0 || p.X > ImageWidthPx || p.Y > ImageHeightPx)
                throw new DomainException("Duvar köşeleri görselin içinde olmalıdır.");
        }

        (TopLeftX, TopLeftY) = (quad.TopLeft.X, quad.TopLeft.Y);
        (TopRightX, TopRightY) = (quad.TopRight.X, quad.TopRight.Y);
        (BottomRightX, BottomRightY) = (quad.BottomRight.X, quad.BottomRight.Y);
        (BottomLeftX, BottomLeftY) = (quad.BottomLeft.X, quad.BottomLeft.Y);
        RealWallWidthCm = realWallWidthCm;
        RealWallHeightCm = realWallHeightCm;
        Version++;
    }

    public void Update(string name, RoomType roomType, int sortOrder)
    {
        Name = name;
        RoomType = roomType;
        SortOrder = sortOrder;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Misafirin yüklediği oda girişte üye hesabına taşınır.</summary>
    public void SetOwner(string ownerKey)
    {
        if (OwnerKey is null) throw new DomainException("Hazır sahnelerin sahibi değiştirilemez.");
        OwnerKey = ownerKey;
    }
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

/// <summary>Misafir/üye sahiplik anahtarı üretimi. Misafirler çerezdeki rastgele anahtarla, üyeler
/// Customer.Id ile tanınır; girişte misafir verisi üyeye birleştirilir.</summary>
public static class WallOwner
{
    public static string ForCustomer(Guid customerId) => $"c:{customerId:N}";
    public static string ForGuest(string guestKey) => $"g:{guestKey}";
}

/// <summary>Sunucuda üretilen yüksek kaliteli kendi-oda render'ı / indirme kaydı - ücretsiz kota bu
/// kayıtlardan sayılır (poster değiştirip istemcide önizlemek kotadan düşmez).</summary>
public class RoomPreviewRender : BaseEntity
{
    public string OwnerKey { get; private set; } = default!;
    public Guid RoomSceneId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ResultUrl { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private RoomPreviewRender() { }

    public RoomPreviewRender(string ownerKey, Guid roomSceneId, Guid productId, string resultUrl)
    {
        OwnerKey = ownerKey;
        RoomSceneId = roomSceneId;
        ProductId = productId;
        ResultUrl = resultUrl;
    }
}
