using System;
using XingShuRuntime.Utilities;
class AssistBallPolicyTests
{
	static int checks;
	static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
	static void Main()
	{
		Check(AssistBallPolicy.Size(44, true, true) == 64, "upgrade previous mobile default");
		Check(AssistBallPolicy.Size(64, true, true) == 64, "keep compact default on mobile");
		Check(AssistBallPolicy.Size(36, true, false) == 48, "mobile minimum stays touchable");
		Check(AssistBallPolicy.Size(110, true, true) == 110, "keep custom size");
		Check(AssistBallPolicy.Size(48, true, false) == 48, "keep edited minimum after upgrade");
		Check(AssistBallPolicy.Size(float.NaN, true, false) == 64, "invalid size fallback");
		Check(AssistBallPolicy.Size(400, true, false) == 200, "upper size bound");
		Check(AssistBallPolicy.Size(44, false, true) == 44, "desktop size unchanged");
		Check(!AssistBallPolicy.ShouldHide(true,true,true,false,true,false,1.9f,0), "touch idle grace");
		Check(AssistBallPolicy.ShouldHide(true,true,true,false,true,false,2,0), "stale hover after release cannot block touch collapse");
		Check(!AssistBallPolicy.ShouldHide(true,true,true,true,false,false,100,0), "drag remains visible");
		Check(!AssistBallPolicy.ShouldHide(true,true,true,false,false,true,100,0), "open menu remains visible");
		Check(!AssistBallPolicy.ShouldHide(false,true,true,false,false,false,100,0), "disabled setting respected");
		Check(!AssistBallPolicy.ShouldHide(true,false,true,false,false,false,100,0), "no dock no collapse");
		Check(!AssistBallPolicy.ShouldHide(true,true,false,false,true,false,100,0), "desktop hover remains visible");
		Check(AssistBallPolicy.ShouldHide(true,true,false,false,false,false,0,0), "desktop behavior unchanged");
		Check(AssistBallPolicy.ShouldHide(true,true,true,false,false,false,12,10), "real idle time independent of speed");
		foreach (float scale in new [] { 1.5f, 1.8f, 2.1f, 3.375f })
		{
			float reveal = AssistBallPolicy.Reveal(48, scale, true);
			Check(reveal * scale >= 32 && reveal <= 48, "touchable reveal at UI scale " + scale);
			Check(Math.Abs(AssistBallPolicy.Reveal(64,scale,false)*scale-20)<.01f, "desktop reveal unchanged");
		}
		Console.WriteLine("Assist ball: " + checks + " checks passed");
	}
}
