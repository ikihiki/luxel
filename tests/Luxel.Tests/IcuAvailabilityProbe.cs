using Luxel.Typography;
using Xunit;

namespace Luxel.Tests;

public class IcuAvailabilityProbe
{
    [Fact]
    public void WriteAvailability()
    {
        // 検証環境での可否をファイルに書き出す (E2E 確認用の一時プローブ)
        string info = IcuSegmenter.IsAvailable
            ? "available: " + Icu.Wrapper.IcuVersion
            : "unavailable: " + IcuSegmenter.UnavailableReason;
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "luxel_icu_probe.txt"), info);
    }
}
