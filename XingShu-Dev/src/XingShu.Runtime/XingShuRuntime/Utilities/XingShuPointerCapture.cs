using System.Collections.Generic;

namespace XingShuRuntime.Utilities;

internal sealed class XingShuPointerCapture
{
	private readonly Dictionary<int, int> _release = new Dictionary<int, int>();

	private readonly Dictionary<int, int> _beginFrame = new Dictionary<int, int>();

	public void Sample(int id, bool begin, bool end, bool hit, int frame)
	{
		if (begin && (!_beginFrame.TryGetValue(id, out var value) || value != frame))
		{
			_release.Remove(id);
			_beginFrame[id] = frame;
		}
		if (begin && hit)
		{
			_release[id] = int.MaxValue;
		}
		if (end && _release.ContainsKey(id))
		{
			_release[id] = frame;
		}
	}

	public bool Contains(int id, int frame)
	{
		if (_release.TryGetValue(id, out var value))
		{
			return value >= frame;
		}
		return false;
	}

	public void ExpireMissing(HashSet<int> active, int frame)
	{
		foreach (int item in new List<int>(_beginFrame.Keys))
		{
			if (!active.Contains(item))
			{
				if (_release.TryGetValue(item, out var value) && value == int.MaxValue)
				{
					_release[item] = frame;
				}
				else if (value < frame)
				{
					_release.Remove(item);
					_beginFrame.Remove(item);
				}
			}
		}
	}

	public void Clear()
	{
		_release.Clear();
		_beginFrame.Clear();
	}
}
