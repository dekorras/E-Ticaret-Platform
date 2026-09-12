using Dekorras.Domain.Common;

namespace Dekorras.Domain.Localization;

public class Language : AuditableEntity
{
    public string Code { get; private set; } = default!; // ISO 639-1: tr, en, de, fr, nl, es, ar
    public string NativeName { get; private set; } = default!;
    public bool IsRightToLeft { get; private set; }
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }

    private Language() { }

    public Language(string code, string nativeName, bool isRightToLeft, int displayOrder)
    {
        Code = code;
        NativeName = nativeName;
        IsRightToLeft = isRightToLeft;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
