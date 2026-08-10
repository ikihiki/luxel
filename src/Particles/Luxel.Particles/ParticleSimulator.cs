namespace Luxel.Particles;

/// <summary>1 ステップの積分器。バッファ (SoA) と分離してあり、v1 は CPU 実装のみ。
/// 将来 GPU compute シミュレーションを同じ外部 API の下で差し込めるようにするための seam
/// (公開面に CPU 読み戻し前提の API は置かない)。</summary>
public interface IParticleSimulator
{
    /// <summary>速度/重力/抗力を積分し、寿命切れを前方詰めで除去する (<see cref="ParticleBuffer.Count"/> を更新)。</summary>
    void Step(ParticleBuffer buffer, in ParticleConfig config, float dt);
}

/// <summary>CPU 積分器 (オイラー法)。死亡パーティクルを発生順を保ったまま前方詰めで除去する。</summary>
public sealed class CpuParticleSimulator : IParticleSimulator
{
    public void Step(ParticleBuffer b, in ParticleConfig cfg, float dt)
    {
        float gravity = cfg.Gravity;
        float dragF = cfg.Drag != 0f ? MathF.Max(0f, 1f - cfg.Drag * dt) : 1f;

        int w = 0;
        for (int r = 0; r < b.Count; r++)
        {
            float age = b.Age[r] + dt;
            if (age >= b.LifeMax[r]) continue;   // 死亡 → 詰めない (除去)

            float vx = b.VelX[r];
            float vy = b.VelY[r] + gravity * dt;
            float vz = b.VelZ[r];
            if (dragF != 1f) { vx *= dragF; vy *= dragF; vz *= dragF; }

            // 前方詰め (w ≤ r なので同配列内コピーで安全)
            b.PosX[w] = b.PosX[r] + vx * dt;
            b.PosY[w] = b.PosY[r] + vy * dt;
            b.PosZ[w] = b.PosZ[r] + vz * dt;
            b.VelX[w] = vx;
            b.VelY[w] = vy;
            b.VelZ[w] = vz;
            b.Age[w] = age;
            b.LifeMax[w] = b.LifeMax[r];
            b.Size[w] = b.Size[r];
            b.Tint[w] = b.Tint[r];
            w++;
        }
        b.Count = w;
    }
}
