using Luxel.Framework.Game;

// docs:begin framework-fixed-timestep
var timestep = new FixedTimestep(fixedDt: 0.125, maxStepsPerFrame: 3);
double[] frames = [0.0625, 0.1875, 0.625];
int updates = 0;
foreach (double frameDt in frames)
{
    int steps = timestep.Advance(frameDt);
    for (int i = 0; i < steps; i++) updates++;
}
Console.WriteLine($"framework: updates={updates}, total={timestep.TotalSteps}, dropped={timestep.DroppedSteps}, alpha={timestep.Alpha:F2}");
// docs:end framework-fixed-timestep

return updates == 5 && timestep.TotalSteps == 5 && timestep.DroppedSteps == 2 ? 0 : 1;
