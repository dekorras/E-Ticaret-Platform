using Dekorras.Domain.Common;

namespace Dekorras.Domain.Catalog;

public class Category : AuditableEntity
{
    public Guid? ParentCategoryId { get; private set; }
    public Category? ParentCategory { get; private set; }
    private readonly List<Category> _children = [];
    public IReadOnlyCollection<Category> Children => _children.AsReadOnly();

    public string Slug { get; private set; } = default!;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? ImageUrl { get; private set; }

    /// <summary>Anasayfada, bu kategoriye özel bir ürün bloğu ("modül") gösterilsin mi? Bkz.
    /// GetFeaturedHomepageCategoriesQuery - anasayfa artık TÜM ürünleri filtresiz göstermek yerine
    /// yalnızca bu bayrağı taşıyan kategorilerin bloklarını render eder.</summary>
    public bool IsFeaturedOnHomepage { get; private set; }

    private readonly List<CategoryTranslation> _translations = [];
    public IReadOnlyCollection<CategoryTranslation> Translations => _translations.AsReadOnly();

    private Category() { }

    public Category(string slug, Guid? parentCategoryId, int displayOrder)
    {
        Slug = slug;
        ParentCategoryId = parentCategoryId;
        DisplayOrder = displayOrder;
    }

    public void SetTranslation(string languageCode, string name, string? description, string? metaTitle, string? metaDescription)
    {
        var existing = _translations.FirstOrDefault(t => t.LanguageCode == languageCode);
        if (existing is not null)
        {
            existing.Update(name, description, metaTitle, metaDescription);
            return;
        }
        _translations.Add(new CategoryTranslation(Id, languageCode, name, description, metaTitle, metaDescription));
    }

    public void MoveTo(Guid? newParentCategoryId) => ParentCategoryId = newParentCategoryId;
    public void UpdateSlug(string slug) => Slug = slug;
    public void Reorder(int displayOrder) => DisplayOrder = displayOrder;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void SetImage(string? imageUrl) => ImageUrl = imageUrl;
    public void SetFeaturedOnHomepage(bool isFeatured) => IsFeaturedOnHomepage = isFeatured;
}

public class CategoryTranslation : BaseEntity
{
    public Guid CategoryId { get; private set; }
    public string LanguageCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }

    private CategoryTranslation() { }

    public CategoryTranslation(Guid categoryId, string languageCode, string name, string? description, string? metaTitle, string? metaDescription)
    {
        CategoryId = categoryId;
        LanguageCode = languageCode;
        Name = name;
        Description = description;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
    }

    public void Update(string name, string? description, string? metaTitle, string? metaDescription)
    {
        Name = name;
        Description = description;
        MetaTitle = metaTitle;
        MetaDescription = metaDescription;
    }
}
