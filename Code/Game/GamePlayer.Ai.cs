using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox.Diagnostics;
using System;

namespace Forp.Game;

public sealed partial class GamePlayer : Component
{
	[Property] public bool IsAi { get; set; } = false;
}
