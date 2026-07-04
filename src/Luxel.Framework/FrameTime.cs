namespace Luxel.Framework;

/// <summary>frame 時間情報。System が per-frame delta やフレーム番号を参照するために使う。</summary>
public readonly record struct FrameTime(
    long Frame,
    float DeltaSeconds,
    double TotalSeconds);
