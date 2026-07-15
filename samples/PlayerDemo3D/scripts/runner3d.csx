Update = (self, world, dt) =>
{
    self.Pos3D.X += 1.0f * dt;
    self.Pos3D.Z = 0.45f * MathF.Sin(world.Time * 3f);
};
