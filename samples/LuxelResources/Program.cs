using System.Text;
using Luxel.Resources;

// docs:begin resource-pipeline
var files = new MemoryFileSystem();
files.Set("hello.txt", Encoding.UTF8.GetBytes("hello resources"));
using var resources = new ResourceSystem(
    sources: [new FileSource(files)],
    steps: [new Utf8TextStep()]);
using ResourceHandle<TextAsset> handle = resources.Load<TextAsset>("hello.txt");
await handle.Ready;
// docs:end resource-pipeline
Console.WriteLine($"resources: status={handle.Status}, value={handle.Value.Text}, version={handle.Version}");
return handle.IsReady && handle.Value.Text == "HELLO RESOURCES" ? 0 : 1;

sealed record TextAsset(string Text);
sealed class Utf8TextStep : IResourceStep<byte[], TextAsset>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".txt"];
    public Task<TextAsset> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
        => Task.FromResult(new TextAsset(Encoding.UTF8.GetString(input).ToUpperInvariant()));
}
