using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Core.Scene;
using Core.Unit;
using Tools;
using UnityEngine;

internal sealed class CharacterAnimProbe : MonoBehaviour
{
	private const string LogName = "xingshu_character_anim.log";

	private const long MaxBytes = 1048576L;

	private const long KeepBytes = 262144L;

	private const int MaxCharacters = 10;

	private static readonly object SyncLock = new object();

	private static string _logPath;

	private static bool _logReady;

	private static bool _logFailed;

	private float _nextSample;

	private static int _lastCharCount = -1;

	private static string _lastSceneName = "";

	private static float _lastReportedTimeScale = 1f;

	private static readonly Dictionary<int, string> _lastCharWarnSignatures = new Dictionary<int, string>();

	private void Update()
	{
		try
		{
			if (!(Time.unscaledTime < _nextSample))
			{
				_nextSample = Time.unscaledTime + 3f;
				EnsureLog();
				SampleOnce();
			}
		}
		catch (Exception ex)
		{
			try
			{
				Debug.LogWarning("[XingShu AnimProbe] update failed: " + ex);
			}
			catch
			{
			}
		}
	}

	private static void EnsureLog()
	{
		if (_logFailed || _logReady)
		{
			return;
		}
		try
		{
			_logPath = Path.Combine(Application.persistentDataPath, "xingshu_character_anim.log");
			Directory.CreateDirectory(Application.persistentDataPath);
			TrimIfNeeded();
			AppendLine("===== XingShu CharacterAnim Probe Start =====");
			AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
			AppendLine("unity=" + Application.unityVersion);
			AppendLine("persistent=" + Application.persistentDataPath);
			Debug.Log("[XingShu AnimProbe] 角色动画诊断日志: " + _logPath);
			_logReady = true;
		}
		catch (Exception ex)
		{
			_logFailed = true;
			try
			{
				Debug.LogWarning("[XingShu AnimProbe] log init failed: " + ex.Message);
			}
			catch
			{
			}
		}
	}

	private static void TrimIfNeeded()
	{
		try
		{
			if (!File.Exists(_logPath))
			{
				return;
			}
			long length;
			using (FileStream fileStream = File.OpenRead(_logPath))
			{
				length = fileStream.Length;
			}
			if (length < 1048576)
			{
				return;
			}
			byte[] array = new byte[0];
			long num = 262144L;
			using (FileStream fileStream2 = File.OpenRead(_logPath))
			{
				long num2 = Math.Max(0L, length - num);
				fileStream2.Seek(num2, SeekOrigin.Begin);
				array = new byte[length - num2];
				int num3 = fileStream2.Read(array, 0, array.Length);
				if (num3 < array.Length)
				{
					byte[] array2 = new byte[num3];
					Array.Copy(array, array2, num3);
					array = array2;
				}
			}
			using FileStream fileStream3 = new FileStream(_logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
			fileStream3.Write(array, 0, array.Length);
		}
		catch
		{
		}
	}

	internal static void AppendLine(string line)
	{
		if (string.IsNullOrEmpty(_logPath))
		{
			return;
		}
		try
		{
			lock (SyncLock)
			{
				using StreamWriter streamWriter = new StreamWriter(_logPath, append: true, Encoding.UTF8);
				streamWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line);
			}
		}
		catch
		{
		}
	}

	private static void SampleOnce()
	{
		string text = "?";
		int num = -1;
		float timeScale = Time.timeScale;
		try
		{
			SceneManager inst = SimpleSingletonProvider<SceneManager>.inst;
			if (inst != null && inst.currentType != null)
			{
				num = (int)inst.currentType.Value;
				text = inst.currentType.Value.ToString();
			}
		}
		catch (Exception ex)
		{
			text = "sceneErr:" + ex.GetType().Name;
		}
		if (!string.Equals(text, _lastSceneName, StringComparison.OrdinalIgnoreCase))
		{
			_lastSceneName = text;
			AppendLine("[SCENE] 场景切换到: " + text + " (type=" + num + ", timeScale=" + timeScale.ToString("0.###") + ")");
		}
		if (Math.Abs(timeScale - _lastReportedTimeScale) > 0.05f)
		{
			_lastReportedTimeScale = timeScale;
			AppendLine("[TIMESCALE] Time.timeScale 变更为: " + timeScale.ToString("0.###") + " (scene=" + text + ")");
		}
		CharacterAnimator[] array = null;
		try
		{
			array = UnityEngine.Object.FindObjectsOfType<CharacterAnimator>();
		}
		catch (Exception)
		{
			array = new CharacterAnimator[0];
		}
		int num2 = ((array != null) ? array.Length : 0);
		if (num2 != _lastCharCount)
		{
			_lastCharCount = num2;
			_lastCharWarnSignatures.Clear();
			AppendLine("[CHARS] 场景 " + text + " 角色数量变更为: " + num2);
		}
		if (array == null || num2 == 0)
		{
			return;
		}
		int num3 = ((num2 > 10) ? 10 : num2);
		for (int i = 0; i < num3; i++)
		{
			CharacterAnimator characterAnimator = array[i];
			if (characterAnimator == null)
			{
				continue;
			}
			string text2 = CheckAbnormal(characterAnimator);
			int instanceID = characterAnimator.GetInstanceID();
			if (!string.IsNullOrEmpty(text2))
			{
				if (!_lastCharWarnSignatures.TryGetValue(instanceID, out var value) || value != text2)
				{
					_lastCharWarnSignatures[instanceID] = text2;
					AppendLine("[WARN_ANIM] 角色 [" + characterAnimator.name + "] 异常: " + text2 + " | 详情: " + Describe(characterAnimator, i));
				}
			}
			else if (_lastCharWarnSignatures.ContainsKey(instanceID))
			{
				_lastCharWarnSignatures.Remove(instanceID);
				AppendLine("[INFO_ANIM] 角色 [" + characterAnimator.name + "] 动画已恢复正常状态");
			}
		}
	}

	private static string CheckAbnormal(CharacterAnimator ca)
	{
		if (ca == null)
		{
			return null;
		}
		try
		{
			Animator animator = ca.animator;
			if (animator == null)
			{
				return "animator组件丢失(null)";
			}
			if (!animator.enabled)
			{
				return "animator被禁用(enabled=false)";
			}
			if (animator.runtimeAnimatorController == null)
			{
				return "缺少runtimeAnimatorController";
			}
			if (ReadBoolField(ca, "_isPaused", fallback: false))
			{
				return "角色被标记为paused";
			}
			if (animator.speed == 0f && Time.timeScale > 0.05f)
			{
				return "anim.speed为0且游戏未暂停";
			}
		}
		catch (Exception ex)
		{
			return "检测异常:" + ex.Message;
		}
		return null;
	}

	private static string Describe(CharacterAnimator ca, int index)
	{
		if (ca == null)
		{
			return "[C" + index + "] <null>";
		}
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			stringBuilder.Append("[C").Append(index).Append("] name=")
				.Append(ca.name);
		}
		catch
		{
			stringBuilder.Append("[C").Append(index).Append("] name=<err>");
		}
		try
		{
			stringBuilder.Append(" active=").Append(ca.gameObject != null && ca.gameObject.activeInHierarchy);
		}
		catch
		{
			stringBuilder.Append(" active=<err>");
		}
		try
		{
			if (ca.transform != null)
			{
				Vector3 position = ca.transform.position;
				Vector3 localScale = ca.transform.localScale;
				stringBuilder.Append(" pos=").Append(position.x.ToString("0.0")).Append(",")
					.Append(position.y.ToString("0.0"))
					.Append(",")
					.Append(position.z.ToString("0.0"));
				stringBuilder.Append(" scale=").Append(localScale.x.ToString("0.###")).Append(",")
					.Append(localScale.y.ToString("0.###"));
			}
		}
		catch
		{
			stringBuilder.Append(" tr=<err>");
		}
		try
		{
			Character owner = ca.owner;
			if (owner == null)
			{
				stringBuilder.Append(" owner=<null>");
			}
			else if (owner.player == null)
			{
				stringBuilder.Append(" owner=<noPlayer>");
			}
			else
			{
				stringBuilder.Append(" playerId=").Append(owner.player.Id);
			}
		}
		catch
		{
			stringBuilder.Append(" owner=<err>");
		}
		Animator animator = null;
		try
		{
			animator = ca.animator;
		}
		catch
		{
			animator = null;
		}
		if (animator == null)
		{
			stringBuilder.Append(" animator=<null>");
			return stringBuilder.ToString();
		}
		try
		{
			stringBuilder.Append(" animEnabled=").Append(animator.enabled ? "1" : "0");
			stringBuilder.Append(" speed=").Append(animator.speed.ToString("0.###"));
			stringBuilder.Append(" ctrl=").Append((animator.runtimeAnimatorController == null) ? "NO" : "yes");
			stringBuilder.Append(" update=").Append(animator.updateMode);
			stringBuilder.Append(" cull=").Append(animator.cullingMode);
		}
		catch (Exception ex)
		{
			stringBuilder.Append(" animErr=").Append(ex.GetType().Name);
			return stringBuilder.ToString();
		}
		try
		{
			bool flag = ReadBoolField(ca, "_isPaused", fallback: false);
			float num = ReadFloatField(ca, "_currentMultiplier", 1f);
			bool flag2 = ReadPlayableGraphValid(ca);
			stringBuilder.Append(" paused=").Append(flag ? "1" : "0");
			stringBuilder.Append(" mult=").Append(num.ToString("0.###"));
			stringBuilder.Append(" playable=").Append(flag2 ? "yes" : "no");
		}
		catch
		{
			stringBuilder.Append(" refl=<err>");
		}
		try
		{
			AnimatorStateInfo currentAnimatorStateInfo = animator.GetCurrentAnimatorStateInfo(0);
			stringBuilder.Append(" stateHash=").Append(currentAnimatorStateInfo.shortNameHash.ToString("X8"));
			stringBuilder.Append(" nt=").Append(currentAnimatorStateInfo.normalizedTime.ToString("0.00"));
			stringBuilder.Append(" speedMul=").Append(currentAnimatorStateInfo.speedMultiplier.ToString("0.00"));
			AnimatorClipInfo[] currentAnimatorClipInfo = animator.GetCurrentAnimatorClipInfo(0);
			if (currentAnimatorClipInfo != null && currentAnimatorClipInfo.Length != 0 && currentAnimatorClipInfo[0].clip != null)
			{
				stringBuilder.Append(" clip=").Append(currentAnimatorClipInfo[0].clip.name).Append(" w=")
					.Append(currentAnimatorClipInfo[0].weight.ToString("0.00"));
			}
			else
			{
				stringBuilder.Append(" clip=<none>");
			}
		}
		catch
		{
			stringBuilder.Append(" state=<err>");
		}
		try
		{
			stringBuilder.Append(" walk=").Append(ReadBoolAnime(ca, "Walk") ? "1" : "0");
			stringBuilder.Append(" walkB=").Append(ReadBoolAnime(ca, "Walk-Back") ? "1" : "0");
		}
		catch
		{
			stringBuilder.Append(" bools=<err>");
		}
		return stringBuilder.ToString();
	}

	private static bool ReadBoolAnime(CharacterAnimator ca, string name)
	{
		try
		{
			return ca.GetAnimeStatus(name);
		}
		catch
		{
			return false;
		}
	}

	private static bool ReadBoolField(object target, string fieldName, bool fallback)
	{
		try
		{
			FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return fallback;
			}
			object value = field.GetValue(target);
			return value is bool && (bool)value;
		}
		catch
		{
			return fallback;
		}
	}

	private static float ReadFloatField(object target, string fieldName, float fallback)
	{
		try
		{
			FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return fallback;
			}
			if (!(field.GetValue(target) is float result))
			{
				return fallback;
			}
			return result;
		}
		catch
		{
			return fallback;
		}
	}

	private static bool ReadPlayableGraphValid(object target)
	{
		try
		{
			FieldInfo field = target.GetType().GetField("playableGraph", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return false;
			}
			object value = field.GetValue(target);
			if (value == null)
			{
				return false;
			}
			MethodInfo method = field.FieldType.GetMethod("IsValid", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			if (method == null)
			{
				return false;
			}
			object obj = method.Invoke(value, null);
			return obj is bool && (bool)obj;
		}
		catch
		{
			return false;
		}
	}
}
