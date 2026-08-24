using System.Globalization;
using System.Text;
using Luxel.Gallery.Presentation;

namespace GalleryBrowser;

/// <summary>Translates shared Gallery semantic tokens into Browser CSS custom properties.</summary>
internal static class GalleryChromeCss
{
    public static string RenderStyleSheet()
    {
        var css = new StringBuilder(2048);
        AppendRule(css, ":root", GalleryChromeTokens.Light);
        AppendRule(css, "html[data-gallery-color-scheme=\"dark\"]", GalleryChromeTokens.Dark);
        return css.ToString();
    }

    private static void AppendRule(StringBuilder css, string selector, GalleryChromeTokens tokens)
    {
        css.Append(selector).Append('{');
        Color("background", tokens.Background);
        Color("surface", tokens.Surface);
        Color("elevated-surface", tokens.ElevatedSurface);
        Color("border", tokens.Border);
        Color("divider", tokens.Divider);
        Color("primary", tokens.Primary);
        Color("selected", tokens.Selected);
        Color("hover", tokens.Hover);
        Color("pressed", tokens.Pressed);
        Color("focus", tokens.Focus);
        Color("text", tokens.Text);
        Color("muted-text", tokens.MutedText);
        Color("subtle-text", tokens.SubtleText);
        Color("inverse-text", tokens.InverseText);
        Color("error", tokens.Error);
        Color("warning", tokens.Warning);
        Color("success", tokens.Success);
        Color("code-surface", tokens.CodeSurface);
        Color("args-editor-surface", tokens.ArgsEditorSurface);
        Number("body-font-size", tokens.BodyFontSize, "px");
        Number("supporting-font-size", tokens.SupportingFontSize, "px");
        Number("code-font-size", tokens.CodeFontSize, "px");
        Number("navigation-font-size", tokens.NavigationFontSize, "px");
        Number("heading-font-size", tokens.HeadingFontSize, "px");
        Number("body-line-height", tokens.BodyLineHeight, string.Empty);
        Number("spacing-unit", tokens.SpacingUnit, "px");
        Number("radius", tokens.Radius, "px");
        Number("large-radius", tokens.LargeRadius, "px");
        Number("toolbar-height", tokens.ToolbarHeight, "px");
        Number("sidebar-width", tokens.SidebarWidth, "px");
        Number("panel-minimum-size", tokens.PanelMinimumSize, "px");
        Number("docs-maximum-width", tokens.DocsMaximumWidth, "px");
        css.Append('}');
        return;

        void Color(string name, GalleryColor color)
            => css.Append("--gallery-").Append(name).Append(':').Append(color.ToCssHex()).Append(';');

        void Number(string name, float value, string unit)
            => css.Append("--gallery-").Append(name).Append(':')
                .Append(value.ToString("0.###", CultureInfo.InvariantCulture)).Append(unit).Append(';');
    }
}
