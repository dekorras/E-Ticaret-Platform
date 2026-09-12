using Dekorras.Domain.Common;

namespace Dekorras.Domain.Content;

public class CmsPage : AuditableEntity
{
    public string Slug { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    private readonly List<CmsPageTranslation> _translations = [];
    public IReadOnlyCollection<CmsPageTranslation> Translations => _translations.AsReadOnly();

    private CmsPage() { }

    public CmsPage(string slug) => Slug = slug;

    public void UpdateSlug(string slug) => Slug = slug;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void SetTranslation(string languageCode, string title, string contentHtml, string? metaTitle, string? metaDescription)
    {
        var existing = _translations.FirstOrDefault(t => t.LanguageCode == languageCode);
        if (existing is not null)
        {
            existing.Update(title, contentHtml, metaTitle, metaDescription);
            return;
        }
        _translations.Add(new CmsPageTranslation(Id, languageCode, title, contentHtml, metaTitle, metaDescription));
    }
}

public class CmsPageTranslation : BaseEntity
{
    public Guid CmsPageId { get; private set; }
    public string LanguageCode { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string ContentHtml { get; private set; } = default!;
    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }

    private CmsPageTranslation() { }

    public CmsPageTranslation(Guid cmsPageId, string languageCode, string title, string contentHtml, string? metaTitle, string? metaDescription)
    {
        CmsPageId = cmsPageId;
        LanguageCode = languageCode;
        Title = title;
        ContentHtml = contentHtml;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
    }

    public void Update(string title, string contentHtml, string? metaTitle, string? metaDescription)
    {
        Title = title;
        ContentHtml = contentHtml;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
    }
}

public class BlogPost : AuditableEntity
{
    public string Slug { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string ContentHtml { get; private set; } = default!;
    public string LanguageCode { get; private set; } = default!;
    public DateTime? PublishedAtUtc { get; private set; }

    private BlogPost() { }

    public BlogPost(string slug, string title, string contentHtml, string languageCode)
    {
        Slug = slug;
        Title = title;
        ContentHtml = contentHtml;
        LanguageCode = languageCode;
    }

    public void Publish() => PublishedAtUtc ??= DateTime.UtcNow;
    public void Unpublish() => PublishedAtUtc = null;

    public void Update(string title, string contentHtml)
    {
        Title = title;
        ContentHtml = contentHtml;
    }
}

public class Banner : AuditableEntity
{
    public string ImageUrl { get; private set; } = default!;
    public string? LinkUrl { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Banner() { }

    public Banner(string imageUrl, string? linkUrl, int displayOrder)
    {
        ImageUrl = imageUrl;
        LinkUrl = linkUrl;
        DisplayOrder = displayOrder;
    }

    public void UpdateDetails(string? linkUrl, int displayOrder)
    {
        LinkUrl = linkUrl;
        DisplayOrder = displayOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public class MenuItem : AuditableEntity
{
    public Guid? ParentMenuItemId { get; private set; }
    public string LabelKey { get; private set; } = default!; // çeviri anahtarı
    public string? Url { get; private set; }
    public int DisplayOrder { get; private set; }

    private MenuItem() { }

    public MenuItem(string labelKey, string? url, int displayOrder, Guid? parentMenuItemId = null)
    {
        LabelKey = labelKey;
        Url = url;
        DisplayOrder = displayOrder;
        ParentMenuItemId = parentMenuItemId;
    }
}
