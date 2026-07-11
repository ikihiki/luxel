// 矢印キーで左右移動、押していなければゆっくり右へ (KeysDown は exe が供給)
Update = (self, world, dt) =>
{
    float v = world.KeysDown.Contains("Left") ? -120f : world.KeysDown.Contains("Right") ? 120f : 30f;
    self.Pos.X += v * dt;
    if (self.Pos.X > 700f) self.Pos.X = -60f;
};
