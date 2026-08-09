using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("Internals/ADR/0020-Resource-System-Builder-Domains-And-Managers", Order = 91, Toc = true)]
    public static StoryResult Adr0020(StoryContext ctx) => $$"""
        # ADR-0020 — ResourceSystemをBuilder・実行Domain・型別Managerで構成する

        - **Status**: Accepted
        - **Date**: 2026-08-09
        - **Deciders**: ikihiki
        - **Learn**: [Resources学習ガイド](story:Learn/Resources/Overview)

        ## Context

        ### History

        Resource pipeline は `Executor.Io / Cpu / External` と3本の固定laneから始まりました。実装が育つにつれて `ResourceSystem` は型付きURIのcacheと依存graphだけでなく、scheduler、ownership、reload publication、GPU deferred disposal、diagnosticsまでを直接担当するようになりました。

        browser-WASMではlane awaitを完了扱いにするinline fallbackが追加されましたが、この経路はqueue順序、公平性、待機時間の計測、lock外dispatchというschedulerの契約を保てません。GPU側でも単一idle hook、pump flush callback、固定device/registryのcaptureへ機能を追加したため、複数manager・複数device・device lost後の世代交換を表現しにくくなりました。

        実行場所はCPU/GPUという種類だけでは決まりません。applicationはdecode、shader compile、host thread、device create、transferなどを別々のaffinity・並列度・budgetで構成する必要があります。また、値の破棄方法、allocation、index、fence、device generationは論理nodeではなく、その値を生成した世代に属します。

        この変更は登録と所有権の意味を変えます。二重API期間を設けると、構築後に変更可能な旧モデルとimmutableな新モデルが同居して検証不能になるため、repository内のcallerを同時に移行する必要があります。

        ### Forces and constraints

        - `(exact output Type, ResourceUri.Key)`、`ResourceHandle<T>`、`ResourceScope`、依存DAG、last-good value、世代cancel、`Pump`での公開は維持する
        - domain名と個数はpackage/applicationが決め、coreに固定一覧を置かない
        - 完成したsystemは最初の`Load<T>()`からreadyで、登録構造が不変である
        - `Luxel.Resources`はGPU、Graphics backend、MessagePipeを参照しない
        - 非threaded WASMでもqueue、cancel、metrics、公平性、owner-context affinityを守る
        - device-loss callback内では復旧せず、frame/Pump threadで順序制御する

        ## Decision

        `ResourceSystemBuilder.BuildAsync()`を標準のcomposition境界とします。domain、source、step、managerを構築前にbuilderへ登録し、全descriptorを検証してからcomponentを生成・startし、ready barrierを通過したimmutableな`ResourceSystem`だけを返します。途中で失敗した場合は生成済みcomponentを逆順に破棄します。同期`Build()`は完全に同期初期化できる構成だけを受け付けます。

        coreに固定domain一覧や独立した公開registry serviceは置きません。domain builderが返すhandleをStep builderの`.RunOn(...)`とmanager構成へ渡し、完成後は`ResourceSystem`がdomain/source/step/managerのimmutable tableを直接所有します。Step実装は実行場所を宣言せず、同じStep typeを異なるdomainへ登録できます。

        `Luxel.Resources`が実装として提供するmanagerはI/O用とCPU用だけです。値はgeneration単位の管理記録を持ち、公開後の旧generation、stale completion、eviction、shutdownを、その値を採用したmanagerへretireします。

        `Luxel.AssetsGpu`は汎用`GpuResourceManager`、GPU domain factory、memory/index/fence retirement、device generationを所有します。組込みAsset型と任意のユーザー定義GPU struct/classはtyped policyで明示登録し、Asset継承、marker interface、`IDisposable`を要求しません。GPU managerへbindしたStepの出力型にpolicyがなければbuildを失敗させます。

        非threaded WASMはbrowser owner context上のserialized/cooperative schedulerを使います。各論理domainはqueueを保ち、effective concurrency 1、FIFO、cancel、metrics、公平なyieldを報告します。

        Graphics backendはtransport-neutralなlifecycle sinkへimmutable eventを通知します。専用adapterがtyped MessagePipe messageへ変換し、subscriberはframe-thread queueへcommandを積みます。device lost recoveryは既に構築済みのGPU manager/domainをpauseし、device generationを交換して対象managerだけをinvalidateします。

        compatibility adapter、legacy constructor、旧名alias、構築後の`AddDomain` / `AddSource` / `AddStep` / `AddManager`は設けません。

        ## Alternatives

        - **`Executor` enumへ値を追加し続ける** — application固有のaffinity、複数device、scheduler capabilityをcoreの固定分類へ押し込み続けるため却下
        - **独立domain registry serviceをDIする** — builder transaction外で登録順とready状態が分裂し、完成時検証とrollbackが弱くなるため却下
        - **ResourceSystem生成後にdomainやStepを動的登録する** — immutable planと曖昧性検証を壊す。pluginは別systemを構築して明示的に切り替える
        - **Assets組込みGPU型だけをGPU managerで扱う** — application独自のbuffer集合やdescriptor構造を同じbudget・retirement・recoveryへ参加させられないため却下
        - **旧API wrapperを恒久的に残す** — ownershipとdomainの二つの意味論が併存し、誤構成をbuild時に拒否できないため却下
        - **device lost時にResourceSystem全体を捨てる** — CPU/I/O cache、handle identity、last-good valueまで不要に失い、borrowed device policyも扱いにくいため却下

        ## Consequences

        - ✅ package/applicationは任意数のdomainを名前、affinity、並列度、budgetとともに構成できる
        - ✅ source、step、manager bindingの欠落・重複・曖昧性を最初のload前に検出できる
        - ✅ generationごとのmanager記録により、reloadとdevice交換をまたいで正しい所有者が値をretireできる
        - ✅ GPU組込み型とユーザー定義型が同じmemory/index/fence/diagnostics基盤へ参加できる
        - ✅ browser-WASMでもnativeと同じdispatch境界と観測可能性を保てる
        - ✅ device lostはGPU managerだけを対象に復旧でき、論理Resource identityを維持できる
        - ⚠️ APIはbreaking changeとなり、全caller、sample、Story、testを同時に移行する必要がある
        - ⚠️ builder validation、ready barrier、失敗時rollback、shutdown順の実装とテストが増える
        - ⚠️ cooperative schedulerで長い同期Stepをpreemptできないため、chunk化可能なStepは明示的にyieldする必要がある
        - ⚠️ GPU型ごとのtyped policy、memory/index metrics、fence-safe retirement、device recoveryの検証負担を各integrationが引き受ける
        """;
}
