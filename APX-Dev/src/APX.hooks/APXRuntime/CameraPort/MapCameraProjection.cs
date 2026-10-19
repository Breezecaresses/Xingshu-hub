using System;
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
