using System;
using System.Collections.Generic;
using UnityEngine;

namespace XingShuRuntime.Utilities;

public static class AssistBall
{
	public sealed class ActionItem
	{
		public string Id;

		public string Title;

		public Action Callback;

		public int Order;

		public string Description;

		public string Icon;

		public Func<bool> IsActive;

		public ActionItem(string id, string title, Action callback, int order = 100, string description = "", string icon = "✦", Func<bool> isActive = null)
		{
			Id = id ?? Guid.NewGuid().ToString("N");
			Title = title ?? "未命名功能";
			Callback = callback;
			Order = order;
			Description = (string.IsNullOrEmpty(description) ? "扩展 Hook 战术模块" : description);
			Icon = (string.IsNullOrEmpty(icon) ? "✦" : icon);
			IsActive = isActive;
		}
	}

	private static readonly List<ActionItem> _actions = new List<ActionItem>();

	private static readonly object _syncLock = new object();

	public static Action OnToggle;

	public static Action<bool> OnSetVisible;

	public static Func<bool> OnGetVisible;

	public static Func<bool> OnGetMenuOpen;

	public static bool IsVisible
	{
		get
		{
			try
			{
				return OnGetVisible != null && OnGetVisible();
			}
			catch
			{
				return false;
			}
		}
	}

	public static bool IsMenuOpen
	{
		get
		{
			try
			{
				return OnGetMenuOpen != null && OnGetMenuOpen();
			}
			catch
			{
				return false;
			}
		}
	}

	public static void RegisterAction(string title, Action callback, string description = "", string icon = "✦", Func<bool> isActive = null)
	{
		RegisterAction(title, title, callback, 100, description, icon, isActive);
	}

	public static void RegisterAction(string id, string title, Action callback, int order = 100, string description = "", string icon = "✦", Func<bool> isActive = null)
	{
		if (callback == null)
		{
			return;
		}
		lock (_syncLock)
		{
			_actions.RemoveAll((ActionItem x) => x.Id == id);
			_actions.Add(new ActionItem(id, title, callback, order, description, icon, isActive));
			_actions.Sort((ActionItem a, ActionItem b) => a.Order.CompareTo(b.Order));
		}
		Debug.Log("[XingShu AssistBall] 已注册功能项: " + title + " (id=" + id + ", order=" + order + ")");
	}

	public static void Unregister(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		lock (_syncLock)
		{
			_actions.RemoveAll((ActionItem x) => x.Id == id);
		}
	}

	public static List<ActionItem> GetActions()
	{
		lock (_syncLock)
		{
			return new List<ActionItem>(_actions);
		}
	}

	public static void Toggle()
	{
		try
		{
			if (OnToggle != null)
			{
				OnToggle();
			}
		}
		catch
		{
		}
	}

	public static void Show()
	{
		try
		{
			if (OnSetVisible != null)
			{
				OnSetVisible(obj: true);
			}
		}
		catch
		{
		}
	}

	public static void Hide()
	{
		try
		{
			if (OnSetVisible != null)
			{
				OnSetVisible(obj: false);
			}
		}
		catch
		{
		}
	}
}
