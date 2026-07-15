Update = (self, world, dt) =>
{
    if (world.Time > 0.20f) world.RequestScene("res://scenes/arena.scene.json");
};
