using Forp.Game;
using Sandbox;
using Sandbox.Diagnostics;
using System;

namespace Forp.Object.Unit;

public static class ObjectUnitManager
{
	public static void AttackUnits(Hex Attacker, Hex Target)
	{
		Assert.IsValid(Attacker);
		Assert.IsValid(Target);

		if (!Attacker.UnitObject.IsValid() || !Target.UnitObject.IsValid())
		{
			Log.Warning($"trying to call AttackUnits while a unit is not valid");
			return;
		}

		Target.UnitObject.Health -= Attacker.UnitObject.Attack;

		if (Target.UnitObject.Health <= 0)
		{
			Log.Info("HOW MANY SHOTS DOES IT TAKE??");
		}
	}
}
