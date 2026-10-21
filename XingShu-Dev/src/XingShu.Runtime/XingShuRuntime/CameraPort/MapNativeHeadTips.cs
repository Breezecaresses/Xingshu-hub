using System;
using System.Collections.Generic;
using FairyGUI;
using UI;
using UnityEngine;

namespace XingShuRuntime.CameraPort;

internal sealed class MapNativeHeadTips
{
	private sealed class Saved
	{
		internal GList List;

		internal Vector2 Position;

		internal Vector2 Scale;

		internal Vector2 Anchor;

		internal GObject[] Children;
	}

	private readonly Dictionary<GList, Saved> _saved = new Dictionary<GList, Saved>();

	private readonly HashSet<GList> _seen = new HashSet<GList>();

	private readonly List<GList> _stale = new List<GList>();

	private static void Restore(Saved s)
	{
		if (s.List != null && !s.List.isDisposed)
		{
			s.List.SetScale(s.Scale.x, s.Scale.y);
			s.List.xy = s.Position;
		}
	}

	internal void Restore()
	{
		foreach (Saved value in _saved.Values)
		{
			Restore(value);
		}
		_saved.Clear();
		_seen.Clear();
		_stale.Clear();
	}

	internal void Tick(UIBattleInfoPanel panel, Camera camera)
	{
		_seen.Clear();
		GComponent com_PlayerAttrInfos = panel.com_PlayerAttrInfos;
		if (com_PlayerAttrInfos != null && !com_PlayerAttrInfos.isDisposed)
		{
			for (int i = 0; i < com_PlayerAttrInfos.numChildren; i++)
			{
				if (!(com_PlayerAttrInfos.GetChildAt(i) is UICom_PlayerAttrInfo { isDisposed: false, visible: not false, list_AttrChange: { isDisposed: false, numChildren: not 0, parent: not null } list_AttrChange } uICom_PlayerAttrInfo) || !NativeHeadAnchor.TryGet((uICom_PlayerAttrInfo.PlayerData == null) ? null : uICom_PlayerAttrInfo.PlayerData.CharacterInst, camera, out var head))
				{
					continue;
				}
				_seen.Add(list_AttrChange);
				if (!_saved.TryGetValue(list_AttrChange, out var value))
				{
					value = new Saved
					{
						List = list_AttrChange,
						Position = list_AttrChange.xy,
						Scale = new Vector2(list_AttrChange.scaleX, list_AttrChange.scaleY)
					};
					_saved.Add(list_AttrChange, value);
				}
				bool flag = value.Children == null || value.Children.Length != list_AttrChange.numChildren;
				if (!flag)
				{
					for (int j = 0; j < value.Children.Length; j++)
					{
						if (value.Children[j] != list_AttrChange.GetChildAt(j))
						{
							flag = true;
							break;
						}
					}
				}
				if (flag)
				{
					list_AttrChange.EnsureBoundsCorrect();
					value.Children = new GObject[list_AttrChange.numChildren];
					float num = float.PositiveInfinity;
					float num2 = float.NegativeInfinity;
					float num3 = float.NegativeInfinity;
					for (int k = 0; k < list_AttrChange.numChildren; k++)
					{
						GObject childAt = list_AttrChange.GetChildAt(k);
						value.Children[k] = childAt;
						Vector2 vector = list_AttrChange.displayObject.GlobalToLocal(childAt.displayObject.LocalToGlobal(Vector2.zero));
						Vector2 vector2 = list_AttrChange.displayObject.GlobalToLocal(childAt.displayObject.LocalToGlobal(new Vector2(childAt.width, childAt.height)));
						num = Math.Min(num, Math.Min(vector.x, vector2.x));
						num2 = Math.Max(num2, Math.Max(vector.x, vector2.x));
						num3 = Math.Max(num3, Math.Max(vector.y, vector2.y));
					}
					value.Anchor = new Vector2((num + num2) * 0.5f, num3);
				}
				if (PortMath.Finite(value.Anchor.x) && PortMath.Finite(value.Anchor.y))
				{
					list_AttrChange.SetScale(value.Scale.x * 1.15f, value.Scale.y * 1.15f);
					Vector2 point = list_AttrChange.displayObject.LocalToGlobal(value.Anchor);
					Vector2 point2 = new Vector2(head.x, (float)Screen.height - head.y - 12f * NativeHeadAnchor.PixelUnit);
					DisplayObject displayObject = list_AttrChange.parent.displayObject;
					Vector2 vector3 = displayObject.GlobalToLocal(point2) - displayObject.GlobalToLocal(point);
					if (PortMath.Finite(vector3.x) && PortMath.Finite(vector3.y))
					{
						list_AttrChange.xy += vector3;
					}
				}
			}
		}
		_stale.Clear();
		foreach (KeyValuePair<GList, Saved> item in _saved)
		{
			if (!_seen.Contains(item.Key))
			{
				_stale.Add(item.Key);
			}
		}
		foreach (GList item2 in _stale)
		{
			Restore(_saved[item2]);
			_saved.Remove(item2);
		}
	}
}
