using System;
using System.Collections.Generic;
using Core.Scene;
using FairyGUI;
using GameLogic;
using Tools;
using UI;
using UnityEngine;

namespace APXRuntime.CameraPort;

internal sealed class MapCameraProjection
{
	private readonly MapNativeHeadTips _headTips = new MapNativeHeadTips();

	private UIBattleInfoPanel _panel;

	private Container _root;

	private int _corrected;

	private int _anchored;

	private int _playerAnchors;

	private float _maxError;

	private bool _logged;

	private string _failure;

	private readonly Dictionary<UICom_AttrInfo, Vector2> _nativeScales = new Dictionary<UICom_AttrInfo, Vector2>();
	private readonly List<UICom_AttrInfo> _compactLabels = new List<UICom_AttrInfo>();
	internal float AnnotationScale = 1f;

	private void RestoreLabelScales()
	{
		foreach (var entry in _nativeScales)
			if (!entry.Key.isDisposed) entry.Key.SetScale(entry.Value.x, entry.Value.y);
		_nativeScales.Clear();
		_compactLabels.Clear();
	}

	private void CompactLabels(GComponent container, Camera camera)
	{
		_compactLabels.Clear();
		float scale = Mathf.Clamp(AnnotationScale, 0.35f, 1f);
		for (int i = 0; i < container.numChildren; i++)
		{
			if (!(container.GetChildAt(i) is UICom_AttrInfo label) || label.isDisposed) continue;
			if (!_nativeScales.TryGetValue(label, out var nativeScale))
			{
				nativeScale = new Vector2(label.scaleX, label.scaleY);
				_nativeScales.Add(label, nativeScale);
			}
			label.SetScale(nativeScale.x * scale, nativeScale.y * scale);
			if (scale >= 0.999f || !label.visible || label.parent?.displayObject == null) continue;
			Vector3 owner = label.GetOwnerPos();
			if (owner == Vector3.zero) continue;
			Vector3 screen = camera.WorldToScreenPoint(owner);
			if (screen.z <= 0f) continue;
			Vector2 position = label.parent.displayObject.GlobalToLocal(new Vector2(screen.x, Screen.height - screen.y));
			var character = label.PlayerData?.CharacterInst;
			bool right = character != null && character.standLand != null && character.canStep == 0
				&& (character.transform.localPosition - character.standLand.transform.localPosition).x > 0.1f;
			// The native layout uses a fixed 200px rise and 0.8 * width side offset.
			// Scale both offsets with the label, so the leader line also gets shorter.
			position.x += (right ? 1f : -1f) * label.width * 0.8f * scale;
			position.y -= 200f * scale;
			label.xy = position;
			_compactLabels.Add(label);
		}
		// The native collision pass uses unscaled sizes; repeat only for compact labels
		// using their actual visible sizes to avoid spreading them across the board.
		for (int i = 0; i < _compactLabels.Count; i++)
			for (int j = i + 1; j < _compactLabels.Count; j++)
			{
				var a = _compactLabels[i];
				var b = _compactLabels[j];
				float overlapX = (a.width * Math.Abs(a.scaleX) + b.width * Math.Abs(b.scaleX)) * 0.5f - Math.Abs(a.x - b.x);
				float overlapY = (a.height * Math.Abs(a.scaleY) + b.height * Math.Abs(b.scaleY)) * 0.5f - Math.Abs(a.y - b.y);
				if (overlapX > 0f && overlapY > 0f)
				{
					float offset = (overlapY + 4f) * 0.5f;
					if (a.y <= b.y) { a.y -= offset; b.y += offset; }
					else { a.y += offset; b.y -= offset; }
				}
			}
	}

	internal string Status => "头顶定位 v7 / 标注投影 v2 · " + ((_failure != null) ? ("异常：" + _failure) : ((_panel == null) ? "等待有效面板" : ("已校正 " + _corrected + "，锚点组件 " + _anchored + "，原生飘字锚点 " + _playerAnchors + "，坐标回算误差 " + _maxError.ToString("0.00") + " px")));

	internal void Bind(bool active)
	{
		UIBattleInfoPanel uIBattleInfoPanel = null;
		if (active && SimpleSingletonProvider<GameLogicManager>.hasInstance)
		{
			uIBattleInfoPanel = SimpleSingletonProvider<GameLogicManager>.inst.battle?.battleInfo?.UIObject;
		}
		if (uIBattleInfoPanel != null && uIBattleInfoPanel.isDisposed)
		{
			uIBattleInfoPanel = null;
		}
		if (uIBattleInfoPanel != _panel)
		{
			_headTips.Restore();
			RestoreLabelScales();
			if (_root != null)
			{
				_root.onUpdate -= CorrectGuideLines;
			}
			_panel = uIBattleInfoPanel;
			_corrected = (_anchored = (_playerAnchors = 0));
			_maxError = 0f;
			_root = uIBattleInfoPanel?.rootContainer;
			if (_root != null)
			{
				_root.onUpdate += CorrectGuideLines;
			}
		}
	}

	private void CorrectPlayerAnchors(UIBattleInfoPanel panel, Camera camera)
	{
		GComponent com_PlayerAttrInfos = panel.com_PlayerAttrInfos;
		if (com_PlayerAttrInfos == null || com_PlayerAttrInfos.isDisposed)
		{
			return;
		}
		for (int i = 0; i < com_PlayerAttrInfos.numChildren; i++)
		{
			if (!(com_PlayerAttrInfos.GetChildAt(i) is UICom_PlayerAttrInfo { isDisposed: false, visible: not false, parent: { isDisposed: false, displayObject: not null } parent } uICom_PlayerAttrInfo))
			{
				continue;
			}
			Vector3 ownerPos = uICom_PlayerAttrInfo.GetOwnerPos();
			if (ownerPos == Vector3.zero)
			{
				continue;
			}
			Vector3 vector = camera.WorldToScreenPoint(ownerPos);
			if (!PortMath.Finite(vector.x) || !PortMath.Finite(vector.y) || !PortMath.Finite(vector.z))
			{
				continue;
			}
			if (vector.z <= 0f)
			{
				uICom_PlayerAttrInfo.visible = false;
				continue;
			}
			Vector2 point = new Vector2(vector.x, (float)Screen.height - vector.y);
			Vector2 xy = parent.displayObject.GlobalToLocal(point);
			if (PortMath.Finite(xy.x) && PortMath.Finite(xy.y))
			{
				uICom_PlayerAttrInfo.xy = xy;
				_playerAnchors++;
			}
		}
	}

	private void CorrectGuideLines()
	{
		try
		{
			UIBattleInfoPanel panel = _panel;
			Camera camera = ((BattleSceneController.inst == null) ? null : BattleSceneController.inst.mainCamera);
			if (panel == null || panel.isDisposed || camera == null || camera.targetTexture != null)
			{
				return;
			}
			_corrected = (_anchored = (_playerAnchors = 0));
			_maxError = 0f;
			CorrectPlayerAnchors(panel, camera);
			_headTips.Tick(panel, camera);
			GComponent com_AttrInfos = panel.com_AttrInfos;
			if (com_AttrInfos == null || com_AttrInfos.isDisposed)
			{
				return;
			}
			CompactLabels(com_AttrInfos, camera);
			for (int i = 0; i < com_AttrInfos.numChildren; i++)
			{
				if (!(com_AttrInfos.GetChildAt(i) is UICom_AttrInfo { isDisposed: false, visible: not false, displayObject: not null, graph_Sign: not null } uICom_AttrInfo))
				{
					continue;
				}
				Vector3 ownerPos = uICom_AttrInfo.GetOwnerPos();
				if (ownerPos == Vector3.zero)
				{
					continue;
				}
				Vector3 vector = camera.WorldToScreenPoint(ownerPos);
				if (!PortMath.Finite(vector.x) || !PortMath.Finite(vector.y) || !PortMath.Finite(vector.z) || vector.z <= 0f)
				{
					uICom_AttrInfo.visible = false;
					continue;
				}
				Vector2 vector2 = new Vector2(vector.x, (float)Screen.height - vector.y);
				Vector2 targetPos = uICom_AttrInfo.displayObject.GlobalToLocal(vector2);
				if (PortMath.Finite(targetPos.x) && PortMath.Finite(targetPos.y))
				{
					uICom_AttrInfo.UpdateGuideLine(targetPos);
					_corrected++;
					if (uICom_AttrInfo.pivotAsAnchor)
					{
						_anchored++;
					}
					float num = (float)Math.Sqrt((uICom_AttrInfo.displayObject.LocalToGlobal(uICom_AttrInfo.graph_Sign.xy) - vector2).sqrMagnitude);
					if (PortMath.Finite(num))
					{
						_maxError = Math.Max(_maxError, num);
					}
					if (!_logged)
					{
						_logged = true;
						Debug.Log("[APX MapCamera] guide projection v2 active; pivotAsAnchor=" + uICom_AttrInfo.pivotAsAnchor + "; size=" + uICom_AttrInfo.width + "x" + uICom_AttrInfo.height + "; pivot=" + uICom_AttrInfo.pivotX + "," + uICom_AttrInfo.pivotY);
					}
				}
			}
			_failure = null;
		}
		catch (Exception ex)
		{
			Bind(active: false);
			string text = ex.GetType().Name + ": " + ex.Message;
			if (_failure != text)
			{
				Debug.LogWarning("[APX MapCamera] guide projection v2: " + ex);
			}
			_failure = text;
		}
	}
}
