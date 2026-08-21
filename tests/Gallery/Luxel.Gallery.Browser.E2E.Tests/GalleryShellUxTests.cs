using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using static Microsoft.Playwright.Assertions;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class GalleryShellUxTests : PageTest
{
    [Fact]
    public async Task Japanese_shell_labels_and_theme_choice_persist_across_reload()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await Page.GotoAsync(GalleryTestHost.BaseUrl);

        await Expect(Page.GetByRole(AriaRole.Searchbox, new() { Name = "ストーリーを検索" }))
            .ToBeVisibleAsync(new() { Timeout = 90_000 });
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "ストーリーナビゲーション" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Group, new() { Name = "表示テーマ" })).ToBeVisibleAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "ja");
        await Expect(Page.GetByText("コントロールギャラリー", new() { Exact = true })).ToBeVisibleAsync();

        ILocator light = Page.GetByRole(AriaRole.Button, new() { Name = "ライト", Exact = true });
        await light.ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-gallery-theme", "light");
        Assert.Equal("light", await Page.EvaluateAsync<string>(
            "() => localStorage.getItem('luxel.gallery.shell-theme')"));
        string lightBackground = await Page.Locator("body").EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor");
        await Page.GetByRole(AriaRole.Button, new() { Name = "ダーク", Exact = true }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-gallery-color-scheme", "dark");
        string darkBackground = await Page.Locator("body").EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor");
        Assert.NotEqual(lightBackground, darkBackground);
        await light.ClickAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Searchbox, new() { Name = "ストーリーを検索" }))
            .ToBeVisibleAsync(new() { Timeout = 90_000 });
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-gallery-theme", "light");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "ライト", Exact = true }))
            .ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Fact]
    public async Task Generated_control_docs_render_an_api_table_instead_of_widget_fallback()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await Page.GotoAsync(GalleryTestHost.BaseUrl);
        await Expect(Page.GetByRole(AriaRole.Searchbox, new() { Name = "ストーリーを検索" }))
            .ToBeVisibleAsync(new() { Timeout = 90_000 });

        string[] docsPaths = await Page.Locator("a.story-link, a.folder-story-link").EvaluateAllAsync<string[]>("""
            links => links.map(link => new URL(link.href).searchParams.get('story'))
                .filter(path => path?.startsWith('Controls/') && path.endsWith('/Docs'))
            """);
        Assert.NotEmpty(docsPaths);

        await Page.GotoAsync($"{GalleryTestHost.BaseUrl}/?story={Uri.EscapeDataString(docsPaths[0])}");
        ILocator apiReference = Page.Locator(".api-reference").First;
        await Expect(apiReference).ToBeVisibleAsync(new() { Timeout = 90_000 });
        await Expect(apiReference.Locator("table.api-table")).ToBeVisibleAsync();
        await Expect(apiReference.GetByRole(AriaRole.Columnheader, new() { Name = "名前" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".markdown-embed-unavailable[data-embed-kind='ControlApiTable']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Narrow_layout_uses_keyboard_accessible_navigation_drawer()
    {
        await GalleryTestHost.EnsureStartedAsync();
        await Page.SetViewportSizeAsync(480, 800);
        await Page.GotoAsync(GalleryTestHost.BaseUrl);

        ILocator toggle = Page.Locator("#gallery-navigation-toggle");
        ILocator sidebar = Page.Locator("#gallery-navigation");
        ILocator search = Page.GetByRole(AriaRole.Searchbox, new() { Name = "ストーリーを検索" });
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 90_000 });
        await Expect(sidebar).ToBeHiddenAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");

        await toggle.ClickAsync();
        await Expect(sidebar).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(toggle).ToHaveAttributeAsync("aria-label", "ナビゲーションを閉じる");
        await GalleryPolling.EventuallyAsync(() => search.EvaluateAsync<bool>("element => element === document.activeElement"),
            message: "Opening the narrow navigation drawer did not move focus to story search.");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(sidebar).ToBeHiddenAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await GalleryPolling.EventuallyAsync(() => toggle.EvaluateAsync<bool>("element => element === document.activeElement"),
            message: "Closing the narrow navigation drawer did not return focus to its toggle.");
    }
}
