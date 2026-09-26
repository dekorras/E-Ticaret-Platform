using System.Text.Encodings.Web;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.FeatureManagement;

namespace Dekorras.Storefront.WallCovering;

/// <summary>Tüm sayfalarda tek yerden yönetilen "Duvarında Gör" bağlantısı (spec 1.6.6-A):
/// <c>&lt;wall-preview-link product="@slug" config="@config" variant="button|icon|text" source="kart" /&gt;</c>.
/// Gerçek bir &lt;a href&gt; üretir (JS olmadan tam sayfa açılır); wall-preview-link.js tıklamayı
/// yakalayıp modalda açar. `WallPreviewLink` özellik bayrağı kapalıysa hiçbir şey render edilmez.</summary>
[HtmlTargetElement("wall-preview-link", TagStructure = TagStructure.WithoutEndTag)]
public sealed class WallPreviewLinkTagHelper(IFeatureManagerSnapshot featureManager) : TagHelper
{
    public string Product { get; set; } = "";
    public WallConfiguration? Config { get; set; }
    public string Variant { get; set; } = "button";
    public string? Source { get; set; }
    public Guid? Scene { get; set; }
    public string? ReturnUrl { get; set; }
    public string? CssClass { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Product) || !await featureManager.IsEnabledAsync(WallFeatures.WallPreviewLink))
        {
            output.SuppressOutput();
            return;
        }

        var href = WallPreviewUrl.Build(Product, Config, Scene, null, ReturnUrl, Source);
        var variant = Variant is "icon" or "text" ? Variant : "button";
        var css = variant switch
        {
            "icon" => "btn btn-light btn-sm btn-wall-preview btn-wall-preview-icon",
            "text" => "btn-wall-preview btn-wall-preview-text",
            _ => "btn btn-outline-dark btn-wall-preview"
        };

        output.TagName = "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("href", href);
        output.Attributes.SetAttribute("class", string.IsNullOrWhiteSpace(CssClass) ? css : $"{css} {CssClass}");
        output.Attributes.SetAttribute("data-wall-preview", Product);
        if (!string.IsNullOrEmpty(Source)) output.Attributes.SetAttribute("data-wall-preview-source", Source);

        const string icon = "<i class=\"bi bi-house-door\" aria-hidden=\"true\"></i>";
        if (variant == "icon")
        {
            output.Attributes.SetAttribute("title", "Duvarında Gör");
            output.Attributes.SetAttribute("aria-label", "Duvarında Gör");
            output.Content.SetHtmlContent(icon);
        }
        else
        {
            output.Content.SetHtmlContent($"{icon} <span>{HtmlEncoder.Default.Encode("Duvarında Gör")}</span>");
        }
    }
}
