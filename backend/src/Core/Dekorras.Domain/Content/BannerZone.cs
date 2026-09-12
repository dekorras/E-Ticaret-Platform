using Dekorras.Domain.Common;

namespace Dekorras.Domain.Content;

public class BannerZone : AuditableEntity
{
    public string Key { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;

    private BannerZone() { }

    public BannerZone(string key, string name, string? description)
    {
        Key = key;
        Name = name;
        Description = description;
    }

    public void UpdateDetails(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public enum BannerNodeType
{
    Row = 0,
    Column = 1
}

public class BannerNode : AuditableEntity
{
    public Guid BannerZoneId { get; private set; }

    public Guid? ParentId { get; private set; }
    public BannerNode? Parent { get; private set; }
    private readonly List<BannerNode> _children = [];
    public IReadOnlyCollection<BannerNode> Children => _children.AsReadOnly();

    public BannerNodeType NodeType { get; private set; }
    public int SortOrder { get; private set; }
    public int Depth { get; private set; }

    /// <summary>
    /// Materyalize edilmiş yol - kökten bu düğüme kadar Guid'lerin "/" ile ayrılmış hali
    /// (ör. "/{kökId}/{araId}/{buId}/"). Bir düğümü kendi alt ağacına taşımayı Application
    /// katmanında `newParentPath.StartsWith(movingNode.Path)` ile tek sorguda engellemek için var -
    /// Category'de böyle bir kontrol yok çünkü kategori taşıma Admin'de tekil seçim ile yapılıyor,
    /// burada ise sürükle-bırak sırasında herhangi bir düğüm hedef olabiliyor.
    /// </summary>
    public string Path { get; private set; } = default!;

    public bool IsActive { get; private set; } = true;
    public string? CustomCssClass { get; private set; }
    public string? CustomId { get; private set; }

    /// <summary>Düz nvarchar(max) - AuditLog.DetailsJson ile aynı desen, attribute yok.</summary>
    public string? SettingsJson { get; private set; }

    private readonly List<BannerContent> _contents = [];
    public IReadOnlyCollection<BannerContent> Contents => _contents.AsReadOnly();

    private BannerNode() { }

    public BannerNode(Guid bannerZoneId, Guid? parentId, BannerNodeType nodeType, int sortOrder, int depth, string path)
    {
        BannerZoneId = bannerZoneId;
        ParentId = parentId;
        NodeType = nodeType;
        SortOrder = sortOrder;
        Depth = depth;
        Path = path;
    }

    public void Reparent(Guid? newParentId, string newPath, int newDepth, int newSortOrder)
    {
        ParentId = newParentId;
        Path = newPath;
        Depth = newDepth;
        SortOrder = newSortOrder;
    }

    public void Reorder(int sortOrder) => SortOrder = sortOrder;
    public void SetSettings(string? settingsJson) => SettingsJson = settingsJson;
    public void SetCustomAttributes(string? customCssClass, string? customId)
    {
        CustomCssClass = customCssClass;
        CustomId = customId;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public enum BannerContentType
{
    Image = 0,
    ImageWithOverlay = 1,
    Heading = 2,
    Text = 3,
    Button = 4,
    LinkList = 5,
    Video = 6,
    RawHtml = 7,
    Spacer = 8,
    ProductWidget = 9
}

public class BannerContent : AuditableEntity
{
    public Guid BannerNodeId { get; private set; }
    public BannerContentType ContentType { get; private set; }
    public int SortOrder { get; private set; }

    public string? Title { get; private set; }
    public string? Subtitle { get; private set; }
    public string? Body { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? ImageUrlMobile { get; private set; }
    public string? AltText { get; private set; }
    public string? LinkUrl { get; private set; }
    public string? LinkTarget { get; private set; }
    public string? ButtonText { get; private set; }

    /// <summary>Düz nvarchar(max) - AuditLog.DetailsJson ile aynı desen, attribute yok.</summary>
    public string? SettingsJson { get; private set; }

    public DateTime? StartDateUtc { get; private set; }
    public DateTime? EndDateUtc { get; private set; }
    public bool IsActive { get; private set; } = true;

    private BannerContent() { }

    public BannerContent(Guid bannerNodeId, BannerContentType contentType, int sortOrder)
    {
        BannerNodeId = bannerNodeId;
        ContentType = contentType;
        SortOrder = sortOrder;
    }

    public void UpdateText(string? title, string? subtitle, string? body, string? buttonText)
    {
        Title = title;
        Subtitle = subtitle;
        Body = body;
        ButtonText = buttonText;
    }

    public void UpdateImage(string? imageUrl, string? imageUrlMobile, string? altText)
    {
        ImageUrl = imageUrl;
        ImageUrlMobile = imageUrlMobile;
        AltText = altText;
    }

    public void UpdateLink(string? linkUrl, string? linkTarget)
    {
        LinkUrl = linkUrl;
        LinkTarget = linkTarget;
    }

    public void SetSettings(string? settingsJson) => SettingsJson = settingsJson;
    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    public void Schedule(DateTime? startDateUtc, DateTime? endDateUtc)
    {
        StartDateUtc = startDateUtc;
        EndDateUtc = endDateUtc;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
