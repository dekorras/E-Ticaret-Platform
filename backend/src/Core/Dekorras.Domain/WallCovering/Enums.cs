namespace Dekorras.Domain.WallCovering;

/// <summary>Mural: tek parça poster, ölçüye göre kırpılır/esnetilir. Pattern: tekrarlı desen, duvara döşenir.</summary>
public enum WallProductType
{
    Mural,
    Pattern
}

public enum RepeatType
{
    Straight,
    HalfDrop
}

/// <summary>Görselin ölçüye uydurulma şekli: Crop = oran korunur, taşan kısım kırpılır; Stretch = tüm görsel, oran bozulur.</summary>
public enum FitMode
{
    Crop,
    Stretch
}

public enum ImageFilter
{
    None,
    Grayscale,
    Sepia
}

public enum LengthUnit
{
    Cm,
    M,
    In,
    Ft
}

public enum WallAlign
{
    Left,
    Center,
    Right
}

/// <summary>Faceted katalog filtresinin etiket grupları (bkz. spec 1.2 - Tag.Group).</summary>
public enum TagGroup
{
    Color,
    Room,
    Style,
    Theme,
    Nature,
    Tone,
    Density,
    Scale,
    Light
}

public enum RoomType
{
    LivingRoom,
    Bedroom,
    KidsRoom,
    Office,
    Kitchen,
    Hallway,
    Bathroom,
    Other
}

public enum DesignRequestType
{
    OzelOlcu,
    RenkDegisikligi,
    GorselDuzenlemeKirpma,
    OgeEkleKaldir,
    Diger
}

public enum DesignRequestStatus
{
    New,
    InProgress,
    Answered,
    Closed
}

public enum ProofStatus
{
    Beklemede,
    Onaylandi,
    RevizyonIstendi
}

public enum ProductionFileStatus
{
    Pending,
    Processing,
    Ready,
    Failed
}
