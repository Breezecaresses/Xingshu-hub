using System;
using System.Collections.Generic;
using Core.Scene;
using Core.Unit;
using GameLogic;
using Tools;
using UnityEngine;

namespace APXRuntime.CameraPort;

internal sealed class MapNativeDice
{
	internal sealed class DiceCameraRange
	{
		private sealed class Saved
		{
			internal Camera Camera;

			internal float Far;

			internal float LastFar;

			internal float[] Distances;

			internal float LastDistance;

			internal bool DistanceChanged;
		}

		private readonly List<Saved> _saved = new List<Saved>();

		private static bool Matches(Camera main, Camera child)
		{
			if (child != null && child != main && child.name == "DiceCamera" && child.transform.parent == main.transform && child.targetTexture == null)
			{
				return child.cullingMask == 256;
			}
			return false;
		}

		internal void Protect(Camera main)
		{
			if (main == null || !PortMath.Finite(main.farClipPlane))
			{
				Restore();
				return;
			}
			for (int num = _saved.Count - 1; num >= 0; num--)
			{
				if (!Matches(main, _saved[num].Camera))
				{
					RestoreOne(_saved[num]);
					_saved.RemoveAt(num);
				}
			}
			Camera[] componentsInChildren = main.GetComponentsInChildren<Camera>(includeInactive: true);
			foreach (Camera camera in componentsInChildren)
			{
				if (!Matches(main, camera))
				{
					continue;
				}
				Saved saved = null;
				foreach (Saved item in _saved)
				{
					if (item.Camera == camera)
					{
						saved = item;
						break;
					}
				}
				if (saved == null)
				{
					saved = new Saved
					{
						Camera = camera,
						Far = camera.farClipPlane,
						LastFar = camera.farClipPlane,
						Distances = (float[])camera.layerCullDistances.Clone()
					};
					_saved.Add(saved);
				}
				float num3 = (saved.LastFar = (camera.farClipPlane = Math.Max(saved.Far, main.farClipPlane)));
				float[] layerCullDistances = camera.layerCullDistances;
				if (layerCullDistances[8] > 0f && layerCullDistances[8] < num3)
				{
					layerCullDistances[8] = num3;
					camera.layerCullDistances = layerCullDistances;
					saved.LastDistance = num3;
					saved.DistanceChanged = true;
				}
			}
		}

		private static void RestoreOne(Saved saved)
		{
			if (saved.Camera == null)
			{
				return;
			}
			if (saved.Camera.farClipPlane == saved.LastFar)
			{
				saved.Camera.farClipPlane = saved.Far;
			}
			if (saved.DistanceChanged)
			{
				float[] layerCullDistances = saved.Camera.layerCullDistances;
				if (layerCullDistances[8] == saved.LastDistance)
				{
					layerCullDistances[8] = saved.Distances[8];
					saved.Camera.layerCullDistances = layerCullDistances;
				}
			}
		}

		internal void Restore()
		{
			foreach (Saved item in _saved)
			{
				RestoreOne(item);
			}
			_saved.Clear();
		}
	}

	private sealed class Roll
	{
		internal DiceController Dice;

		internal Character Owner;

		internal Vector3 Position;

		internal Vector3 Scale;

		internal Vector3 Offset;

		internal Animator Animator;

		internal AnimatorCullingMode Culling;

		internal float Diameter;

		internal Transform Blank;

		internal Transform Point;

		internal bool Applied;

		internal float Factor = 1f;

		internal NativeDiceVfxBridge.Binding Vfx;
	}

	private readonly DiceCameraRange _diceCameraRange = new DiceCameraRange();

	private readonly Dictionary<DiceController, Roll> _rolls = new Dictionary<DiceController, Roll>();

	private readonly List<DiceController> _stale = new List<DiceController>();

	private Camera _camera;

	private float[] _cullDistances;

	internal void Register(DiceController dice, long playerId, Vector3 position, Transform blank, Transform point, FashionDiceConfigure config = null)
	{
		Release(dice);
		if (dice == null || dice.Container == null || blank == null || point == null || !SimpleSingletonProvider<GameLogicManager>.hasInstance)
		{
			return;
		}
		BattlePlayerData playerDataById = SimpleSingletonProvider<GameLogicManager>.inst.battle.GetPlayerDataById(playerId);
		if (playerDataById == null || playerDataById.CharacterInst == null)
		{
			return;
		}
		Animator component = dice.Container.GetComponent<Animator>();
		Roll roll = new Roll
		{
			Dice = dice,
			Owner = playerDataById.CharacterInst,
			Position = position,
			Scale = dice.transform.localScale,
			Blank = blank,
			Point = point,
			Diameter = Math.Max(ModelDiameter(blank), ModelDiameter(point)),
			Offset = position - playerDataById.CharacterInst.transform.position - Vector3.up * 48f,
			Animator = component,
			Culling = ((!(component == null)) ? component.cullingMode : AnimatorCullingMode.AlwaysAnimate)
		};
		_rolls.Add(dice, roll);
		if (config != null)
		{
			roll.Vfx = NativeDiceVfxBridge.Install(component, dice.Container.transform, roll.Owner, config.DiceVfx, roll.Offset, () => roll.Diameter, () => roll.Position + (dice.Container.transform.position - dice.transform.position) / roll.Factor);
		}
	}

	private static float ModelDiameter(Transform model)
	{
		float num = 0f;
		if (model != null)
		{
			Renderer[] componentsInChildren = model.GetComponentsInChildren<Renderer>(includeInactive: true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				float magnitude = componentsInChildren[i].bounds.size.magnitude;
				if (PortMath.Finite(magnitude) && magnitude > 0.001f && magnitude < 500f)
				{
					num = Math.Max(num, magnitude);
				}
			}
		}
		return num;
	}

	internal void ReleaseModel(GameObject model)
	{
		if (model == null)
		{
			return;
		}
		DiceController diceController = null;
		foreach (Roll value in _rolls.Values)
		{
			if ((value.Blank != null && model.transform == value.Blank) || (value.Point != null && model.transform == value.Point))
			{
				diceController = value.Dice;
				break;
			}
		}
		if (diceController != null)
		{
			Release(diceController);
		}
	}

	private static void Restore(Roll r)
	{
		if (r.Applied)
		{
			if (r.Dice != null)
			{
				r.Dice.transform.position = r.Position;
				r.Dice.transform.localScale = r.Scale;
			}
			if (r.Animator != null)
			{
				r.Animator.cullingMode = r.Culling;
			}
			r.Applied = false;
			r.Factor = 1f;
		}
	}

	internal void Release(DiceController dice)
	{
		if ((object)dice != null && _rolls.TryGetValue(dice, out var value))
		{
			Restore(value);
			if (value.Vfx != null)
			{
				value.Vfx.Dispose();
			}
			_rolls.Remove(dice);
		}
	}

	internal void Restore()
	{
		NativeDiceVfxBridge.Tick(active: false);
		foreach (Roll value in _rolls.Values)
		{
			Restore(value);
		}
		RestoreCamera();
	}

	private void RestoreCamera()
	{
		_diceCameraRange.Restore();
		if (_camera != null && _cullDistances != null)
		{
			_camera.layerCullDistances = _cullDistances;
		}
		_camera = null;
		_cullDistances = null;
	}

	internal void Clear()
	{
		Restore();
		foreach (Roll value in _rolls.Values)
		{
			if (value.Vfx != null)
			{
				value.Vfx.Dispose();
			}
		}
		_rolls.Clear();
		NativeDiceVfxBridge.Clear();
	}

	internal void Tick(bool active)
	{
		BattleSceneController inst = BattleSceneController.inst;
		Camera camera = ((inst == null) ? null : inst.mainCamera);
		if (!active || camera == null || camera.targetTexture != null)
		{
			Restore();
			return;
		}
		if (_camera != camera)
		{
			Restore();
			_camera = camera;
			_cullDistances = (float[])camera.layerCullDistances.Clone();
		}
		if (_rolls.Count > 0 || NativeDiceVfxBridge.ActiveCount > 0)
		{
			_diceCameraRange.Protect(camera);
		}
		_stale.Clear();
		foreach (KeyValuePair<DiceController, Roll> roll in _rolls)
		{
			Roll value = roll.Value;
			NativeDiceVfxBridge.Record(value.Vfx);
			if (value.Dice == null || !value.Dice.gameObject.activeInHierarchy || value.Owner == null)
			{
				_stale.Add(roll.Key);
				continue;
			}
			if (!NativeHeadAnchor.TryGet(value.Owner, camera, out var head))
			{
				Restore(value);
				continue;
			}
			value.Applied = true;
			if (value.Animator != null)
			{
				value.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}
			Renderer[] componentsInChildren = value.Dice.Container.GetComponentsInChildren<Renderer>(includeInactive: true);
			Renderer[] array;
			if (value.Diameter <= 0.001f)
			{
				array = componentsInChildren;
				for (int i = 0; i < array.Length; i++)
				{
					float magnitude = array[i].bounds.size.magnitude;
					if (PortMath.Finite(magnitude) && magnitude > 0.001f && magnitude < 500f)
					{
						value.Diameter = Math.Max(value.Diameter, magnitude);
					}
				}
			}
			if (value.Diameter <= 0.001f)
			{
				continue;
			}
			Vector3 vector = camera.ScreenToWorldPoint(head);
			float num = Math.Abs(camera.WorldToScreenPoint(vector + camera.transform.up * value.Diameter).y - head.y);
			float num2 = NativeHeadLayout.ReadableScale(num, 140f * NativeHeadAnchor.PixelUnit);
			float num3 = num * num2;
			if (!PortMath.Finite(num3) || num3 <= 0f)
			{
				continue;
			}
			float num4 = Vector3.Dot(value.Offset, camera.transform.right);
			if (Math.Abs(num4) < 0.01f)
			{
				num4 = ((Math.Abs(value.Offset.x) > 0.01f) ? value.Offset.x : value.Offset.z);
			}
			float lane = ((Math.Abs(num4) < 0.01f) ? 0f : ((num4 > 0f) ? 1f : (-1f)));
			Vector2 vector2 = NativeHeadLayout.DiceCenter(new Vector2(head.x, head.y), num3, 12f * NativeHeadAnchor.PixelUnit, lane);
			if ((value.Blank == null || value.Blank.parent == value.Dice.Container.transform) && (value.Point == null || value.Point.parent == value.Dice.Container.transform))
			{
				value.Dice.transform.localScale = value.Scale * num2;
				value.Factor = num2;
			}
			value.Dice.transform.position = camera.ScreenToWorldPoint(new Vector3(vector2.x, vector2.y, head.z));
			NativeDiceVfxBridge.Record(value.Vfx);
			float[] layerCullDistances = camera.layerCullDistances;
			bool flag = false;
			array = componentsInChildren;
			for (int i = 0; i < array.Length; i++)
			{
				int layer = array[i].gameObject.layer;
				if (layerCullDistances[layer] > 0f && layerCullDistances[layer] < camera.farClipPlane)
				{
					layerCullDistances[layer] = camera.farClipPlane;
					flag = true;
				}
			}
			if (flag)
			{
				camera.layerCullDistances = layerCullDistances;
			}
		}
		foreach (DiceController item in _stale)
		{
			Release(item);
		}
		NativeDiceVfxBridge.Tick(active: true);
		NativeDiceVfxBridge.ProtectLayers(camera);
		if (_rolls.Count == 0 && NativeDiceVfxBridge.ActiveCount == 0)
		{
			RestoreCamera();
		}
	}
}
