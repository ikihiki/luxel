using Luxel.Input;

// docs:begin input-actions
var bus = new InputBus();
var source = new FakeInputSource();
var gameplay = new InputContext("Gameplay");
var jump = gameplay.Add(new ButtonAction("Jump", KeyCode.Space));
var move = gameplay.Add(new Axis2DAction("Move"));
move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
var stack = new InputStack();
stack.Push(gameplay);
// docs:end input-actions

int triggered = 0;
jump.Triggered += () => triggered++;
source.PressKey(KeyCode.Space); source.PressKey(KeyCode.W); source.PressKey(KeyCode.D);
source.Poll(bus); stack.Update(bus);
Console.WriteLine($"input: jump={jump.Value.Value}, triggered={triggered}, move={move.Value.Value.X:F3},{move.Value.Value.Y:F3}");
if (!jump.Value.Value || triggered != 1 || move.Value.Value.X <= 0 || move.Value.Value.Y <= 0) return 1;
source.ReleaseKey(KeyCode.Space); source.ReleaseKey(KeyCode.W); source.ReleaseKey(KeyCode.D);
source.Poll(bus); stack.Update(bus);
return jump.Value.Value ? 2 : 0;
