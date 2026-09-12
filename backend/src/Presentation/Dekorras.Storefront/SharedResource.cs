namespace Dekorras.Storefront;

/// <summary>IStringLocalizer&lt;SharedResource&gt; için işaretçi sınıf - gerçek metinler
/// Resources/SharedResource.{dil}.resx dosyalarında. Anahtar YOK, Türkçe metnin kendisi anahtar
/// olarak kullanılıyor (varsayılan/nötr bir .resx dosyası kasıtlı olarak YOK) - böylece Türkçe için
/// hiçbir çeviri dosyası eşleşmediğinde IStringLocalizer anahtarı (zaten Türkçe metin) olduğu gibi
/// döndürür.</summary>
public sealed class SharedResource;
