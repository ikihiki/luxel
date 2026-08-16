using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

/// <summary>成果物を順番に作りながらLuxel Galleryの基本を学ぶチュートリアル。</summary>
[StoryMeta("Tutorials/Gallery")]
public static partial class GalleryTutorialStories
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Galleryの作り方

        {{Toc()}}

        このチュートリアルでは、既存の`*.Gallery`プロジェクトへ最初のStoryを追加し、操作できる状態とMarkdown説明を加えます。最後に、独立したGalleryライブラリをホストへ登録し、Native版とBlazor版で同じカタログを表示します。

        ## 完了するとできること

        - `StoryResult`を返すメソッドをStoryとして登録できる
        - `StoryContext`で状態、引数、Outputログを公開できる
        - Markdownページへ同じクラスのサンプルを埋め込める
        - 新しいGalleryライブラリを明示的にホストへ登録できる
        - ビルドとテストで登録漏れやリンク切れを確認できる

        ## 前提

        .NET 10 SDKと、このリポジトリをビルドできる環境を使います。最初は新しいプロジェクトを作らず、対象ライブラリに対応する既存の`Luxel.*.Gallery`プロジェクトで試すのが最短です。

        ## 学習順

        1. [最初のStory](story:Tutorials/Gallery/FirstStory)
        2. [操作できるStory](story:Tutorials/Gallery/InteractiveStory)
        3. [Markdownページ](story:Tutorials/Gallery/MarkdownPage)
        4. [Galleryライブラリの追加](story:Tutorials/Gallery/GalleryLibrary)
        5. [検証と実行](story:Tutorials/Gallery/Verify)
        """;

    [Story]
    public static StoryResult FirstStory(StoryContext ctx) => $$"""
        # 最初のStoryを作る

        {{Toc()}}

        このページでは、ボタンを一つ表示するStoryを既存のGalleryプロジェクトへ追加します。Storyは`StoryResult`へ暗黙変換できるUIを返せますが、メソッドの戻り値は常に`StoryResult`と宣言します。

        ## 1. 配置先を選ぶ

        製品ライブラリではなく、対応する`Luxel.*.Gallery`プロジェクトの`Stories`ディレクトリへ置きます。UIのStoryなら`src/UI/Luxel.UI.Gallery/Stories`が配置先です。製品コードはGalleryへ依存させません。

        ## 2. Storyクラスを書く

        ```csharp
        using static Luxel.Controls.Kit;

        namespace MyProduct.Gallery.Stories;

        [StoryMeta("Examples/MyProduct")]
        public static class WelcomeStories
        {
            [Story]
            public static StoryResult Welcome()
                => Frame(Button(_ => { }, "Hello Luxel"));
        }
        ```

        `StoryMeta`がサイドバーのグループ、関数名がStory名になります。この例のパスは`Examples/MyProduct/Welcome`です。パスはリンクやテストから参照されるIDなので、公開後の変更は参照も一緒に更新します。

        ## 3. Sourceを確認する

        ビルドするとソースジェネレーターがStoryを登録します。GalleryでStoryを開き、Sourceに属性、シグネチャ、本体が表示されることを確認してください。手動のリフレクション登録は不要です。

        次は[操作できるStory](story:Tutorials/Gallery/InteractiveStory)で状態とログを加えます。
        """;

    [Story]
    public static StoryResult InteractiveStory(StoryContext ctx) => $$"""
        # 操作できるStoryを作る

        {{Toc()}}

        `StoryContext`はStoryごとの状態とGalleryの出力先を提供します。下のサンプルでは、ボタンを押すとカウントが変わり、Outputへ操作内容が追加されます。

        {{StoryRef("Tutorials/Gallery/CounterSample", knobs: true)}}

        ## 状態を公開する

        `ctx.Signal(name, initial, description)`で作った値はStoryの状態になり、対応している型はArgsから変更できます。Widgetを再構築するための独自グローバル状態を持たせず、Storyのインスタンスへ閉じ込めます。

        ## 操作結果を記録する

        イベントハンドラーから`ctx.Log(message)`を呼ぶとOutputへ表示されます。クリック、選択、読み込み完了など、見た目だけでは判断できない結果を記録してください。

        ## サンプルを読みやすく保つ

        Storyメソッドには、利用者が理解するために必要な構築コードを残します。長いアルゴリズムや製品実装は製品ライブラリへ置けますが、別のStoryメソッドへ一行で委譲するとSourceが教材にならないため避けます。

        次は[Markdownページ](story:Tutorials/Gallery/MarkdownPage)へ進みます。
        """;

    [Story]
    public static StoryResult MarkdownPage(StoryContext ctx) => $$$""""
        # Markdownページを書く

        {{{Toc()}}}

        説明ページも`StoryResult`を返すStoryです。Markdownを補間文字列で返し、`Toc()`と`StoryRef()`を必要な位置へ埋め込みます。

        ## ページとサンプルを同じクラスに置く

        ページと、そのページだけが使うサンプルは同じpartialクラスへまとめます。ファイルは説明用の`.cs`と実装用の`.Samples.cs`に分けても、C#上は同じクラスです。

        ```csharp
        using static Luxel.Gallery.Story;

        [StoryMeta("Learn/MyProduct")]
        public static partial class LearnMyProduct
        {
            [Story]
            public static StoryResult Overview(StoryContext ctx) => $$"""
                # MyProduct

                {{Toc()}}

                この機能が解決する問題を最初に説明します。

                {{StoryRef("Learn/MyProduct/BasicSample")}}
                """;

            [Story]
            public static StoryResult BasicSample(StoryContext ctx)
            {
                // Sourceで読ませたいサンプル本体をここへ置く。
                return BuildPreview(ctx);
            }
        }
        ```

        ## 一ページの基本構成

        1. このページで達成すること
        2. 前提条件と対応環境
        3. 実行できる最小サンプル
        4. 重要なコードの説明
        5. よくある失敗と診断方法
        6. 次に読むページ

        Markdownのhole記法、表、数式、図、画像の詳細は[Authoring reference](story:Internals/Authoring)を参照してください。

        次は[Galleryライブラリの追加](story:Tutorials/Gallery/GalleryLibrary)へ進みます。
        """";

    [Story]
    public static StoryResult GalleryLibrary(StoryContext ctx) => $$"""
        # Galleryライブラリを追加する

        {{Toc()}}

        新しい機能群を独立させる場合は、製品ライブラリと別に`Luxel.MyProduct.Gallery`を作ります。GalleryライブラリはStoryを所有し、Native版とBlazor版のホストが明示的に組み込みます。

        ## 1. プロジェクトの役割を宣言する

        ```xml
        <PropertyGroup>
          <LuxelProjectRole>GalleryCategory</LuxelProjectRole>
          <LuxelGalleryCategory>MyProduct</LuxelGalleryCategory>
          <LuxelPlatform>Browser</LuxelPlatform>
          <LuxelGalleryCompatibility>Browser;Native</LuxelGalleryCompatibility>
          <LuxelGalleryRegistrationIdentity>MyProduct.Base</LuxelGalleryRegistrationIdentity>
        </PropertyGroup>
        ```

        製品ライブラリ、`Luxel.Gallery`、必要なUIライブラリを`ProjectReference`で参照し、`Luxel.Gallery.Generators`をAnalyzerとして追加します。ネイティブAPIだけを使うStoryは、Browser対応の基底GalleryとNative拡張Galleryを分けます。

        ## 2. カテゴリの登録入口を作る

        ```csharp
        public static class MyProductGalleryProject
        {
            public static StoryOwnership Ownership { get; }
                = StoryOwnership.BrowserSafe("MyProduct", "MyProduct.Base");

            public static IServiceCollection AddMyProductGallery(this IServiceCollection services)
                => services.AddStoryCatalog(Register);

            public static void Register(StoryCatalogBuilder builder)
            {
                using IDisposable ownership = builder.BeginOwnership(Ownership);
                Luxel.Gallery.Generated.StoryRegistration_Luxel_MyProduct_Gallery.Register(builder);
            }
        }
        ```

        登録クラス名は生成されたアセンブリ識別子に合わせます。既存カテゴリの実装例として`UiGalleryProject`や`InputGalleryProject`を参照してください。

        ## 3. ホストへ追加する

        Blazorホストでは`builder.Services.AddMyProductGallery()`を呼びます。Native集約ホストではカテゴリの`Register`を`GalleryStoryProject`へ追加します。参照を追加しただけではカタログへ表示されないため、両ホストの登録を確認してください。

        次は[検証と実行](story:Tutorials/Gallery/Verify)へ進みます。
        """;

    [Story]
    public static StoryResult Verify(StoryContext ctx) => $$"""
        # 検証してGalleryを起動する

        {{Toc()}}

        ## ビルドする

        ```powershell
        dotnet build gallery/GalleryBrowser/GalleryBrowser.csproj --no-restore
        dotnet build gallery/GalleryNative/GalleryNative.csproj --no-restore
        ```

        ソースジェネレーターの診断、重複パス、Storyの不正な戻り値、ホスト登録のコンパイルエラーをここで確認します。

        ## テストする

        ```powershell
        dotnet test tests/Gallery/Luxel.Gallery.Generators.Tests/Luxel.Gallery.Generators.Tests.csproj --no-restore
        dotnet test tests/Gallery/Luxel.Gallery.Site.Tests/Luxel.Gallery.Site.Tests.csproj --no-restore
        ```

        テストはStory本文の文言や頻繁に変わるサンプル一覧へ依存させません。ジェネレーターの契約、カタログのライフサイクル、リンク解決など、Gallery基盤の安定した規則を検証します。

        ## 起動して確認する

        ```powershell
        dotnet run --project gallery/GalleryBrowser
        dotnet run --project gallery/GalleryNative -- vk
        ```

        最後に、サイドバーのパス、Argsの再編集、Output、Source、テーマ切り替え、Markdown内の埋め込みを確認します。これでGallery作成の基本手順は完了です。

        より詳しいStory規約と回帰テストは[Gallery internals](story:Internals/Gallery)と[Contributing](story:Internals/Contributing)を参照してください。
        """;
}
