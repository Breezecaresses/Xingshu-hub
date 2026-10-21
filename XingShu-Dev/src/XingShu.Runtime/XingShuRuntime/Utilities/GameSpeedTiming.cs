using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Playables;

namespace XingShuRuntime.Utilities;

internal sealed class GameSpeedTiming
{
	private readonly List<Animator> _animators = new List<Animator>();

	private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();

	private readonly List<PlayableDirector> _directors = new List<PlayableDirector>();

	private readonly FieldInfo _dotweenClock = typeof(DOTween).GetField("unscaledTimeScale", BindingFlags.Static | BindingFlags.Public);

	private bool _ownsClock;

	private bool _ownsDOTween;

	private float _originalTime;

	private float _originalDOTween;

	private float _lastDOTween;

	private float _nextScan;

	public void Apply(float scale)
	{
		scale = Sanitize(scale);
		if (!_ownsClock)
		{
			_originalTime = Time.timeScale;
			_ownsClock = true;
			_nextScan = float.NegativeInfinity;
		}
		Time.timeScale = scale;
		if (_dotweenClock != null)
		{
			if (!_ownsDOTween)
			{
				_originalDOTween = (float)_dotweenClock.GetValue(null);
				_ownsDOTween = true;
			}
			_lastDOTween = _originalDOTween * scale;
			_dotweenClock.SetValue(null, _lastDOTween);
		}
		if (Time.unscaledTime < _nextScan)
		{
			return;
		}
		_nextScan = Time.unscaledTime + 0.5f;
		_animators.RemoveAll((Animator item) => item == null);
		_particles.RemoveAll((ParticleSystem item) => item == null);
		_directors.RemoveAll((PlayableDirector item) => item == null);
		Animator[] array = Object.FindObjectsOfType<Animator>();
		foreach (Animator animator in array)
		{
			if (animator.updateMode == AnimatorUpdateMode.UnscaledTime)
			{
				if (!_animators.Contains(animator))
				{
					_animators.Add(animator);
				}
				animator.updateMode = AnimatorUpdateMode.Normal;
			}
		}
		ParticleSystem[] array2 = Object.FindObjectsOfType<ParticleSystem>();
		foreach (ParticleSystem particleSystem in array2)
		{
			ParticleSystem.MainModule main = particleSystem.main;
			if (main.useUnscaledTime)
			{
				if (!_particles.Contains(particleSystem))
				{
					_particles.Add(particleSystem);
				}
				main.useUnscaledTime = false;
			}
		}
		PlayableDirector[] array3 = Object.FindObjectsOfType<PlayableDirector>();
		foreach (PlayableDirector playableDirector in array3)
		{
			if (playableDirector.timeUpdateMode == DirectorUpdateMode.UnscaledGameTime)
			{
				if (!_directors.Contains(playableDirector))
				{
					_directors.Add(playableDirector);
				}
				playableDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
			}
		}
	}

	public void Restore()
	{
		if (!_ownsClock)
		{
			return;
		}
		Time.timeScale = _originalTime;
		_ownsClock = false;
		if (_ownsDOTween)
		{
			if (Mathf.Approximately((float)_dotweenClock.GetValue(null), _lastDOTween))
			{
				_dotweenClock.SetValue(null, _originalDOTween);
			}
			_ownsDOTween = false;
		}
		foreach (Animator animator in _animators)
		{
			if (animator != null && animator.updateMode == AnimatorUpdateMode.Normal)
			{
				animator.updateMode = AnimatorUpdateMode.UnscaledTime;
			}
		}
		foreach (ParticleSystem particle in _particles)
		{
			if (particle != null)
			{
				ParticleSystem.MainModule main = particle.main;
				main.useUnscaledTime = true;
			}
		}
		foreach (PlayableDirector director in _directors)
		{
			if (director != null && director.timeUpdateMode == DirectorUpdateMode.GameTime)
			{
				director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
			}
		}
		_animators.Clear();
		_particles.Clear();
		_directors.Clear();
	}

	public static float Sanitize(float value)
	{
		if (!float.IsNaN(value) && !float.IsInfinity(value))
		{
			return Mathf.Clamp(value, 0f, 10f);
		}
		return 1f;
	}
}
