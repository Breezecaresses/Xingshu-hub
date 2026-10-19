using System;
using UnityEngine;

namespace APXRuntime.CameraPort;

internal sealed class MapTouchGesture
{
	private int _count;

	private int _id0;

	private int _id1;

	private bool _locked;

	private bool _moving;

	private Vector2 _previous;

	private Vector2 _origin;

	private float _span;

	private float _originSpan;

	internal bool Consumed { get; private set; }

	internal bool Owns(int id)
	{
		if (!_locked && _count > 0)
		{
			if (id != _id0)
			{
				if (_count == 2)
				{
					return id == _id1;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	internal void Cancel()
	{
		_locked = true;
		_count = 0;
		_moving = false;
		Consumed = false;
	}

	private static bool Finite(Vector2 p)
	{
		if (PortMath.Finite(p.x))
		{
			return PortMath.Finite(p.y);
		}
		return false;
	}

	private static float Distance(Vector2 a, Vector2 b)
	{
		return (float)Math.Sqrt((a - b).sqrMagnitude);
	}

	private bool Reject()
	{
		Cancel();
		return false;
	}

	internal bool Update(MapTouchSample[] samples, int count, bool allowed, float threshold, out MapTouchMotion motion)
	{
		motion = new MapTouchMotion
		{
			ZoomRatio = 1f
		};
		if (count == 0)
		{
			_locked = false;
			_count = 0;
			_moving = false;
			Consumed = false;
			return false;
		}
		if (!allowed || samples == null || count < 0 || count > 2 || count > samples.Length || !PortMath.Finite(threshold) || threshold < 1f)
		{
			return Reject();
		}
		int num = 0;
		MapTouchSample mapTouchSample = default(MapTouchSample);
		MapTouchSample mapTouchSample2 = default(MapTouchSample);
		bool flag = _count == 0;
		bool flag2 = _count < 2;
		for (int i = 0; i < count; i++)
		{
			MapTouchSample mapTouchSample3 = samples[i];
			if (mapTouchSample3.Cancelled || mapTouchSample3.Id < 0 || !Finite(mapTouchSample3.Position))
			{
				return Reject();
			}
			if (i > 0 && mapTouchSample3.Id == samples[0].Id)
			{
				return Reject();
			}
			if (mapTouchSample3.Id == _id0)
			{
				flag = true;
			}
			if (mapTouchSample3.Id == _id1)
			{
				flag2 = true;
			}
			if (!mapTouchSample3.Ended)
			{
				if (!mapTouchSample3.Allowed)
				{
					return Reject();
				}
				bool flag3 = Owns(mapTouchSample3.Id);
				if ((!flag3 && !mapTouchSample3.Began) || (flag3 && mapTouchSample3.Began))
				{
					return Reject();
				}
				if (num++ == 0)
				{
					mapTouchSample = mapTouchSample3;
				}
				else
				{
					mapTouchSample2 = mapTouchSample3;
				}
			}
		}
		if (num == 0)
		{
			_locked = false;
			_count = 0;
			_moving = false;
			Consumed = false;
			return false;
		}
		if (_locked)
		{
			return false;
		}
		if (!flag || !flag2)
		{
			return Reject();
		}
		if (num == 2 && mapTouchSample.Id > mapTouchSample2.Id)
		{
			MapTouchSample mapTouchSample4 = mapTouchSample;
			mapTouchSample = mapTouchSample2;
			mapTouchSample2 = mapTouchSample4;
		}
		Vector2 vector = ((num == 1) ? mapTouchSample.Position : new Vector2((mapTouchSample.Position.x + mapTouchSample2.Position.x) * 0.5f, (mapTouchSample.Position.y + mapTouchSample2.Position.y) * 0.5f));
		float num2 = ((num == 1) ? 0f : Distance(mapTouchSample.Position, mapTouchSample2.Position));
		if (!Finite(vector) || !PortMath.Finite(num2) || (num == 2 && num2 < threshold * 2f))
		{
			return Reject();
		}
		if (_count != num || mapTouchSample.Id != _id0 || (num == 2 && mapTouchSample2.Id != _id1))
		{
			_count = num;
			_id0 = mapTouchSample.Id;
			_id1 = ((num == 2) ? mapTouchSample2.Id : (-1));
			_previous = (_origin = vector);
			_span = (_originSpan = num2);
			_moving = false;
			Consumed |= num == 2;
			return false;
		}
		if (!_moving && Distance(vector, _origin) < threshold && (num == 1 || Math.Abs(num2 - _originSpan) < threshold))
		{
			return false;
		}
		_moving = true;
		Consumed = true;
		motion.Previous = _previous;
		motion.Current = vector;
		motion.ZoomRatio = ((num == 2) ? Math.Max(0.8f, Math.Min(1.25f, _span / num2)) : 1f);
		_previous = vector;
		_span = num2;
		return true;
	}
}
