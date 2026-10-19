using System;
using System.Collections.Generic;
using System.Reflection;
using Core;
using Core.Scene;
using Core.Unit;
using UnityEngine;

namespace APXRuntime.CameraPort;

internal static class NativeDiceVfxBridge
{
	internal sealed class Binding
	{
		internal Transform Source;

		internal Character Owner;

		internal string Key;

		internal Vector3 Offset;

		internal Vector3 NativeRoot;

		internal Func<float> Diameter;

		internal Func<Vector3> NativePosition;

		internal bool Disposed;

		internal float Expires;

		internal readonly List<Sample> Samples = new List<Sample>();

		internal void Dispose()
		{
			if (!Disposed)
			{
				Record(this);
				Disposed = true;
				Expires = Time.unscaledTime + 30f;
			}
		}
	}

	internal struct Sample
	{
		internal Vector3 World;

		internal Vector3 Native;

		internal float Diameter;
	}

	private sealed class ParticleState
	{
		internal ParticleSystem System;

		internal ParticleSystemSimulationSpace Space;

		internal ParticleSystemScalingMode Scaling;

		internal Transform Custom;

		internal bool Local;

		private ParticleSystem.Particle[] _buffer;

		private static readonly FieldInfo Position = typeof(ParticleSystem.Particle).GetField("m_Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		private static readonly FieldInfo Velocity = typeof(ParticleSystem.Particle).GetField("m_Velocity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		internal void Switch(bool local)
		{
			if (System == null || Local == local)
			{
				return;
			}
			ParticleSystem.MainModule main = System.main;
			if (Space != 0)
			{
				if (Position == null || Velocity == null)
				{
					throw new NotSupportedException("Native particle position/velocity fields unavailable");
				}
				int num = Math.Max(1, main.maxParticles);
				if (_buffer == null || _buffer.Length < num)
				{
					_buffer = new ParticleSystem.Particle[num];
				}
				int particles = System.GetParticles(_buffer);
				Matrix4x4 matrix4x = ((Space == ParticleSystemSimulationSpace.Custom && Custom != null) ? Custom.localToWorldMatrix : Matrix4x4.identity);
				Matrix4x4 matrix4x2 = (local ? (System.transform.worldToLocalMatrix * matrix4x) : (matrix4x.inverse * System.transform.localToWorldMatrix));
				for (int i = 0; i < particles; i++)
				{
					object obj = _buffer[i];
					_buffer[i].position = matrix4x2.MultiplyPoint3x4((Vector3)Position.GetValue(obj));
					_buffer[i].velocity = matrix4x2.MultiplyVector((Vector3)Velocity.GetValue(obj));
				}
				main.simulationSpace = ((!local) ? Space : ParticleSystemSimulationSpace.Local);
				System.SetParticles(_buffer, particles);
			}
			main.scalingMode = ((!local) ? Scaling : ParticleSystemScalingMode.Hierarchy);
			Local = local;
		}
	}

	private sealed class Ticket
	{
		internal Effect Effect;

		internal string Key;

		internal Vector3 Emission;

		internal float Deadline;

		internal GameObject Frame;

		internal Character Owner;

		internal Vector3 NativeSpawn;

		internal Vector3 Offset;

		internal Vector3 EmissionOffset;

		internal float Diameter;

		internal ParticleState[] Particles;

		internal Renderer[] Renderers;
	}

	private static readonly List<Binding> Sources = new List<Binding>();

	private static readonly List<Ticket> Tickets = new List<Ticket>();

	private static bool _active;

	private static int _observed;

	private static int _attached;

	private static int _ambiguous;

	private static int _errors;

	internal static int ActiveCount => Tickets.Count;

	internal static string Status => "骰子 v8 · 原生特效 " + _observed + " / 绑定 " + _attached + " / 活动 " + Tickets.Count + " / 歧义 " + _ambiguous + " / 异常 " + _errors;

	internal static Binding Install(Animator animator, Transform source, Character owner, string key, Vector3 offset, Func<float> diameter, Func<Vector3> nativePosition)
	{
		if (source == null || owner == null || string.IsNullOrEmpty(key))
		{
			return null;
		}
		Binding binding = new Binding
		{
			Source = source,
			Owner = owner,
			Key = key,
			Offset = offset,
			NativeRoot = owner.transform.position + Vector3.up * 48f + offset,
			Diameter = diameter,
			NativePosition = nativePosition
		};
		Sources.Add(binding);
		Record(binding);
		return binding;
	}

	internal static void Record(Binding b)
	{
		if (b == null || b.Disposed || b.Source == null || b.Owner == null)
		{
			return;
		}
		Sample sample = default(Sample);
		sample.World = b.Source.position;
		sample.Native = b.NativePosition();
		sample.Diameter = b.Diameter();
		Sample item = sample;
		if (!NativeHeadAnchor.Finite(item.World) || !NativeHeadAnchor.Finite(item.Native))
		{
			return;
		}
		int count = b.Samples.Count;
		if (count <= 0 || !Exact(b.Samples[count - 1].World, item.World) || !Exact(b.Samples[count - 1].Native, item.Native) || b.Samples[count - 1].Diameter != item.Diameter)
		{
			if (count >= 1024)
			{
				b.Samples.RemoveAt(0);
			}
			b.Samples.Add(item);
		}
	}

	internal static void Observe(Effect effect, string key)
	{
		if (effect == null)
		{
			return;
		}
		Release(effect.gameObject);
		if (!_active || effect.transform.parent != null || string.IsNullOrEmpty(key))
		{
			return;
		}
		bool flag = false;
		foreach (Binding source in Sources)
		{
			if (source.Key == key && (!source.Disposed || Time.unscaledTime <= source.Expires))
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			return;
		}
		_observed++;
		Ticket ticket = new Ticket
		{
			Effect = effect,
			Key = key,
			Emission = effect.transform.position,
			Deadline = Time.unscaledTime + 2f
		};
		Tickets.Add(ticket);
		try
		{
			TryAttach(ticket);
		}
		catch (Exception ex)
		{
			Fail(ticket, ex);
		}
	}

	private static bool TryAttach(Ticket t)
	{
		Binding binding = null;
		Sample sample = default(Sample);
		foreach (Binding source in Sources)
		{
			if (source.Key != t.Key || source.Owner == null || (source.Disposed && Time.unscaledTime > source.Expires))
			{
				continue;
			}
			Record(source);
			foreach (Sample sample2 in source.Samples)
			{
				if (Exact(sample2.World, t.Emission) && !(sample2.Diameter <= 0.001f))
				{
					if (binding != null && ((object)binding.Owner != source.Owner || !Exact(binding.Offset, source.Offset) || !Exact(binding.NativeRoot, source.NativeRoot) || !Exact(sample.Native, sample2.Native) || sample.Diameter != sample2.Diameter))
					{
						_ambiguous++;
						return false;
					}
					binding = source;
					sample = sample2;
				}
			}
		}
		if (binding == null)
		{
			return false;
		}
		if (t.Effect.transform.parent != null)
		{
			return false;
		}
		t.Owner = binding.Owner;
		t.Offset = binding.Offset;
		t.NativeSpawn = sample.Native;
		t.Diameter = sample.Diameter;
		t.EmissionOffset = sample.Native - binding.NativeRoot;
		t.Frame = new GameObject("APX Native Dice VFX Anchor");
		t.Frame.hideFlags = HideFlags.DontSave;
		t.Frame.transform.position = t.Emission;
		t.Effect.transform.SetParent(t.Frame.transform, worldPositionStays: true);
		ParticleSystem[] componentsInChildren = t.Effect.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
		t.Particles = new ParticleState[componentsInChildren.Length];
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			ParticleSystem.MainModule main = componentsInChildren[i].main;
			t.Particles[i] = new ParticleState
			{
				System = componentsInChildren[i],
				Space = main.simulationSpace,
				Scaling = main.scalingMode,
				Custom = main.customSimulationSpace
			};
		}
		t.Renderers = t.Effect.GetComponentsInChildren<Renderer>(includeInactive: true);
		_attached++;
		Apply(t);
		return true;
	}

	private static bool Exact(Vector3 a, Vector3 b)
	{
		if (a.x == b.x && a.y == b.y)
		{
			return a.z == b.z;
		}
		return false;
	}

	private static bool Owns(Ticket t)
	{
		if (t.Frame != null && t.Effect != null)
		{
			return t.Effect.transform.parent == t.Frame.transform;
		}
		return false;
	}

	private static void NativePose(Ticket t)
	{
		if (t.Frame != null)
		{
			t.Frame.transform.position = t.NativeSpawn;
			t.Frame.transform.localScale = Vector3.one;
		}
		if (Owns(t) && t.Particles != null)
		{
			ParticleState[] particles = t.Particles;
			for (int i = 0; i < particles.Length; i++)
			{
				particles[i]?.Switch(local: false);
			}
		}
	}

	private static void Finish(Ticket t)
	{
		NativePose(t);
		if (Owns(t))
		{
			t.Effect.transform.SetParent(null, worldPositionStays: true);
		}
		Tickets.Remove(t);
		if (t.Frame != null)
		{
			UnityEngine.Object.Destroy(t.Frame);
		}
	}

	private static void Fail(Ticket t, Exception ex)
	{
		_errors++;
		Debug.LogWarning("[APX Dice v8] native VFX compensation: " + ex);
		Finish(t);
	}

	internal static void Release(GameObject effect)
	{
		if (effect == null)
		{
			return;
		}
		for (int num = Tickets.Count - 1; num >= 0; num--)
		{
			if (Tickets[num].Effect != null && Tickets[num].Effect.gameObject == effect)
			{
				Finish(Tickets[num]);
			}
		}
	}

	internal static void Clear()
	{
		_active = false;
		for (int num = Tickets.Count - 1; num >= 0; num--)
		{
			Finish(Tickets[num]);
		}
		Sources.Clear();
	}

	private static void Apply(Ticket t)
	{
		BattleSceneController inst = BattleSceneController.inst;
		Camera camera = ((inst == null) ? null : inst.mainCamera);
		if (!_active || camera == null || camera.targetTexture != null || !NativeHeadAnchor.TryGet(t.Owner, camera, out var head))
		{
			NativePose(t);
			return;
		}
		Vector3 vector = camera.ScreenToWorldPoint(head);
		float num = Math.Abs(camera.WorldToScreenPoint(vector + camera.transform.up * t.Diameter).y - head.y);
		float num2 = NativeHeadLayout.ReadableScale(num, 140f * NativeHeadAnchor.PixelUnit);
		float num3 = num * num2;
		if (!PortMath.Finite(num3) || num3 <= 0.001f)
		{
			NativePose(t);
			return;
		}
		float num4 = Vector3.Dot(t.Offset, camera.transform.right);
		if (Math.Abs(num4) < 0.01f)
		{
			num4 = ((Math.Abs(t.Offset.x) > 0.01f) ? t.Offset.x : t.Offset.z);
		}
		float lane = ((!(Math.Abs(num4) < 0.01f)) ? ((num4 > 0f) ? 1 : (-1)) : 0);
		Vector2 vector2 = NativeHeadLayout.DiceCenter(new Vector2(head.x, head.y), num3, 12f * NativeHeadAnchor.PixelUnit, lane);
		ParticleState[] particles = t.Particles;
		for (int i = 0; i < particles.Length; i++)
		{
			particles[i].Switch(local: true);
		}
		t.Frame.transform.localScale = Vector3.one * num2;
		t.Frame.transform.position = camera.ScreenToWorldPoint(new Vector3(vector2.x, vector2.y, head.z)) + t.EmissionOffset * num2;
	}

	internal static void Tick(bool active)
	{
		_active = active;
		for (int num = Sources.Count - 1; num >= 0; num--)
		{
			if (Sources[num].Owner == null || (Sources[num].Disposed && Time.unscaledTime > Sources[num].Expires))
			{
				Sources.RemoveAt(num);
			}
		}
		for (int num2 = Tickets.Count - 1; num2 >= 0; num2--)
		{
			Ticket ticket = Tickets[num2];
			try
			{
				if (ticket.Effect == null || !ticket.Effect.IsActive || ticket.Effect.effectPoolKey != ticket.Key || !ticket.Effect.gameObject.activeInHierarchy)
				{
					Finish(ticket);
				}
				else if (ticket.Frame == null)
				{
					if (!active || Time.unscaledTime > ticket.Deadline)
					{
						Finish(ticket);
					}
					else
					{
						TryAttach(ticket);
					}
				}
				else if (!Owns(ticket))
				{
					Finish(ticket);
				}
				else
				{
					Apply(ticket);
				}
			}
			catch (Exception ex)
			{
				Fail(ticket, ex);
			}
		}
	}

	internal static void ProtectLayers(Camera camera)
	{
		float[] layerCullDistances = camera.layerCullDistances;
		bool flag = false;
		foreach (Ticket ticket in Tickets)
		{
			if (!Owns(ticket) || ticket.Renderers == null)
			{
				continue;
			}
			Renderer[] renderers = ticket.Renderers;
			foreach (Renderer renderer in renderers)
			{
				if (renderer != null && layerCullDistances[renderer.gameObject.layer] > 0f && layerCullDistances[renderer.gameObject.layer] < camera.farClipPlane)
				{
					layerCullDistances[renderer.gameObject.layer] = camera.farClipPlane;
					flag = true;
				}
			}
		}
		if (flag)
		{
			camera.layerCullDistances = layerCullDistances;
		}
	}
}
