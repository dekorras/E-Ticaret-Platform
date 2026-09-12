namespace Dekorras.Domain.Catalog;

public enum UnitOfMeasure
{
    Piece,       // adet
    SquareMeter, // m²
    LinearMeter  // mt (metre tül)
}

public enum StockAvailability
{
    InStock,        // Stokta var
    OutOfStock,     // Stokta yok
    PreOrder,       // Ön Sipariş
    ArrivesInDays   // 2-3 gün içinde
}

public enum ProductStatus
{
    Draft,
    Active,
    Inactive
}
